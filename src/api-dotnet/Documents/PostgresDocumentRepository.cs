using EnterpriseDocumentAssistant.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace EnterpriseDocumentAssistant.Api.Documents;

public sealed class PostgresDocumentRepository : IDocumentRepository
{
    private readonly string _tenantConnectionString;
    private readonly string _privilegedConnectionString;

    public PostgresDocumentRepository(IConfiguration configuration)
    {
        _tenantConnectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");
        _privilegedConnectionString = configuration.GetConnectionString("PostgresPrivileged")
            ?? _tenantConnectionString;
    }

    public IReadOnlyCollection<DocumentRecord> GetAll(
        string? tenantId = null,
        string? ownerId = null,
        bool bypassTenantIsolation = false)
    {
        const string sql = """
            SELECT id,
                   file_name,
                   content_type,
                   size_in_bytes,
                   storage_path,
                   status,
                   created_at,
                   tenant_id,
                   owner_id
            FROM documents
            WHERE @ownerId IS NULL OR owner_id = @ownerId
            ORDER BY created_at DESC;
            """;

        using var connection = OpenConnection(bypassTenantIsolation);
        using var transaction = connection.BeginTransaction();
        ApplyTenantContext(connection, transaction, tenantId, bypassTenantIsolation);
        using var command = new NpgsqlCommand(sql, connection, transaction);
        AddOwnerParameter(command, ownerId);
        using var reader = command.ExecuteReader();

        var documents = new List<DocumentRecord>();
        while (reader.Read())
        {
            documents.Add(ReadDocument(reader));
        }

        reader.Close();
        transaction.Commit();
        return documents;
    }

    public DocumentRecord? GetById(
        Guid documentId,
        string? tenantId = null,
        string? ownerId = null,
        bool bypassTenantIsolation = false)
    {
        const string sql = """
            SELECT id,
                   file_name,
                   content_type,
                   size_in_bytes,
                   storage_path,
                   status,
                   created_at,
                   tenant_id,
                   owner_id
            FROM documents
            WHERE id = @documentId
              AND (@ownerId IS NULL OR owner_id = @ownerId)
            LIMIT 1;
            """;

        using var connection = OpenConnection(bypassTenantIsolation);
        using var transaction = connection.BeginTransaction();
        ApplyTenantContext(connection, transaction, tenantId, bypassTenantIsolation);
        using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("documentId", documentId);
        AddOwnerParameter(command, ownerId);
        using var reader = command.ExecuteReader();

        var document = reader.Read() ? ReadDocument(reader) : null;
        reader.Close();
        transaction.Commit();
        return document;
    }

    public DocumentRecord Add(
        string fileName,
        string? contentType,
        long sizeInBytes,
        string storagePath,
        string tenantId = TenantIsolation.LegacyTenantId,
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
            TenantIsolation.Normalize(tenantId),
            DocumentOwnership.Normalize(ownerId));

        const string sql = """
            INSERT INTO documents
                (id, file_name, content_type, size_in_bytes, storage_path, status, created_at, tenant_id, owner_id)
            VALUES
                (@id, @fileName, @contentType, @sizeInBytes, @storagePath, @status, @createdAt, @tenantId, @ownerId);
            """;

        using var connection = OpenConnection(bypassTenantIsolation: false);
        using var transaction = connection.BeginTransaction();
        PostgresTenantSession.Apply(connection, transaction, document.TenantId);
        using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", document.Id);
        command.Parameters.AddWithValue("fileName", document.FileName);
        command.Parameters.AddWithValue(
            "contentType",
            document.ContentType is null ? DBNull.Value : document.ContentType);
        command.Parameters.AddWithValue("sizeInBytes", document.SizeInBytes);
        command.Parameters.AddWithValue("storagePath", document.StoragePath);
        command.Parameters.AddWithValue("status", document.Status);
        command.Parameters.AddWithValue("createdAt", document.CreatedAt);
        command.Parameters.AddWithValue("tenantId", document.TenantId);
        command.Parameters.AddWithValue("ownerId", document.OwnerId);
        command.ExecuteNonQuery();
        transaction.Commit();

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

        using var connection = OpenConnection(bypassTenantIsolation: true);
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("documentId", documentId);
        command.Parameters.AddWithValue("status", status.Trim());
        command.ExecuteNonQuery();
    }

    private NpgsqlConnection OpenConnection(bool bypassTenantIsolation)
    {
        var connection = new NpgsqlConnection(
            bypassTenantIsolation ? _privilegedConnectionString : _tenantConnectionString);
        connection.Open();
        return connection;
    }

    private static void ApplyTenantContext(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string? tenantId,
        bool bypassTenantIsolation)
    {
        if (bypassTenantIsolation)
        {
            return;
        }

        PostgresTenantSession.Apply(
            connection,
            transaction,
            TenantIsolation.Normalize(tenantId ?? string.Empty));
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
            reader.GetString(7),
            reader.GetString(8));
    }
}
