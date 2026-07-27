using EnterpriseDocumentAssistant.Api.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EnterpriseDocumentAssistant.Api.Tests;

public sealed class DocumentIngestionWorkerTests
{
    [Fact]
    public async Task Worker_completes_claimed_job_and_marks_document_indexed()
    {
        var documents = new InMemoryDocumentRepository();
        var document = documents.Add("worker-success.txt", "text/plain", 12, "/tmp/worker-success.txt");
        var jobs = new WorkerJobRepository(CreateProcessingJob(document.Id))
        {
            RecoveryCount = 1
        };
        var processor = new StubProcessor((_, _) => Task.FromResult(SuccessResult()));
        var worker = CreateWorker(jobs, documents, processor);

        await worker.StartAsync(CancellationToken.None);
        var transition = await jobs.Transition.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(DocumentIngestionStatus.Completed, transition.Status);
        Assert.Equal(1, jobs.RecoveryCalls);
        Assert.Equal("indexed", documents.GetById(document.Id)!.Status);
        Assert.Equal(1, processor.CallCount);
    }

    [Fact]
    public async Task Worker_marks_known_permanent_processing_failure_as_failed()
    {
        var documents = new InMemoryDocumentRepository();
        var document = documents.Add("worker-failed.txt", "text/plain", 12, "/tmp/worker-failed.txt");
        var jobs = new WorkerJobRepository(CreateProcessingJob(document.Id))
        {
            FailureStatus = DocumentIngestionStatus.Failed
        };
        var processor = new StubProcessor((_, _) => throw new DocumentIngestionProcessingException(
            "unsupported-content",
            "The document cannot be processed."));
        var worker = CreateWorker(jobs, documents, processor);

        await worker.StartAsync(CancellationToken.None);
        var transition = await jobs.Transition.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(DocumentIngestionStatus.Failed, transition.Status);
        Assert.Equal("unsupported-content", jobs.LastErrorCode);
        Assert.False(jobs.LastRetryable);
        Assert.Equal("failed", documents.GetById(document.Id)!.Status);
    }

    [Fact]
    public async Task Worker_requeues_unexpected_failure_when_attempts_remain()
    {
        var documents = new InMemoryDocumentRepository();
        var document = documents.Add("worker-retry.txt", "text/plain", 12, "/tmp/worker-retry.txt");
        var jobs = new WorkerJobRepository(CreateProcessingJob(document.Id))
        {
            FailureStatus = DocumentIngestionStatus.Pending
        };
        var processor = new StubProcessor((_, _) => throw new InvalidOperationException("Transient storage failure."));
        var worker = CreateWorker(jobs, documents, processor);

        await worker.StartAsync(CancellationToken.None);
        var transition = await jobs.Transition.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(DocumentIngestionStatus.Pending, transition.Status);
        Assert.Equal("unhandled-processing-error", jobs.LastErrorCode);
        Assert.True(jobs.LastRetryable);
        Assert.Equal("retry-pending", documents.GetById(document.Id)!.Status);
    }

    [Fact]
    public async Task Worker_fails_job_when_document_metadata_is_missing()
    {
        var missingDocumentId = Guid.NewGuid();
        var documents = new InMemoryDocumentRepository();
        var jobs = new WorkerJobRepository(CreateProcessingJob(missingDocumentId))
        {
            FailureStatus = DocumentIngestionStatus.Failed
        };
        var processor = new StubProcessor((_, _) => Task.FromResult(SuccessResult()));
        var worker = CreateWorker(jobs, documents, processor);

        await worker.StartAsync(CancellationToken.None);
        var transition = await jobs.Transition.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(DocumentIngestionStatus.Failed, transition.Status);
        Assert.Equal("document-record-not-found", jobs.LastErrorCode);
        Assert.False(jobs.LastRetryable);
        Assert.Equal(0, processor.CallCount);
    }

