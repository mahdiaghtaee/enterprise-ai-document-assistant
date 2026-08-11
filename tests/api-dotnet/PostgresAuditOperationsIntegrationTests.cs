using EnterpriseDocumentAssistant.Api.Audit;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace EnterpriseDocumentAssistant.Api.Tests;

public sealed class PostgresAuditOperationsIntegrationTests
{
    private const string AppUser = "audit_test_app";
    private const string AppPassword = "audit-test-app-password";
    private const string PlatformUser = "audit_test_platform";
    private const string PlatformPassword = "audit-test-platform-password";
    private const string PrivilegedUser = "audit_test_privileged";
    private const string PrivilegedPassword = "audit-test-privileged-password";

    private static readonly string? AdminConnectionString =
        Environment.GetEnvironmentVariable("POSTGRES_TEST_CONNECTION_STRING");

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Concurrent_same_tenant_inserts_form_one_valid_chain()
    {
        if (AdminConnectionString is null)
        {
            return;
        }

        await EnsureAuditOperationsSchemaAsync();
        var repository = CreateAuditRepository();
        const string tenantId = "audit-concurrency-tenant";

        var writes = Enumerable.Range(1, 20)
            .Select(index => repository.AppendAsync(
                CreateWrite(tenantId, $"concurrent-{index}"),
                bypassTenantIsolation: false,
                CancellationToken.None))
            .ToArray();

        await Task.WhenAll(writes);

        await using var admin = new NpgsqlConnection(AdminConnectionString);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*), count(DISTINCT chain_sequence), min(chain_sequence), max(chain_sequence)
            FROM audit_events
            WHERE tenant_id = @tenantId;
            """,
            admin);
        command.Parameters.AddWithValue("tenantId", tenantId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(20, reader.GetInt64(0));
        Assert.Equal(20, reader.GetInt64(1));
        Assert.Equal(1, reader.GetInt64(2));
        Assert.Equal(20, reader.GetInt64(3));

        var integrity = await repository.VerifyIntegrityAsync(
            new AuditIntegrityQuery(tenantId, BypassTenantIsolation: false),
            CancellationToken.None);

        Assert.True(integrity.IsValid);
        Assert.Equal(20, integrity.CheckedCount);
        Assert.Null(integrity.FirstBrokenSequence);
        Assert.Equal(20, integrity.HeadSequence);
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Archive_preserves_chain_and_tenant_visible_history()
    {
        if (AdminConnectionString is null)
        {
            return;
        }

        await EnsureAuditOperationsSchemaAsync();
        const string tenantId = "audit-archive-tenant";
        var now = DateTimeOffset.UtcNow;
        await InsertAsAdminAsync(tenantId, "old-1", now.AddDays(-120));
        await InsertAsAdminAsync(tenantId, "old-2", now.AddDays(-100));
        await InsertAsAdminAsync(tenantId, "recent", now.AddDays(-1));

        var maintenance = CreateMaintenanceRepository();
        var archived = await maintenance.ArchiveBeforeAsync(
            now.AddDays(-90),
            batchSize: 10,
            CancellationToken.None);

        Assert.Equal(2, archived);

        var repository = CreateAuditRepository();
        var events = await repository.GetRecentAsync(
            new AuditEventQuery(
                tenantId,
                BypassTenantIsolation: false,
                Limit: 10,
                IncludeArchived: true),
            CancellationToken.None);

        Assert.Equal(3, events.Count);
        Assert.Equal(2, events.Count(item => item.IsArchived));
        Assert.Single(events, item => !item.IsArchived);

        var integrity = await repository.VerifyIntegrityAsync(
            new AuditIntegrityQuery(tenantId, BypassTenantIsolation: false),
            CancellationToken.None);
        Assert.True(integrity.IsValid);
        Assert.Equal(3, integrity.CheckedCount);
        Assert.Equal(3, integrity.HeadSequence);
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Verifier_detects_tampered_event_payload()
    {
        if (AdminConnectionString is null)
        {
            return;
        }

        await EnsureAuditOperationsSchemaAsync();
        var repository = CreateAuditRepository();
        const string tenantId = "audit-tamper-tenant";
        await repository.AppendAsync(
            CreateWrite(tenantId, "first"),
            bypassTenantIsolation: false,
            CancellationToken.None);
        await repository.AppendAsync(
            CreateWrite(tenantId, "second"),
            bypassTenantIsolation: false,
            CancellationToken.None);

        await using (var admin = new NpgsqlConnection(AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var tamper = new NpgsqlCommand(
                """
                UPDATE audit_events
                SET details = jsonb_build_object('tampered', true)
                WHERE tenant_id = @tenantId AND chain_sequence = 1;
                """,
                admin);
            tamper.Parameters.AddWithValue("tenantId", tenantId);
            Assert.Equal(1, await tamper.ExecuteNonQueryAsync());
        }

        var integrity = await repository.VerifyIntegrityAsync(
            new AuditIntegrityQuery(tenantId, BypassTenantIsolation: false),
            CancellationToken.None);

        Assert.False(integrity.IsValid);
        Assert.Equal(0, integrity.CheckedCount);
        Assert.Equal(1, integrity.FirstBrokenSequence);
        Assert.Equal(2, integrity.HeadSequence);
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Application_roles_cannot_mutate_active_or_archived_audit_rows()
    {
        if (AdminConnectionString is null)
        {
            return;
        }

        await EnsureAuditOperationsSchemaAsync();
        const string tenantId = "audit-privilege-tenant";
        var now = DateTimeOffset.UtcNow;
        await InsertAsAdminAsync(tenantId, "old", now.AddDays(-120));
        await CreateMaintenanceRepository().ArchiveBeforeAsync(
            now.AddDays(-90),
            10,
            CancellationToken.None);
        await InsertAsAdminAsync(tenantId, "active", now);

        await AssertMutationDeniedAsync(
            AppUser,
            AppPassword,
            tenantId,
            "DELETE FROM audit_events WHERE tenant_id = @tenantId;");
        await AssertMutationDeniedAsync(
            AppUser,
            AppPassword,
            tenantId,
            "UPDATE audit_event_archive SET outcome = 'failure' WHERE tenant_id = @tenantId;");
        await AssertMutationDeniedAsync(
            PrivilegedUser,
            PrivilegedPassword,
            tenantId,
            "DELETE FROM audit_event_archive WHERE tenant_id = @tenantId;");
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Tenant_runtime_cannot_verify_another_tenants_chain()
    {
        if (AdminConnectionString is null)
        {
            return;
        }

        await EnsureAuditOperationsSchemaAsync();
        await InsertAsAdminAsync("tenant-a", "a", DateTimeOffset.UtcNow);
        await InsertAsAdminAsync("tenant-b", "b", DateTimeOffset.UtcNow);

        await using var connection = new NpgsqlConnection(BuildConnectionString(AppUser, AppPassword));
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var context = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', 'tenant-a', true);",
            connection,
            transaction))
        {
            await context.ExecuteNonQueryAsync();
        }

        await using var command = new NpgsqlCommand(
            "SELECT * FROM verify_audit_chain_scoped('tenant-b');",
            connection,
            transaction);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteReaderAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
        await transaction.RollbackAsync();
    }

    private static AuditEventWrite CreateWrite(string tenantId, string marker) => new(
        TenantId: tenantId,
        ActorUserId: "audit-test-user",
        ActorRole: "User",
        EventType: "audit.test",
        Action: "test",
        ResourceType: "audit_test",
        ResourceId: marker,
        Outcome: "success",
        CorrelationId: $"correlation-{marker}",
        TraceId: null,
        Details: new Dictionary<string, object?> { ["marker"] = marker });

    private static PostgresAuditEventRepository CreateAuditRepository()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = BuildConnectionString(AppUser, AppPassword),
                ["ConnectionStrings:PostgresPlatform"] = BuildConnectionString(PlatformUser, PlatformPassword),
                ["ConnectionStrings:PostgresPrivileged"] = BuildConnectionString(PrivilegedUser, PrivilegedPassword)
            })
            .Build();
        return new PostgresAuditEventRepository(configuration);
    }

    private static PostgresAuditMaintenanceRepository CreateMaintenanceRepository()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgresPrivileged"] = BuildConnectionString(
                    PrivilegedUser,
                    PrivilegedPassword)
            })
            .Build();
        return new PostgresAuditMaintenanceRepository(configuration);
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

    private static async Task InsertAsAdminAsync(
        string tenantId,
        string marker,
        DateTimeOffset occurredAt)
    {
        const string sql = """
            INSERT INTO audit_events
                (occurred_at, tenant_id, actor_user_id, actor_role, event_type, action,
                 resource_type, resource_id, outcome, correlation_id, trace_id, details)
            VALUES
                (@occurredAt, @tenantId, 'audit-admin', 'System', 'audit.test', 'test',
                 'audit_test', @marker, 'success', @correlationId, NULL,
                 jsonb_build_object('marker', @marker));
            """;
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("occurredAt", occurredAt);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("marker", marker);
        command.Parameters.AddWithValue("correlationId", $"correlation-{marker}");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertMutationDeniedAsync(
        string username,
        string password,
        string tenantId,
        string sql)
    {
        await using var connection = new NpgsqlConnection(BuildConnectionString(username, password));
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var context = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenantId, true);",
            connection,
            transaction))
        {
            context.Parameters.AddWithValue("tenantId", tenantId);
            await context.ExecuteNonQueryAsync();
        }

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
        await transaction.RollbackAsync();
    }

    private static async Task EnsureAuditOperationsSchemaAsync()
    {
        const string baseSql = """
            DROP TABLE IF EXISTS audit_event_archive CASCADE;
            DROP TABLE IF EXISTS audit_chain_heads CASCADE;
            DROP TABLE IF EXISTS audit_events CASCADE;
            DROP FUNCTION IF EXISTS archive_audit_events(TIMESTAMPTZ, INTEGER) CASCADE;
            DROP FUNCTION IF EXISTS verify_audit_chain_scoped(TEXT) CASCADE;
            DROP FUNCTION IF EXISTS verify_audit_chain(TEXT) CASCADE;
            DROP FUNCTION IF EXISTS assign_audit_event_chain() CASCADE;
            DROP FUNCTION IF EXISTS audit_event_compute_hash(TEXT, BIGINT, TIMESTAMPTZ, TEXT, TEXT, TEXT, TEXT, TEXT, TEXT, TEXT, TEXT, TEXT, TEXT, JSONB) CASCADE;
            DROP FUNCTION IF EXISTS audit_event_canonical_payload(BIGINT, TIMESTAMPTZ, TEXT, TEXT, TEXT, TEXT, TEXT, TEXT, TEXT, TEXT, TEXT, TEXT, JSONB) CASCADE;

