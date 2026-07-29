using System.Diagnostics;
using EnterpriseDocumentAssistant.Api.Observability;
using Microsoft.Extensions.Options;

namespace EnterpriseDocumentAssistant.Api.Documents;

public sealed class DocumentIngestionWorkerOptions
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(15);

    public TimeSpan ProcessingTimeout { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan RecoveryInterval { get; set; } = TimeSpan.FromMinutes(1);
}

public sealed class DocumentIngestionWorker : BackgroundService
{
    private readonly IIngestionJobRepository _jobRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentIngestionProcessor _processor;
    private readonly DocumentIngestionWorkerOptions _options;
    private readonly ILogger<DocumentIngestionWorker> _logger;

    public DocumentIngestionWorker(
        IIngestionJobRepository jobRepository,
        IDocumentRepository documentRepository,
        IDocumentIngestionProcessor processor,
        IOptions<DocumentIngestionWorkerOptions> options,
        ILogger<DocumentIngestionWorker> logger)
    {
        _jobRepository = jobRepository;
        _documentRepository = documentRepository;
        _processor = processor;
        _options = options.Value;
        _logger = logger;

        ValidateOptions(_options);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextRecoveryAt = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                if (now >= nextRecoveryAt)
                {
                    var recovered = await _jobRepository.RecoverAbandonedAsync(
                        now - _options.ProcessingTimeout,
                        now,
                        stoppingToken);

                    if (recovered > 0)
                    {
                        ApplicationTelemetry.IngestionRecovered.Add(recovered);
                        _logger.LogWarning("Recovered {RecoveredJobCount} abandoned ingestion jobs.", recovered);
                    }

                    nextRecoveryAt = now + _options.RecoveryInterval;
                }

                var job = await _jobRepository.ClaimNextAvailableAsync(now, stoppingToken);
                if (job is null)
                {
                    await Task.Delay(_options.PollInterval, stoppingToken);
                    continue;
                }

                await ProcessJobAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "The ingestion worker loop failed. Processing will resume after the polling delay.");
                await Task.Delay(_options.PollInterval, stoppingToken);
            }
        }
    }

    private async Task ProcessJobAsync(
        DocumentIngestionJob job,
        CancellationToken stoppingToken)
    {
        using var activity = ApplicationTelemetry.ActivitySource.StartActivity(
            "document.ingestion.process",
            ActivityKind.Consumer);
        activity?.SetTag("ingestion.job.id", job.Id);
        activity?.SetTag("document.id", job.DocumentId);
        activity?.SetTag("ingestion.attempt", job.AttemptCount);
        var stopwatch = Stopwatch.StartNew();

        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["IngestionJobId"] = job.Id,
            ["DocumentId"] = job.DocumentId,
            ["AttemptCount"] = job.AttemptCount
        });

        var document = _documentRepository.GetById(
            job.DocumentId,
            bypassTenantIsolation: true);
        if (document is null)
        {
            await _jobRepository.MarkFailedOrRetryAsync(
                job.Id,
                "document-record-not-found",
                "The document metadata row no longer exists.",
                retryable: false,
                DateTimeOffset.UtcNow,
                CancellationToken.None);

            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, "document-record-not-found");
            ApplicationTelemetry.IngestionFailed.Add(
                1,
                ApplicationTelemetry.Tag("error.code", "document-record-not-found"));
            ApplicationTelemetry.IngestionDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
            _logger.LogError(
                "Ingestion job {JobId} failed because document {DocumentId} does not exist.",
                job.Id,
                job.DocumentId);
            return;
        }

        activity?.SetTag("tenant.id", document.TenantId);
        activity?.SetTag("document.content_type", document.ContentType);

        try
        {
            _documentRepository.UpdateStatus(document.Id, "processing");
            var result = await _processor.ProcessAsync(document, stoppingToken);
            await _jobRepository.MarkCompletedAsync(
                job.Id,
                DateTimeOffset.UtcNow,
                stoppingToken);
            _documentRepository.UpdateStatus(document.Id, "indexed");

            stopwatch.Stop();
            activity?.SetTag("ingestion.chunk_count", result.ChunkCount);
            activity?.SetTag("ingestion.vector_count", result.VectorCount);
            activity?.SetStatus(ActivityStatusCode.Ok);
            ApplicationTelemetry.IngestionCompleted.Add(1);
            ApplicationTelemetry.IngestionDuration.Record(stopwatch.Elapsed.TotalMilliseconds);

            _logger.LogInformation(
                "Completed ingestion job {JobId} for document {DocumentId}: {ChunkCount} chunks and {VectorCount} vectors using {EmbeddingModel}.",
                job.Id,
                document.Id,
                result.ChunkCount,
                result.VectorCount,
                result.EmbeddingModel);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await _jobRepository.ReturnToPendingAsync(
                job.Id,
                DateTimeOffset.UtcNow,
                CancellationToken.None);
            _documentRepository.UpdateStatus(document.Id, "uploaded");
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, "worker-stopped");
            ApplicationTelemetry.IngestionRetried.Add(
                1,
                ApplicationTelemetry.Tag("reason", "worker-stopped"));
            ApplicationTelemetry.IngestionDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
        catch (DocumentIngestionProcessingException exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.ErrorCode);
            await RecordFailureAsync(
                job,
                document,
                exception.ErrorCode,
                exception.Message,
                exception.Retryable,
                stopwatch);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            _logger.LogError(
                exception,
                "Unexpected failure while processing ingestion job {JobId} for document {DocumentId}.",
                job.Id,
                document.Id);

            await RecordFailureAsync(
                job,
                document,
                "unhandled-processing-error",
                exception.Message,
                retryable: true,
                stopwatch);
        }
    }

    private async Task RecordFailureAsync(
        DocumentIngestionJob job,
        DocumentRecord document,
        string errorCode,
        string errorSummary,
        bool retryable,
        Stopwatch stopwatch)
    {
        var updatedJob = await _jobRepository.MarkFailedOrRetryAsync(
            job.Id,
            errorCode,
            errorSummary,
            retryable,
            DateTimeOffset.UtcNow + _options.RetryDelay,
            CancellationToken.None);

        var documentStatus = updatedJob.Status == DocumentIngestionStatus.Pending
            ? "retry-pending"
            : "failed";
        _documentRepository.UpdateStatus(document.Id, documentStatus);
        stopwatch.Stop();

        if (updatedJob.Status == DocumentIngestionStatus.Pending)
        {
            ApplicationTelemetry.IngestionRetried.Add(
                1,
                ApplicationTelemetry.Tag("error.code", updatedJob.LastErrorCode));
        }
        else
        {
            ApplicationTelemetry.IngestionFailed.Add(
                1,
                ApplicationTelemetry.Tag("error.code", updatedJob.LastErrorCode));
        }

        ApplicationTelemetry.IngestionDuration.Record(stopwatch.Elapsed.TotalMilliseconds);

        _logger.LogWarning(
            "Ingestion job {JobId} for document {DocumentId} moved to {Status} after attempt {AttemptCount}/{MaxAttempts}. Error: {ErrorCode}.",
            updatedJob.Id,
            document.Id,
            updatedJob.Status,
            updatedJob.AttemptCount,
            updatedJob.MaxAttempts,
            updatedJob.LastErrorCode);
    }

    private static void ValidateOptions(DocumentIngestionWorkerOptions options)
    {
        if (options.PollInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("IngestionWorker:PollInterval must be greater than zero.");
        }

        if (options.RetryDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException("IngestionWorker:RetryDelay cannot be negative.");
        }

        if (options.ProcessingTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("IngestionWorker:ProcessingTimeout must be greater than zero.");
        }

        if (options.RecoveryInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("IngestionWorker:RecoveryInterval must be greater than zero.");
        }
    }
}
