using System.Text.Json;
using EnterpriseDocumentAssistant.Api.Documents;
using EnterpriseDocumentAssistant.Api.Observability;
using EnterpriseDocumentAssistant.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace EnterpriseDocumentAssistant.Api.Audit;

public sealed class PostgresAuditEventRepository : IAuditEventRepository
{
    private readonly string _tenantConnectionString;
    private readonly string _privilegedConnectionString;

    public PostgresAuditEventRepository(IConfiguration configuration)
    {
        _tenantConnectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");
        _privilegedConnectionString = configuration.GetConnectionString("PostgresPrivileged")
            ?? _tenantConnectionString;
    }

    public async Task<AuditEventRecord> AppendAsync(
        AuditEventWrite auditEvent,
        bool bypassTenantIsolation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        const string sql = """
            INSERT INTO audit_events
                (tenant_id, actor_user_id, actor_role, event_type, action, resource_type,
                 resource_id, outcome, correlation_id, trace_id, details)
            VALUES
                (@tenantId, @actorUserId, @actorRole, @eventType, @action, @resourceType,
                 @resourceId, @outcome, @correlationId, @traceId, @details)
            RETURNING id, occurred_at, tenant_id, actor_user_id, actor_role, event_type,
                      action, resource_type, resource_id, outcome, correlation_id, trace_id,
                      details::text;
            """;

        await using var connection = new NpgsqlConnection(
            bypassTenantIsolation ? _privilegedConnectionString : _tenantConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!bypassTenantIsolation)
        {
            await PostgresTenantSession.ApplyAsync(
                connection,
                transaction,
                auditEvent.TenantId,
                cancellationToken);
        }

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddWriteParameters(command, auditEvent);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The audit event insert did not return a row.");
        }

        var record = ReadRecord(reader);
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        ApplicationTelemetry.AuditEventsPersisted.Add(
            1,
            ApplicationTelemetry.Tag("audit.event_type", auditEvent.EventType));
        return record;
    }

    public async Task<IReadOnlyList<AuditEventRecord>> GetRecentAsync(
        AuditEventQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();

        const string sql = """
            SELECT id, occurred_at, tenant_id, actor_user_id, actor_role, event_type,
                   action, resource_type, resource_id, outcome, correlation_id, trace_id,
                   details::text
            FROM audit_events
            WHERE @tenantId IS NULL OR tenant_id = @tenantId
            ORDER BY occurred_at DESC, id DESC
            LIMIT @limit;
            """;

        await using var connection = new NpgsqlConnection(
            query.BypassTenantIsolation ? _privilegedConnectionString : _tenantConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!query.BypassTenantIsolation)
        {
            await PostgresTenantSession.ApplyAsync(
                connection,
                transaction,
                query.TenantId ?? string.Empty,
                cancellationToken);
        }

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("tenantId", NpgsqlDbType.Text).Value =
            query.TenantId is null ? DBNull.Value : TenantIsolation.Normalize(query.TenantId);
        command.Parameters.AddWithValue("limit", query.Limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<AuditEventRecord>();

        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(ReadRecord(reader));
        }

        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return records;
    }

    private static void AddWriteParameters(NpgsqlCommand command, AuditEventWrite auditEvent)
    {
        command.Parameters.AddWithValue("tenantId", TenantIsolation.Normalize(auditEvent.TenantId));
        command.Parameters.AddWithValue("actorUserId", auditEvent.ActorUserId);
        command.Parameters.AddWithValue("actorRole", auditEvent.ActorRole);
        command.Parameters.AddWithValue("eventType", auditEvent.EventType);
        command.Parameters.AddWithValue("action", auditEvent.Action);
        command.Parameters.AddWithValue("resourceType", auditEvent.ResourceType);
        command.Parameters.Add("resourceId", NpgsqlDbType.Text).Value =
            auditEvent.ResourceId is null ? DBNull.Value : auditEvent.ResourceId;
        command.Parameters.AddWithValue("outcome", auditEvent.Outcome);
        command.Parameters.AddWithValue("correlationId", auditEvent.CorrelationId);
        command.Parameters.Add("traceId", NpgsqlDbType.Text).Value =
            auditEvent.TraceId is null ? DBNull.Value : auditEvent.TraceId;
        command.Parameters.Add("details", NpgsqlDbType.Jsonb).Value =
            JsonSerializer.Serialize(auditEvent.Details ?? new Dictionary<string, object?>());
    }

    private static AuditEventRecord ReadRecord(NpgsqlDataReader reader)
    {
        var details = JsonSerializer.Deserialize<Dictionary<string, object?>>(reader.GetString(12))
            ?? new Dictionary<string, object?>();

        return new AuditEventRecord(
            reader.GetInt64(0),
            reader.GetFieldValue<DateTimeOffset>(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            details);
    }
}