            DO
            $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'audit_test_app') THEN
                    CREATE ROLE audit_test_app LOGIN PASSWORD 'audit-test-app-password'
                        NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'audit_test_platform') THEN
                    CREATE ROLE audit_test_platform LOGIN PASSWORD 'audit-test-platform-password'
                        NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'audit_test_privileged') THEN
                    CREATE ROLE audit_test_privileged LOGIN PASSWORD 'audit-test-privileged-password'
                        NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'document_app') THEN
                    CREATE ROLE document_app NOLOGIN NOSUPERUSER NOBYPASSRLS;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'document_platform') THEN
                    CREATE ROLE document_platform NOLOGIN NOSUPERUSER NOBYPASSRLS;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'document_privileged') THEN
                    CREATE ROLE document_privileged NOLOGIN NOSUPERUSER NOBYPASSRLS;
                END IF;
            END
            $$;

            ALTER ROLE audit_test_app PASSWORD 'audit-test-app-password';
            ALTER ROLE audit_test_platform PASSWORD 'audit-test-platform-password';
            ALTER ROLE audit_test_privileged PASSWORD 'audit-test-privileged-password';
            GRANT document_platform TO audit_test_platform;
            GRANT document_privileged TO audit_test_privileged;

            CREATE TABLE audit_events
            (
                id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                occurred_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                tenant_id TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                actor_role VARCHAR(50) NOT NULL,
                event_type VARCHAR(100) NOT NULL,
                action VARCHAR(100) NOT NULL,
                resource_type VARCHAR(100) NOT NULL,
                resource_id TEXT NULL,
                outcome VARCHAR(30) NOT NULL,
                correlation_id VARCHAR(128) NOT NULL,
                trace_id VARCHAR(64) NULL,
                details JSONB NOT NULL DEFAULT '{}'::jsonb,
                CONSTRAINT ck_audit_events_outcome CHECK (outcome IN ('success', 'failure', 'not_found', 'denied'))
            );

            GRANT USAGE ON SCHEMA public TO audit_test_app, audit_test_platform, audit_test_privileged;
            GRANT SELECT, INSERT ON audit_events TO audit_test_app, audit_test_platform, audit_test_privileged;
            GRANT USAGE, SELECT ON SEQUENCE audit_events_id_seq TO audit_test_app, audit_test_platform, audit_test_privileged;

            ALTER TABLE audit_events ENABLE ROW LEVEL SECURITY;
            ALTER TABLE audit_events FORCE ROW LEVEL SECURITY;

            CREATE POLICY audit_test_tenant_select
                ON audit_events FOR SELECT TO audit_test_app
                USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''));
            CREATE POLICY audit_test_tenant_insert
                ON audit_events FOR INSERT TO audit_test_app
                WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''));
            CREATE POLICY audit_test_platform_all
                ON audit_events FOR ALL TO audit_test_platform
                USING (true) WITH CHECK (true);
            CREATE POLICY audit_test_privileged_all
                ON audit_events FOR ALL TO audit_test_privileged
                USING (true) WITH CHECK (true);
            """;

        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand(baseSql, connection))
        {
            await command.ExecuteNonQueryAsync();
        }

        await ExecuteMigrationAsync(
            connection,
            "infra/postgres/init/zzzzzzzz-audit-operations.sql");
        await ExecuteMigrationAsync(
            connection,
            "infra/postgres/init/zzzzzzzzz-audit-verification-access.sql");

        const string grantSql = """
            GRANT SELECT, INSERT ON audit_events TO audit_test_app, audit_test_platform, audit_test_privileged;
            GRANT SELECT ON audit_event_archive TO audit_test_app, audit_test_platform, audit_test_privileged;
            GRANT USAGE, SELECT ON SEQUENCE audit_events_id_seq TO audit_test_app, audit_test_platform, audit_test_privileged;
            GRANT EXECUTE ON FUNCTION verify_audit_chain_scoped(TEXT) TO audit_test_app, audit_test_platform, audit_test_privileged;
            GRANT EXECUTE ON FUNCTION archive_audit_events(TIMESTAMPTZ, INTEGER) TO audit_test_privileged;

            DROP POLICY IF EXISTS audit_test_archive_tenant_select ON audit_event_archive;
            CREATE POLICY audit_test_archive_tenant_select
                ON audit_event_archive FOR SELECT TO audit_test_app
                USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''));
            DROP POLICY IF EXISTS audit_test_archive_platform_select ON audit_event_archive;
            CREATE POLICY audit_test_archive_platform_select
                ON audit_event_archive FOR SELECT TO audit_test_platform
                USING (true);
            DROP POLICY IF EXISTS audit_test_archive_privileged_select ON audit_event_archive;
            CREATE POLICY audit_test_archive_privileged_select
                ON audit_event_archive FOR SELECT TO audit_test_privileged
                USING (true);
            """;
        await using var grants = new NpgsqlCommand(grantSql, connection);
        await grants.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteMigrationAsync(NpgsqlConnection connection, string relativePath)
    {
        var scriptPath = FindRepositoryFile(relativePath);
        var script = await File.ReadAllTextAsync(scriptPath);
        var executableSql = string.Join(
            Environment.NewLine,
            script.Split('\n').Where(line => !line.TrimStart().StartsWith('\\')));

        await using var migration = new NpgsqlCommand(executableSql, connection)
        {
            CommandTimeout = 60
        };
        await migration.ExecuteNonQueryAsync();
    }

    private static string FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }
}
