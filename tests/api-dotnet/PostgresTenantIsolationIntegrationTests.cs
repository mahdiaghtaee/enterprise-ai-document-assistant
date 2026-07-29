using EnterpriseDocumentAssistant.Api.Documents;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace EnterpriseDocumentAssistant.Api.Tests;

public sealed class PostgresTenantIsolationIntegrationTests
{
    private const string AppUser = "tenant_test_app";
    private const string AppPassword = "tenant-test-app-password";
    private const string PrivilegedUser = "tenant_test_privileged";
    private const string PrivilegedPassword = "tenant-test-privileged-password";

    private static readonly string? AdminConnectionString =
        Environment.GetEnvironmentVariable("POSTGRES_TEST_CONNECTION_STRING");

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Runtime_role_can_only_read_rows_for_the_active_tenant()
    {
        if (AdminConnectionString is null)
        {
            return;
        }

        await EnsureTenantSchemaAsync();
        var repository = CreateRepository();
        var tenantA = repository.Add("tenant-a.txt", "text/plain", 1, "a", "tenant-a", "user-a");
        var tenantB = repository.Add("tenant-b.txt", "text/plain", 1, "b", "tenant-b", "user-b");

        var tenantAResults = repository.GetAll("tenant-a");
        var tenantBResults = repository.GetAll("tenant-b");
        var platformResults = repository.GetAll(bypassTenantIsolation: true);

        Assert.Contains(tenantAResults, document => document.Id == tenantA.Id);
        Assert.DoesNotContain(tenantAResults, document => document.Id == tenantB.Id);
        Assert.Contains(tenantBResults, document => document.Id == tenantB.Id);
        Assert.DoesNotContain(tenantBResults, document => document.Id == tenantA.Id);
        Assert.Contains(platformResults, document => document.Id == tenantA.Id);
        Assert.Contains(platformResults, document => document.Id == tenantB.Id);
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Runtime_role_cannot_insert_a_row_for_another_tenant()
    {
        if (AdminConnectionString is null)
        {
            return;
        }

        await EnsureTenantSchemaAsync();
        await using var connection = new NpgsqlConnection(BuildConnectionString(AppUser, AppPassword));
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var contextCommand = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', 'tenant-a', true);",
            connection,
            transaction))
        {
            await contextCommand.ExecuteNonQueryAsync();
        }

        const string sql = """
            INSERT INTO documents
                (id, file_name, content_type, size_in_bytes, storage_path, status, created_at, tenant_id, owner_id)
            VALUES
                (@id, 'forbidden.txt', 'text/plain', 1, 'forbidden', 'uploaded', CURRENT_TIMESTAMP, 'tenant-b', 'user-b');
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
        await transaction.RollbackAsync();
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Runtime_role_without_tenant_context_fails_closed()
    {
        if (AdminConnectionString is null)
        {
            return;
        }

        await EnsureTenantSchemaAsync();
        await using var connection = new NpgsqlConnection(BuildConnectionString(AppUser, AppPassword));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM documents;", connection);

        var count = (long)(await command.ExecuteScalarAsync() ?? 0L);

        Assert.Equal(0, count);
    }

    private static PostgresDocumentRepository CreateRepository()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = BuildConnectionString(AppUser, AppPassword),
                ["ConnectionStrings:PostgresPrivileged"] = BuildConnectionString(
                    PrivilegedUser,
                    PrivilegedPassword)
            })
            .Build();

        return new PostgresDocumentRepository(configuration);
    }

    private static string BuildConnectionString(string username, string password)
    {
        var builder = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Username = username,
            Password = password,
            Pooling = false
        };
        return builder.ConnectionString;
    }

    private static async Task EnsureTenantSchemaAsync()
    {
        const string sql = """
            DROP TABLE IF EXISTS document_ingestion_jobs CASCADE;
            DROP TABLE IF EXISTS document_chunks CASCADE;
            DROP TABLE IF EXISTS documents CASCADE;

            CREATE TABLE documents
            (
                id UUID PRIMARY KEY,
                file_name TEXT NOT NULL,
                content_type TEXT NULL,
                size_in_bytes BIGINT NOT NULL,
                storage_path TEXT NOT NULL,
                status TEXT NOT NULL,
                created_at TIMESTAMPTZ NOT NULL,
                tenant_id TEXT NOT NULL,
                owner_id TEXT NOT NULL
            );

            DO
            $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'tenant_test_app') THEN
                    CREATE ROLE tenant_test_app LOGIN PASSWORD 'tenant-test-app-password'
                        NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'tenant_test_privileged') THEN
                    CREATE ROLE tenant_test_privileged LOGIN PASSWORD 'tenant-test-privileged-password'
                        NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
                END IF;
            END
            $$;

            ALTER ROLE tenant_test_app PASSWORD 'tenant-test-app-password';
            ALTER ROLE tenant_test_privileged PASSWORD 'tenant-test-privileged-password';
            GRANT USAGE ON SCHEMA public TO tenant_test_app, tenant_test_privileged;
            GRANT SELECT, INSERT, UPDATE, DELETE ON documents TO tenant_test_app, tenant_test_privileged;

            ALTER TABLE documents ENABLE ROW LEVEL SECURITY;
            ALTER TABLE documents FORCE ROW LEVEL SECURITY;

            CREATE POLICY tenant_test_documents
                ON documents
                FOR ALL
                TO tenant_test_app
                USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''))
                WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''));

            CREATE POLICY tenant_test_privileged_documents
                ON documents
                FOR ALL
                TO tenant_test_privileged
                USING (true)
                WITH CHECK (true);
            """;

        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
