using EnterpriseDocumentAssistant.Api.Security;
using Npgsql;

namespace EnterpriseDocumentAssistant.Api.Documents;

public interface IDocumentProcessingStatusReader
{
    Task<DocumentIngestionJob?> GetLatestForDocumentAsync(
        Guid documentId,
        DocumentAccessContext access,
        CancellationToken cancellationToken);
}

public sealed class PostgresDocumentProcessingStatusReader : IDocumentProcessingStatusReader
{
    private readonly string _tenantConnectionString;
    private readonly string _platformConnectionString;

    public PostgresDocumentProcessingStatusReader(IConfiguration configuration)
    {
        _tenantConnectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");
        _platformConnectionString = configuration.GetConnectionString("PostgresPlatform")
            ?? configuration.GetConnectionString("PostgresPrivileged")
            ?? _tenantConnectionString;
    }

    public async Task<DocumentIngestionJob?> GetLatestForDocumentAsync(
        Guid documentId,
        DocumentAccessContext access,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
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

        await using var connection = new NpgsqlConnection(
            access.UsePrivilegedDatabase ? _platformConnectionString : _tenantConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!access.UsePrivilegedDatabase)
        {
            await PostgresTenantSession.ApplyAsync(
                connection,
                transaction,
                access.TenantId,
                cancellationToken);
        }

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("documentId", documentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var job = await reader.ReadAsync(cancellationToken) ? ReadJob(reader) : null;
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return job;
    }

    private static DocumentIngestionJob ReadJob(NpgsqlDataReader reader) => new(
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

public sealed class RepositoryDocumentProcessingStatusReader : IDocumentProcessingStatusReader
{
    private readonly IIngestionJobRepository _repository;

    public RepositoryDocumentProcessingStatusReader(IIngestionJobRepository repository)
    {
        _repository = repository;
    }

    public Task<DocumentIngestionJob?> GetLatestForDocumentAsync(
        Guid documentId,
        DocumentAccessContext access,
        CancellationToken cancellationToken) =>
        _repository.GetLatestForDocumentAsync(documentId, cancellationToken);
}

public static class DocumentProcessingStatusReaderServiceCollectionExtensions
{
    public static IServiceCollection AddDocumentProcessingStatusReader(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration.GetConnectionString("Postgres")))
        {
            services.AddSingleton<IDocumentProcessingStatusReader, PostgresDocumentProcessingStatusReader>();
        }
        else
        {
            services.AddSingleton<IDocumentProcessingStatusReader, RepositoryDocumentProcessingStatusReader>();
        }

        return services;
    }
}
