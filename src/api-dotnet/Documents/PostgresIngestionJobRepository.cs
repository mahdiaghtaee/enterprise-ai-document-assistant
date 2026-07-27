using Npgsql;

namespace EnterpriseDocumentAssistant.Api.Documents;

public sealed class PostgresIngestionJobRepository : IIngestionJobRepository
{
    private readonly string _connectionString;

    public PostgresIngestionJobRepository(IConfiguration configuration)
    {
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
            now);

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

    public async Task<DocumentIngestionJob> CreatePendingAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var job = await InsertPendingJobAsync(
                connection,
                transaction,
                documentId,
                DocumentIngestionDefaults.MaxAttempts,
                now,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return job;
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.UniqueViolation
                && exception.ConstraintName == "ux_document_ingestion_jobs_active_document")
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new ActiveDocumentIngestionJobException(documentId, exception);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<DocumentIngestionJob?> GetLatestForDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id,
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
                   updated_at
            FROM document_ingestion_jobs
            WHERE document_id = @documentId
            ORDER BY created_at DESC, id DESC
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("documentId", documentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? ReadJob(reader) : null;
    }

    public async Task<DocumentIngestionJob?> ClaimNextAvailableAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH candidate AS
            (
                SELECT id
                FROM document_ingestion_jobs
                WHERE status = 'Pending'
                  AND available_at <= @now
                  AND attempt_count < max_attempts
                ORDER BY available_at, created_at, id
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            UPDATE document_ingestion_jobs AS job
            SET status = 'Processing',
                attempt_count = job.attempt_count + 1,
                started_at = @now,
                completed_at = NULL,
                failed_at = NULL,
                last_error_code = NULL,
                last_error_summary = NULL,
                updated_at = @now
            FROM candidate
            WHERE job.id = candidate.id
            RETURNING job.id,
                      job.document_id,
                      job.status,
                      job.attempt_count,
                      job.max_attempts,
                      job.available_at,
                      job.started_at,
                      job.completed_at,
                      job.failed_at,
                      job.last_error_code,
                      job.last_error_summary,
                      job.created_at,
                      job.updated_at;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("now", now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var job = ReadJob(reader);
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return job;
    }

    public Task<DocumentIngestionJob> MarkCompletedAsync(
        long jobId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE document_ingestion_jobs
            SET status = 'Completed',
                completed_at = @completedAt,
                failed_at = NULL,
                last_error_code = NULL,
                last_error_summary = NULL,
                updated_at = @completedAt
            WHERE id = @jobId
              AND status = 'Processing'
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

        return ExecuteRequiredJobUpdateAsync(sql, command =>
        {
            command.Parameters.AddWithValue("jobId", jobId);
            command.Parameters.AddWithValue("completedAt", completedAt);
        }, cancellationToken);
    }

    public Task<DocumentIngestionJob> MarkFailedOrRetryAsync(
        long jobId,
        string errorCode,
        string errorSummary,
        bool retryable,
        DateTimeOffset retryAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE document_ingestion_jobs
            SET status = CASE
                    WHEN @retryable AND attempt_count < max_attempts THEN 'Pending'
                    ELSE 'Failed'
                END,
                available_at = CASE
                    WHEN @retryable AND attempt_count < max_attempts THEN @retryAt
                    ELSE available_at
                END,
                started_at = CASE
                    WHEN @retryable AND attempt_count < max_attempts THEN NULL
                    ELSE started_at
                END,
                completed_at = NULL,
                failed_at = CASE
                    WHEN @retryable AND attempt_count < max_attempts THEN NULL
                    ELSE @now
                END,
                last_error_code = @errorCode,
                last_error_summary = @errorSummary,
                updated_at = @now
            WHERE id = @jobId
              AND status = 'Processing'
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

        var now = DateTimeOffset.UtcNow;
        return ExecuteRequiredJobUpdateAsync(sql, command =>
        {
            command.Parameters.AddWithValue("jobId", jobId);
            command.Parameters.AddWithValue("retryable", retryable);
            command.Parameters.AddWithValue("retryAt", retryAt);
            command.Parameters.AddWithValue("now", now);
            command.Parameters.AddWithValue("errorCode", NormalizeError(errorCode, 100));
            command.Parameters.AddWithValue("errorSummary", NormalizeError(errorSummary, 500));
        }, cancellationToken);
    }

    public async Task ReturnToPendingAsync(
        long jobId,
        DateTimeOffset availableAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE document_ingestion_jobs
            SET status = 'Pending',
                attempt_count = GREATEST(attempt_count - 1, 0),
                available_at = @availableAt,
                started_at = NULL,
                completed_at = NULL,
                failed_at = NULL,
                last_error_code = 'worker-stopped',
                last_error_summary = 'Processing was interrupted by application shutdown and was returned to the queue.',
                updated_at = @availableAt
            WHERE id = @jobId
              AND status = 'Processing';
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("jobId", jobId);
        command.Parameters.AddWithValue("availableAt", availableAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> RecoverAbandonedAsync(
        DateTimeOffset staleBefore,
        DateTimeOffset retryAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE document_ingestion_jobs
            SET status = CASE
                    WHEN attempt_count < max_attempts THEN 'Pending'
                    ELSE 'Failed'
                END,
                available_at = CASE
                    WHEN attempt_count < max_attempts THEN @retryAt
                    ELSE available_at
                END,
                started_at = CASE
                    WHEN attempt_count < max_attempts THEN NULL
                    ELSE started_at
                END,
                completed_at = NULL,
                failed_at = CASE
                    WHEN attempt_count < max_attempts THEN NULL
                    ELSE @now
                END,
                last_error_code = 'worker-timeout',
                last_error_summary = 'The processing lease expired before the job completed.',
                updated_at = @now
            WHERE status = 'Processing'
              AND started_at < @staleBefore;
            """;

        var now = DateTimeOffset.UtcNow;
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("staleBefore", staleBefore);
        command.Parameters.AddWithValue("retryAt", retryAt);
        command.Parameters.AddWithValue("now", now);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<DocumentIngestionJob> ExecuteRequiredJobUpdateAsync(
        string sql,
        Action<NpgsqlCommand> configure,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        configure(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The ingestion job was not found in the expected processing state.");
        }

        return ReadJob(reader);
    }

    private static async Task InsertDocumentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DocumentRecord document,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO documents
                (id, file_name, content_type, size_in_bytes, storage_path, status, created_at)
            VALUES
                (@id, @fileName, @contentType, @sizeInBytes, @storagePath, @status, @createdAt);
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

        return ReadJob(reader);
    }

    private static DocumentIngestionJob ReadJob(NpgsqlDataReader reader)
    {
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

    private static string NormalizeError(string value, int maximumLength)
    {
        var normalized = NormalizeRequired(value, nameof(value));
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
