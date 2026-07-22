using EnterpriseDocumentAssistant.Api.Documents;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace EnterpriseDocumentAssistant.Api.Tests;

public sealed class PostgresIngestionJobRepositoryIntegrationTests
{
    private static readonly string? ConnectionString =
        Environment.GetEnvironmentVariable("POSTGRES_TEST_CONNECTION_STRING");

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Document_and_pending_job_are_created_in_one_transaction()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await EnsureSchemaAsync();
        var repository = CreateRepository();
        var marker = Guid.NewGuid().ToString("N");

        var result = await repository.CreateDocumentWithPendingJobAsync(
            new CreateDocumentIngestionRequest(
                $"atomic-{marker}.txt",
                "text/plain",
                128,
                $"/tmp/{marker}.txt"),
            CancellationToken.None);

        Assert.Equal(result.Document.Id, result.Job.DocumentId);
        Assert.Equal(DocumentIngestionStatus.Pending, result.Job.Status);
        Assert.Equal(0, result.Job.AttemptCount);
        Assert.Equal(DocumentIngestionDefaults.MaxAttempts, result.Job.MaxAttempts);
        Assert.Equal(1, await CountDocumentsAsync(result.Document.Id));
        Assert.Equal(1, await CountJobsAsync(result.Document.Id));
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Job_constraint_failure_rolls_back_document_insert()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await EnsureSchemaAsync();
        var repository = CreateRepository();
        var marker = Guid.NewGuid().ToString("N");
        var storagePath = $"/tmp/rollback-{marker}.txt";

        await Assert.ThrowsAsync<PostgresException>(() =>
            repository.CreateDocumentWithPendingJobAsync(
                new CreateDocumentIngestionRequest(
                    $"rollback-{marker}.txt",
                    "text/plain",
                    128,
                    storagePath,
                    MaxAttempts: 0),
                CancellationToken.None));

        Assert.Equal(0, await CountDocumentsByStoragePathAsync(storagePath));
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Duplicate_active_job_returns_controlled_conflict()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await EnsureSchemaAsync();
        var repository = CreateRepository();
        var marker = Guid.NewGuid().ToString("N");
        var created = await repository.CreateDocumentWithPendingJobAsync(
            new CreateDocumentIngestionRequest(
                $"duplicate-{marker}.txt",
                "text/plain",
                64,
                $"/tmp/duplicate-{marker}.txt"),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ActiveDocumentIngestionJobException>(() =>
            repository.CreatePendingAsync(created.Document.Id, CancellationToken.None));

        Assert.Equal(created.Document.Id, exception.DocumentId);
        Assert.Equal(1, await CountJobsAsync(created.Document.Id));
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Pre_cancelled_operation_does_not_create_rows()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await EnsureSchemaAsync();
        var repository = CreateRepository();
        var marker = Guid.NewGuid().ToString("N");
        var storagePath = $"/tmp/cancel-{marker}.txt";
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.CreateDocumentWithPendingJobAsync(
                new CreateDocumentIngestionRequest(
                    $"cancel-{marker}.txt",
                    "text/plain",
                    64,
                    storagePath),
                cancellation.Token));

        Assert.Equal(0, await CountDocumentsByStoragePathAsync(storagePath));
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
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static Task<long> CountDocumentsAsync(Guid documentId) =>
        ExecuteCountAsync("SELECT count(*) FROM documents WHERE id = @value;", documentId);

    private static Task<long> CountDocumentsByStoragePathAsync(string storagePath) =>
        ExecuteCountAsync("SELECT count(*) FROM documents WHERE storage_path = @value;", storagePath);

    private static Task<long> CountJobsAsync(Guid documentId) =>
        ExecuteCountAsync("SELECT count(*) FROM document_ingestion_jobs WHERE document_id = @value;", documentId);

    private static async Task<long> ExecuteCountAsync(string sql, object value)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("value", value);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }
}
