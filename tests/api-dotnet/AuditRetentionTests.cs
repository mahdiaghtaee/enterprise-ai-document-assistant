using EnterpriseDocumentAssistant.Api.Audit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EnterpriseDocumentAssistant.Api.Tests;

public sealed class AuditRetentionTests
{
    [Fact]
    public async Task RunOnce_archives_bounded_batches_until_short_batch()
    {
        var repository = new StubAuditMaintenanceRepository(3, 2);
        var now = new DateTimeOffset(2026, 8, 11, 7, 0, 0, TimeSpan.Zero);
        var worker = CreateWorker(
            repository,
            new AuditRetentionOptions
            {
                Enabled = true,
                RetentionDays = 90,
                BatchSize = 3,
                MaxBatchesPerRun = 5,
                Interval = TimeSpan.FromHours(1),
                InitialDelay = TimeSpan.Zero
            },
            new FixedTimeProvider(now));

        var archived = await worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(5, archived);
        Assert.Equal(2, repository.Calls.Count);
        Assert.All(repository.Calls, call => Assert.Equal(3, call.BatchSize));
        Assert.All(repository.Calls, call => Assert.Equal(now.AddDays(-90), call.Cutoff));
    }

    [Fact]
    public async Task RunOnce_stops_at_maximum_batch_count()
    {
        var repository = new StubAuditMaintenanceRepository(2, 2, 2, 2);
        var worker = CreateWorker(
            repository,
            new AuditRetentionOptions
            {
                Enabled = true,
                RetentionDays = 30,
                BatchSize = 2,
                MaxBatchesPerRun = 2,
                Interval = TimeSpan.FromHours(1),
                InitialDelay = TimeSpan.Zero
            },
            new FixedTimeProvider(DateTimeOffset.UtcNow));

        var archived = await worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(4, archived);
        Assert.Equal(2, repository.Calls.Count);
    }

    [Fact]
    public async Task RunOnce_maps_repository_failure_to_controlled_zero_result()
    {
        var repository = new ThrowingAuditMaintenanceRepository();
        var worker = CreateWorker(
            repository,
            ValidOptions(),
            new FixedTimeProvider(DateTimeOffset.UtcNow));

        var archived = await worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, archived);
    }

    [Fact]
    public async Task RunOnce_propagates_requested_cancellation()
    {
        var worker = CreateWorker(
            new StubAuditMaintenanceRepository(1),
            ValidOptions(),
            new FixedTimeProvider(DateTimeOffset.UtcNow));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            worker.RunOnceAsync(cancellation.Token));
    }

    [Theory]
    [InlineData(0, 1000, 10)]
    [InlineData(3651, 1000, 10)]
    [InlineData(90, 0, 10)]
    [InlineData(90, 10001, 10)]
    [InlineData(90, 1000, 0)]
    [InlineData(90, 1000, 101)]
    public void Options_reject_unsafe_bounds(int retentionDays, int batchSize, int maxBatches)
    {
        var options = ValidOptions();
        options.RetentionDays = retentionDays;
        options.BatchSize = batchSize;
        options.MaxBatchesPerRun = maxBatches;

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    private static AuditRetentionWorker CreateWorker(
        IAuditMaintenanceRepository repository,
        AuditRetentionOptions options,
        TimeProvider timeProvider) =>
        new(
            repository,
            Options.Create(options),
            timeProvider,
            NullLogger<AuditRetentionWorker>.Instance);

    private static AuditRetentionOptions ValidOptions() => new()
    {
        Enabled = true,
        RetentionDays = 90,
        BatchSize = 1000,
        MaxBatchesPerRun = 10,
        Interval = TimeSpan.FromHours(24),
        InitialDelay = TimeSpan.Zero
    };

    private sealed class StubAuditMaintenanceRepository : IAuditMaintenanceRepository
    {
        private readonly Queue<int> _results;

        public StubAuditMaintenanceRepository(params int[] results)
        {
            _results = new Queue<int>(results);
        }

        public List<(DateTimeOffset Cutoff, int BatchSize)> Calls { get; } = [];

        public Task<int> ArchiveBeforeAsync(
            DateTimeOffset cutoff,
            int batchSize,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add((cutoff, batchSize));
            return Task.FromResult(_results.Count == 0 ? 0 : _results.Dequeue());
        }
    }

    private sealed class ThrowingAuditMaintenanceRepository : IAuditMaintenanceRepository
    {
        public Task<int> ArchiveBeforeAsync(
            DateTimeOffset cutoff,
            int batchSize,
            CancellationToken cancellationToken) =>
            throw new Npgsql.NpgsqlException("simulated retention failure");
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
