using System.Diagnostics;
using EnterpriseDocumentAssistant.Api.Observability;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EnterpriseDocumentAssistant.Api.Audit;

public sealed class AuditRetentionOptions
{
    public const string SectionName = "AuditRetention";

    public bool Enabled { get; set; }

    public int RetentionDays { get; set; } = 90;

    public int BatchSize { get; set; } = 1_000;

    public int MaxBatchesPerRun { get; set; } = 10;

    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);

    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMinutes(1);

    public void Validate()
    {
        if (RetentionDays is < 1 or > 3_650)
        {
            throw new InvalidOperationException("AuditRetention:RetentionDays must be between 1 and 3650.");
        }

        if (BatchSize is < 1 or > 10_000)
        {
            throw new InvalidOperationException("AuditRetention:BatchSize must be between 1 and 10000.");
        }

        if (MaxBatchesPerRun is < 1 or > 100)
        {
            throw new InvalidOperationException("AuditRetention:MaxBatchesPerRun must be between 1 and 100.");
        }

        if (Interval < TimeSpan.FromMinutes(1) || Interval > TimeSpan.FromDays(7))
        {
            throw new InvalidOperationException("AuditRetention:Interval must be between 1 minute and 7 days.");
        }

        if (InitialDelay < TimeSpan.Zero || InitialDelay > TimeSpan.FromHours(1))
        {
            throw new InvalidOperationException("AuditRetention:InitialDelay must be between zero and 1 hour.");
        }
    }
}

public interface IAuditMaintenanceRepository
{
    Task<int> ArchiveBeforeAsync(
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken);
}

public sealed class PostgresAuditMaintenanceRepository : IAuditMaintenanceRepository
{
    private readonly string _connectionString;

    public PostgresAuditMaintenanceRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("PostgresPrivileged")
            ?? throw new InvalidOperationException(
                "Audit retention requires ConnectionStrings:PostgresPrivileged.");
    }

    public async Task<int> ArchiveBeforeAsync(
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (batchSize is < 1 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        const string sql = "SELECT archive_audit_events(@cutoff, @batchSize);";
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("cutoff", cutoff);
        command.Parameters.AddWithValue("batchSize", batchSize);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }
}

public sealed class AuditRetentionWorker : BackgroundService
{
    private readonly IAuditMaintenanceRepository _repository;
    private readonly AuditRetentionOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuditRetentionWorker> _logger;

    public AuditRetentionWorker(
        IAuditMaintenanceRepository repository,
        IOptions<AuditRetentionOptions> options,
        TimeProvider timeProvider,
        ILogger<AuditRetentionWorker> logger)
    {
        _repository = repository;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _options.Validate();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Audit retention is disabled.");
            return;
        }

        if (_options.InitialDelay > TimeSpan.Zero)
        {
            await Task.Delay(_options.InitialDelay, _timeProvider, stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            await Task.Delay(_options.Interval, _timeProvider, stoppingToken);
        }
    }

    internal async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var cutoff = _timeProvider.GetUtcNow().AddDays(-_options.RetentionDays);
        var stopwatch = Stopwatch.StartNew();
        var totalArchived = 0;
        ApplicationTelemetry.AuditArchiveRuns.Add(1);

        try
        {
            for (var batch = 0; batch < _options.MaxBatchesPerRun; batch++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var archived = await _repository.ArchiveBeforeAsync(
                    cutoff,
                    _options.BatchSize,
                    cancellationToken);
                totalArchived += archived;

                if (archived < _options.BatchSize)
                {
                    break;
                }
            }

            if (totalArchived > 0)
            {
                ApplicationTelemetry.AuditArchivedEvents.Add(totalArchived);
                _logger.LogInformation(
                    "Archived {ArchivedAuditEventCount} audit events older than retention cutoff {AuditRetentionCutoffUtc}.",
                    totalArchived,
                    cutoff);
            }

            return totalArchived;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ApplicationTelemetry.AuditArchiveFailures.Add(1);
            _logger.LogError(
                exception,
                "Audit retention failed for cutoff {AuditRetentionCutoffUtc}.",
                cutoff);
            return 0;
        }
        finally
        {
            stopwatch.Stop();
            ApplicationTelemetry.AuditArchiveDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
        }
    }
}
