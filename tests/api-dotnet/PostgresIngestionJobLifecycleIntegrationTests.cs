using EnterpriseDocumentAssistant.Api.Documents;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace EnterpriseDocumentAssistant.Api.Tests;

public sealed class PostgresIngestionJobLifecycleIntegrationTests
{
    private static readonly string? ConnectionString =
        Environment.GetEnvironmentVariable("POSTGRES_TEST_CONNECTION_STRING");

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Available_job_is_claimed_and_completed()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await EnsureSchemaAsync();
        await ResetDataAsync();
        var repository = CreateRepository();
        var created = await CreateDocumentAsync(repository, "claim");
        var now = DateTimeOffset.UtcNow;

        var claimed = await repository.ClaimNextAvailableAsync(now, CancellationToken.None);

        Assert.NotNull(claimed);
        Assert.Equal(created.Job.Id, claimed!.Id);
        Assert.Equal(DocumentIngestionStatus.Processing, claimed.Status);
        Assert.Equal(1, claimed.AttemptCount);
        Assert.NotNull(claimed.StartedAt);

        var completed = await repository.MarkCompletedAsync(
            claimed.Id,
            now.AddSeconds(1),
            CancellationToken.None);

        Assert.Equal(DocumentIngestionStatus.Completed, completed.Status);
        Assert.NotNull(completed.CompletedAt);

        var latest = await repository.GetLatestForDocumentAsync(
            created.Document.Id,
            CancellationToken.None);
        Assert.Equal(DocumentIngestionStatus.Completed, latest!.Status);
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Retryable_failure_is_requeued_until_attempts_are_exhausted()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await EnsureSchemaAsync();
        await ResetDataAsync();
        var repository = CreateRepository();
        var created = await CreateDocumentAsync(repository, "retry", maxAttempts: 2);
        var firstClaim = await repository.ClaimNextAvailableAsync(
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Equal(created.Job.Id, firstClaim!.Id);

        var pending = await repository.MarkFailedOrRetryAsync(
            firstClaim.Id,
            "transient-test",
            "A retryable test failure occurred.",
            retryable: true,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Equal(DocumentIngestionStatus.Pending, pending.Status);
        Assert.Equal(1, pending.AttemptCount);
        Assert.Null(pending.StartedAt);

        var secondClaim = await repository.ClaimNextAvailableAsync(
            DateTimeOffset.UtcNow.AddSeconds(1),
            CancellationToken.None);
        Assert.Equal(2, secondClaim!.AttemptCount);

        var failed = await repository.MarkFailedOrRetryAsync(
            secondClaim.Id,
            "transient-test",
            "The retry limit was reached.",
            retryable: true,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Equal(DocumentIngestionStatus.Failed, failed.Status);
        Assert.Equal(2, failed.AttemptCount);
        Assert.NotNull(failed.FailedAt);
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Abandoned_processing_job_is_returned_to_the_queue()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await EnsureSchemaAsync();
        await ResetDataAsync();
        var repository = CreateRepository();
        var created = await CreateDocumentAsync(repository, "recovery");
        var claimed = await repository.ClaimNextAvailableAsync(
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Equal(created.Job.Id, claimed!.Id);
        Assert.Equal(DocumentIngestionStatus.Processing, claimed.Status);

        await SetJobStartedAtAsync(claimed.Id, DateTimeOffset.UtcNow.AddHours(-1));

        var recoveredCount = await repository.RecoverAbandonedAsync(
            DateTimeOffset.UtcNow.AddMinutes(-30),
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Equal(1, recoveredCount);

        var recovered = await repository.GetLatestForDocumentAsync(
            created.Document.Id,
            CancellationToken.None);
        Assert.Equal(DocumentIngestionStatus.Pending, recovered!.Status);
        Assert.Equal("worker-timeout", recovered.LastErrorCode);
        Assert.Null(recovered.StartedAt);
    }

    private static Task<DocumentIngestionCreationResult> CreateDocumentAsync(
        PostgresIngestionJobRepository repository,
        string prefix,
        int maxAttempts = DocumentIngestionDefaults.MaxAttempts)
    {
        var marker = Guid.NewGuid().ToString("N");
        return repository.CreateDocumentWithPendingJobAsync(
            new CreateDocumentIngestionRequest(
                $"{prefix}-{marker}.txt",
                "text/plain",
                32,
                $"/tmp/{prefix}-{marker}.txt",
                maxAttempts),
            CancellationToken.None);
    }

    private static PostgresIngestionJobRepository CreateRepository()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = ConnectionString
            })
            .Build();

        return new PostgresIngestionJobRepository(configuration);
    }

    private static async Task EnsureSchemaAsync()
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS documents
            (
                id UUID PRIMARY KEY,
                file_name TEXT NOT NULL,
                content_type TEXT NULL,
                size_in_bytes BIGINT NOT NULL,
                storage_path TEXT NOT NULL,
                status TEXT NOT NULL,
                created_at TIMESTAMPTZ NOT NULL
            );

            CREATE TABLE IF NOT EXISTS document_ingestion_jobs
            (
                id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                document_id UUID NOT NULL REFERENCES documents (id) ON DELETE CASCADE,
                status TEXT NOT NULL DEFAULT 'Pending',
                attempt_count INTEGER NOT NULL DEFAULT 0,
                max_attempts INTEGER NOT NULL DEFAULT 3,
                available_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                started_at TIMESTAMPTZ NULL,
                completed_at TIMESTAMPTZ NULL,
                failed_at TIMESTAMPTZ NULL,
                last_error_code VARCHAR(100) NULL,
                last_error_summary VARCHAR(500) NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT ck_document_ingestion_jobs_status
                    CHECK (status IN ('Pending', 'Processing', 'Completed', 'Failed')),
                CONSTRAINT ck_document_ingestion_jobs_attempts
                    CHECK (attempt_count >= 0 AND max_attempts > 0 AND attempt_count <= max_attempts)
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_document_ingestion_jobs_active_document
                ON document_ingestion_jobs (document_id)
                WHERE status IN ('Pending', 'Processing');

            CREATE INDEX IF NOT EXISTS ix_document_ingestion_jobs_claim
                ON document_ingestion_jobs (available_at, created_at, id)
                WHERE status = 'Pending';
            """;

        await ExecuteNonQueryAsync(sql);
    }

    private static Task ResetDataAsync() =>
        ExecuteNonQueryAsync("TRUNCATE TABLE document_ingestion_jobs, documents RESTART IDENTITY CASCADE;");

    private static async Task SetJobStartedAtAsync(long jobId, DateTimeOffset startedAt)
    {
        const string sql = """
            UPDATE document_ingestion_jobs
            SET started_at = @startedAt,
                updated_at = @startedAt
            WHERE id = @jobId;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("jobId", jobId);
        command.Parameters.AddWithValue("startedAt", startedAt);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteNonQueryAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
