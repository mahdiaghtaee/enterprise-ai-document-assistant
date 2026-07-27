using Npgsql;

namespace EnterpriseDocumentAssistant.Api.Documents;

public sealed class PostgresDocumentRepository : IDocumentRepository
{
    private readonly string _connectionString;

    public PostgresDocumentRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");
    }

    public IReadOnlyCollection<DocumentRecord> GetAll()
    {
        const string sql = """
            SELECT id,
                   file_name,
                   content_type,
                   size_in_bytes,
                   storage_path,
                   status,
                   created_at
            FROM documents
            ORDER BY created_at DESC;
            """;

        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();

        using var command = new NpgsqlCommand(sql, connection);
        using var reader = command.ExecuteReader();

        var documents = new List<DocumentRecord>();

        while (reader.Read())
        {
            documents.Add(ReadDocument(reader));
        }

        return documents;
    }

    public DocumentRecord? GetById(Guid documentId)
    {
        const string sql = """
            SELECT id,
                   file_name,
                   content_type,
                   size_in_bytes,
                   storage_path,
                   status,
                   created_at
            FROM documents
            WHERE id = @documentId
            LIMIT 1;
            """;

        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("documentId", documentId);
        using var reader = command.ExecuteReader();

        return reader.Read() ? ReadDocument(reader) : null;
    }

    public DocumentRecord Add(
        string fileName,
        string? contentType,
        long sizeInBytes,
        string storagePath)
    {
        var document = new DocumentRecord(
            Guid.NewGuid(),
            fileName.Trim(),
            string.IsNullOrWhiteSpace(contentType) ? null : contentType.Trim(),
            sizeInBytes,
            storagePath,
            "uploaded",
            DateTimeOffset.UtcNow);

        const string sql = """
            INSERT INTO documents
                (id, file_name, content_type, size_in_bytes, storage_path, status, created_at)
            VALUES
                (@id, @fileName, @contentType, @sizeInBytes, @storagePath, @status, @createdAt);
            """;

        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();

        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", document.Id);
        command.Parameters.AddWithValue("fileName", document.FileName);
        command.Parameters.AddWithValue(
            "contentType",
            document.ContentType is null ? DBNull.Value : document.ContentType);
        command.Parameters.AddWithValue("sizeInBytes", document.SizeInBytes);
        command.Parameters.AddWithValue("storagePath", document.StoragePath);
        command.Parameters.AddWithValue("status", document.Status);
        command.Parameters.AddWithValue("createdAt", document.CreatedAt);
        command.ExecuteNonQuery();

        return document;
    }

    public void UpdateStatus(Guid documentId, string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            throw new ArgumentException("Document status is required.", nameof(status));
        }

        const string sql = """
            UPDATE documents
            SET status = @status
            WHERE id = @documentId;
            """;

        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("documentId", documentId);
        command.Parameters.AddWithValue("status", status.Trim());
        command.ExecuteNonQuery();
    }

    private static DocumentRecord ReadDocument(NpgsqlDataReader reader)
    {
        return new DocumentRecord(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetInt64(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetFieldValue<DateTimeOffset>(6));
    }
}
