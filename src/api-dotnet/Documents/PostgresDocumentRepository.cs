using Npgsql;
using NpgsqlTypes;

namespace EnterpriseDocumentAssistant.Api.Documents;

public sealed class PostgresDocumentRepository : IDocumentRepository
{
    private readonly string _connectionString;

    public PostgresDocumentRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");
    }

    public IReadOnlyCollection<DocumentRecord> GetAll(string? ownerId = null)
    {
        const string sql = """
            SELECT id,
                   file_name,
                   content_type,
                   size_in_bytes,
                   storage_path,
                   status,
                   created_at,
                   owner_id
            FROM documents
            WHERE @ownerId IS NULL OR owner_id = @ownerId
            ORDER BY created_at DESC;
            """;

        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();

        using var command = new NpgsqlCommand(sql, connection);
        AddOwnerParameter(command, ownerId);
        using var reader = command.ExecuteReader();

        var documents = new List<DocumentRecord>();

        while (reader.Read())
        {
            documents.Add(ReadDocument(reader));
        }

        return documents;
    }

    public DocumentRecord? GetById(Guid documentId, string? ownerId = null)
    {
        const string sql = """
            SELECT id,
                   file_name,
                   content_type,
                   size_in_bytes,
                   storage_path,
                   status,
                   created_at,
                   owner_id
            FROM documents
            WHERE id = @documentId
              AND (@ownerId IS NULL OR owner_id = @ownerId)
            LIMIT 1;
            """;

        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("documentId", documentId);
        AddOwnerParameter(command, ownerId);
        using var reader = command.ExecuteReader();

        return reader.Read() ? ReadDocument(reader) : null;
    }

    public DocumentRecord Add(
        string fileName,
        string? contentType,
        long sizeInBytes,
        string storagePath,
        string ownerId = DocumentOwnership.LegacyOwnerId)
    {
        var document = new DocumentRecord(
            Guid.NewGuid(),
            fileName.Trim(),
            string.IsNullOrWhiteSpace(contentType) ? null : contentType.Trim(),
            sizeInBytes,
            storagePath,
            "uploaded",
            DateTimeOffset.UtcNow,
            DocumentOwnership.Normalize(ownerId));

        const string sql = """
            INSERT INTO documents
                (id, file_name, content_type, size_in_bytes, storage_path, status, created_at, owner_id)
            VALUES
                (@id, @fileName, @contentType, @sizeInBytes, @storagePath, @status, @createdAt, @ownerId);
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
        command.Parameters.AddWithValue("ownerId", document.OwnerId);
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

    private static void AddOwnerParameter(NpgsqlCommand command, string? ownerId)
    {
        command.Parameters.Add("ownerId", NpgsqlDbType.Text).Value =
            ownerId is null ? DBNull.Value : DocumentOwnership.Normalize(ownerId);
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
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetString(7));
    }
}