    [Fact]
    public async Task Worker_returns_interrupted_job_to_pending_on_shutdown()
    {
        var documents = new InMemoryDocumentRepository();
        var document = documents.Add("worker-cancel.txt", "text/plain", 12, "/tmp/worker-cancel.txt");
        var jobs = new WorkerJobRepository(CreateProcessingJob(document.Id));
        var processorStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = new StubProcessor(async (_, cancellationToken) =>
        {
            processorStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return SuccessResult();
        });
        var worker = CreateWorker(jobs, documents, processor);

        await worker.StartAsync(CancellationToken.None);
        await processorStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);
        var transition = await jobs.Transition.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(DocumentIngestionStatus.Pending, transition.Status);
        Assert.True(jobs.ReturnToPendingCalled);
        Assert.Equal("uploaded", documents.GetById(document.Id)!.Status);
    }

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void Worker_rejects_invalid_options(DocumentIngestionWorkerOptions options, string expectedMessage)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new DocumentIngestionWorker(
            new WorkerJobRepository(),
            new InMemoryDocumentRepository(),
            new StubProcessor((_, _) => Task.FromResult(SuccessResult())),
            Options.Create(options),
            NullLogger<DocumentIngestionWorker>.Instance));

        Assert.Equal(expectedMessage, exception.Message);
    }

    public static IEnumerable<object[]> InvalidOptions()
    {
        yield return
        [
            new DocumentIngestionWorkerOptions { PollInterval = TimeSpan.Zero },
            "IngestionWorker:PollInterval must be greater than zero."
        ];
        yield return
        [
            new DocumentIngestionWorkerOptions { RetryDelay = TimeSpan.FromSeconds(-1) },
            "IngestionWorker:RetryDelay cannot be negative."
        ];
        yield return
        [
            new DocumentIngestionWorkerOptions { ProcessingTimeout = TimeSpan.Zero },
            "IngestionWorker:ProcessingTimeout must be greater than zero."
        ];
        yield return
        [
            new DocumentIngestionWorkerOptions { RecoveryInterval = TimeSpan.Zero },
            "IngestionWorker:RecoveryInterval must be greater than zero."
        ];
    }

    private static DocumentIngestionWorker CreateWorker(
        WorkerJobRepository jobs,
        IDocumentRepository documents,
        IDocumentIngestionProcessor processor)
    {
        return new DocumentIngestionWorker(
            jobs,
            documents,
            processor,
            Options.Create(new DocumentIngestionWorkerOptions
            {
                PollInterval = TimeSpan.FromMilliseconds(2),
                RetryDelay = TimeSpan.FromMilliseconds(5),
                ProcessingTimeout = TimeSpan.FromMinutes(1),
                RecoveryInterval = TimeSpan.FromMinutes(1)
            }),
            NullLogger<DocumentIngestionWorker>.Instance);
    }

    private static DocumentIngestionProcessingResult SuccessResult() =>
        new(24, 2, "deterministic-local", 2, 8);

    private static DocumentIngestionJob CreateProcessingJob(Guid documentId)
    {
        var now = DateTimeOffset.UtcNow;
        return new DocumentIngestionJob(
            41,
            documentId,
            DocumentIngestionStatus.Processing,
            1,
            3,
            now,
            now,
            null,
            null,
            null,
            null,
            now,
            now);
    }

    private sealed class StubProcessor : IDocumentIngestionProcessor
    {
        private readonly Func<DocumentRecord, CancellationToken, Task<DocumentIngestionProcessingResult>> _process;

        public StubProcessor(Func<DocumentRecord, CancellationToken, Task<DocumentIngestionProcessingResult>> process)
        {
            _process = process;
        }

        public int CallCount { get; private set; }

        public Task<DocumentIngestionProcessingResult> ProcessAsync(
            DocumentRecord document,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return _process(document, cancellationToken);
        }
    }

    private sealed class WorkerJobRepository : IIngestionJobRepository
    {
        private readonly Queue<DocumentIngestionJob> _jobs = new();

        public WorkerJobRepository(params DocumentIngestionJob[] jobs)
        {
            foreach (var job in jobs)
            {
                _jobs.Enqueue(job);
            }
        }

        public TaskCompletionSource<DocumentIngestionJob> Transition { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int RecoveryCount { get; init; }

        public int RecoveryCalls { get; private set; }

        public DocumentIngestionStatus FailureStatus { get; init; } = DocumentIngestionStatus.Failed;

        public string? LastErrorCode { get; private set; }

        public bool LastRetryable { get; private set; }

        public bool ReturnToPendingCalled { get; private set; }

        public Task<DocumentIngestionCreationResult> CreateDocumentWithPendingJobAsync(
            CreateDocumentIngestionRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DocumentIngestionJob> CreatePendingAsync(
            Guid documentId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DocumentIngestionJob?> GetLatestForDocumentAsync(
            Guid documentId,
            CancellationToken cancellationToken) => Task.FromResult<DocumentIngestionJob?>(null);

        public Task<DocumentIngestionJob?> ClaimNextAvailableAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            lock (_jobs)
            {
                return Task.FromResult(_jobs.Count == 0 ? null : _jobs.Dequeue());
            }
        }

        public Task<DocumentIngestionJob> MarkCompletedAsync(
            long jobId,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken)
        {
            var completed = CreateTransition(jobId, DocumentIngestionStatus.Completed, completedAt);
            Transition.TrySetResult(completed);
            return Task.FromResult(completed);
        }

        public Task<DocumentIngestionJob> MarkFailedOrRetryAsync(
            long jobId,
            string errorCode,
            string errorSummary,
            bool retryable,
            DateTimeOffset retryAt,
            CancellationToken cancellationToken)
        {
            LastErrorCode = errorCode;
            LastRetryable = retryable;
            var updated = CreateTransition(jobId, FailureStatus, retryAt) with
            {
                LastErrorCode = errorCode,
                LastErrorSummary = errorSummary,
                FailedAt = FailureStatus == DocumentIngestionStatus.Failed ? DateTimeOffset.UtcNow : null
            };
            Transition.TrySetResult(updated);
            return Task.FromResult(updated);
        }

        public Task ReturnToPendingAsync(
            long jobId,
            DateTimeOffset availableAt,
            CancellationToken cancellationToken)
        {
            ReturnToPendingCalled = true;
            Transition.TrySetResult(CreateTransition(jobId, DocumentIngestionStatus.Pending, availableAt));
            return Task.CompletedTask;
        }

        public Task<int> RecoverAbandonedAsync(
            DateTimeOffset staleBefore,
            DateTimeOffset retryAt,
            CancellationToken cancellationToken)
        {
            RecoveryCalls++;
            return Task.FromResult(RecoveryCalls == 1 ? RecoveryCount : 0);
        }

        private static DocumentIngestionJob CreateTransition(
            long jobId,
            DocumentIngestionStatus status,
            DateTimeOffset now)
        {
            return new DocumentIngestionJob(
                jobId,
                Guid.Empty,
                status,
                1,
                3,
                now,
                status == DocumentIngestionStatus.Pending ? null : now,
                status == DocumentIngestionStatus.Completed ? now : null,
                status == DocumentIngestionStatus.Failed ? now : null,
                null,
                null,
                now,
                now);
        }
    }
}
