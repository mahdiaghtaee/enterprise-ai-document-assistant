using Npgsql;

namespace EnterpriseDocumentAssistant.Api.Documents;

public sealed class OwnershipAwareIngestionJobRepository : IIngestionJobRepository
{
    private readonly PostgresIngestionJobRepository _inner;
    private readonly string _connectionString;

    public OwnershipAwareIngestionJobRepository(
        PostgresIngestionJobRepository inner,
        IConfiguration configuration)
    {
        _inner = inner;
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");
    }

    public async Task<DocumentIngestionCreationResult> CreateDocumentWithPendingJobAsync(
        CreateDocumentIngestionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTimeOffset.UtcNow;
        var document = new DocumentRecord(
            Guid.NewGuid(),
            NormalizeRequired(request.FileName, nameof(request.FileName)),
            NormalizeOptional(request.ContentType),
            request.SizeInBytes,
            NormalizeRequired(request.StoragePath, nameof(request.StoragePath)),
            "uploaded",
            now,
            DocumentOwnership.Normalize(request.OwnerId));

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await InsertDocumentAsync(connection, transaction, document, cancellationToken);
            var job = await InsertPendingJobAsync(
                connection,
                transaction,
                document.Id,
                request.MaxAttempts,
                now,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new DocumentIngestionCreationResult(document, job);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public Task<DocumentIngestionJob> CreatePendingAsync(
        Guid documentId,
        CancellationToken cancellationToken) =>
        _inner.CreatePendingAsync(documentId, cancellationToken);

    public Task<DocumentIngestionJob?> GetLatestForDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken) =>
        _inner.GetLatestForDocumentAsync(documentId, cancellationToken);

    public Task<DocumentIngestionJob?> ClaimNextAvailableAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        _inner.ClaimNextAvailableAsync(now, cancellationToken);

    public Task<DocumentIngestionJob> MarkCompletedAsync(
        long jobId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken) =>
        _inner.MarkCompletedAsync(jobId, completedAt, cancellationToken);

    public Task<DocumentIngestionJob> MarkFailedOrRetryAsync(
        long jobId,
        string errorCode,
        string errorSummary,
        bool retryable,
        DateTimeOffset retryAt,
        CancellationToken cancellationToken) =>
        _inner.MarkFailedOrRetryAsync(
            jobId,
            errorCode,
            errorSummary,
            retryable,
            retryAt,
            cancellationToken);

    public Task ReturnToPendingAsync(
        long jobId,
        DateTimeOffset availableAt,
        CancellationToken cancellationToken) =>
        _inner.ReturnToPendingAsync(jobId, availableAt, cancellationToken);

    public Task<int> RecoverAbandonedAsync(
        DateTimeOffset staleBefore,
        DateTimeOffset retryAt,
        CancellationToken cancellationToken) =>
        _inner.RecoverAbandonedAsync(staleBefore, retryAt, cancellationToken);

    private static async Task InsertDocumentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DocumentRecord document,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO documents
                (id, file_name, content_type, size_in_bytes, storage_path, status, created_at, owner_id)
            VALUES
                (@id, @fileName, @contentType, @sizeInBytes, @storagePath, @status, @createdAt, @ownerId);
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", document.Id);
        command.Parameters.AddWithValue("fileName", document.FileName);
        command.Parameters.AddWithValue(
            "contentType",
            document.ContentType is null ? DBNull.Value : document.ContentType);
        command.Parameters.AddWithValue("sizeInBytes", document.SizeInBytes);
        command.Parameters.AddWithValue("storagePath", document.StoragePath);
        command.Parameters.AddWithValue("status", document.Status);
        command.Parameters.AddWithValue("createdAt", document.CreatedAt);
        command.Parameters.AddWithValue("ownerId", document.OwnerId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<DocumentIngestionJob> InsertPendingJobAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid documentId,
        int maxAttempts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO document_ingestion_jobs
                (document_id, status, attempt_count, max_attempts, available_at, created_at, updated_at)
            VALUES
                (@documentId, 'Pending', 0, @maxAttempts, @availableAt, @createdAt, @updatedAt)
            RETURNING id,
                      document_id,
                      status,
                      attempt_count,
                      max_attempts,
                      available_at,
                      started_at,
                      completed_at,
                      failed_at,
                      last_error_code,
                      last_error_summary,
                      created_at,
                      updated_at;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("documentId", documentId);
        command.Parameters.AddWithValue("maxAttempts", maxAttempts);
        command.Parameters.AddWithValue("availableAt", now);
        command.Parameters.AddWithValue("createdAt", now);
        command.Parameters.AddWithValue("updatedAt", now);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The ingestion job insert did not return a row.");
        }

        return new DocumentIngestionJob(
            reader.GetInt64(0),
            reader.GetGuid(1),
            Enum.Parse<DocumentIngestionStatus>(reader.GetString(2), ignoreCase: false),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
            reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
            reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.GetFieldValue<DateTimeOffset>(11),
            reader.GetFieldValue<DateTimeOffset>(12));
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
