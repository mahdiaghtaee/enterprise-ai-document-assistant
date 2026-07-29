using EnterpriseDocumentAssistant.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace EnterpriseDocumentAssistant.Api.Documents;

internal static class PostgresTenantSession
{
    public static void Apply(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        using var command = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenantId, true);",
            connection,
            transaction);
        command.Parameters.Add("tenantId", NpgsqlDbType.Text).Value = TenantIsolation.Normalize(tenantId);
        command.ExecuteNonQuery();
    }

    public static async Task ApplyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        await using var command = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenantId, true);",
            connection,
            transaction);
        command.Parameters.Add("tenantId", NpgsqlDbType.Text).Value = TenantIsolation.Normalize(tenantId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
