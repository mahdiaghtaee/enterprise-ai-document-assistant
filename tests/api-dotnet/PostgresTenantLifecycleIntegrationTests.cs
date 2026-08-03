using EnterpriseDocumentAssistant.Api.Security;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace EnterpriseDocumentAssistant.Api.Tests;

public sealed class PostgresTenantLifecycleIntegrationTests
{
    private const string AppUser = "lifecycle_test_app";
    private const string AppPassword = "lifecycle-test-app-password";
    private const string PlatformUser = "lifecycle_test_platform";
    private const string PlatformPassword = "lifecycle-test-platform-password";

    private static readonly string? AdminConnectionString =
        Environment.GetEnvironmentVariable("POSTGRES_TEST_CONNECTION_STRING");

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Provision_invitation_acceptance_and_revocation_are_durable()
    {
        if (AdminConnectionString is null)
        {
            return;
        }

        await EnsureLifecycleSchemaAsync();
        var repository = CreateRepository();
        var tokens = new TenantInvitationTokenService(new TenantLifecycleOptions());

        var tenant = await repository.ProvisionAsync(
            new ProvisionTenantCommand(
                "managed-a",
                "Managed A",
                "admin-a",
                "platform-admin"),
            CancellationToken.None);
        var initialAdmin = await repository.EvaluateAccessAsync(
            "managed-a",
            "admin-a",
            CancellationToken.None);
        Assert.Equal(TenantStatuses.Active, tenant.Status);
        Assert.Equal(AppRoles.Admin, initialAdmin.MembershipRole);

        var secret = tokens.Create(24, DateTimeOffset.UtcNow);
        var invitation = await repository.CreateInvitationAsync(
            new CreateTenantInvitationCommand(
                "managed-a",
                "member-a",
                AppRoles.User,
                secret.TokenHash,
                secret.ExpiresAt,
                "admin-a"),
            CancellationToken.None);
        var storedHash = await QueryScalarAsync<string>(
            "SELECT token_hash::text FROM tenant_invitations WHERE id = @id;",
            command => command.Parameters.AddWithValue("id", invitation.Id));
        Assert.Equal(secret.TokenHash, storedHash?.Trim());
        Assert.NotEqual(secret.Token, storedHash?.Trim());

        var membership = await repository.AcceptInvitationAsync(
            new AcceptTenantInvitationCommand(
                "managed-a",
                "member-a",
                TenantInvitationTokenService.Hash(secret.Token),
                "member-a"),
            CancellationToken.None);
        Assert.Equal(TenantMembershipStatuses.Active, membership.Status);
        Assert.Equal(AppRoles.User, membership.Role);

        var replay = await Assert.ThrowsAsync<TenantLifecycleException>(() =>
            repository.AcceptInvitationAsync(
                new AcceptTenantInvitationCommand(
                    "managed-a",
                    "member-a",
                    secret.TokenHash,
                    "member-a"),
                CancellationToken.None));
        Assert.Equal("invitation_not_pending", replay.Code);

        await repository.RemoveMemberAsync(
            new RemoveMembershipCommand("managed-a", "member-a", "admin-a"),
            CancellationToken.None);
        var revokedAccess = await repository.EvaluateAccessAsync(
            "managed-a",
            "member-a",
            CancellationToken.None);
        Assert.False(revokedAccess.MembershipActive);
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Forced_rls_and_final_admin_guard_reject_cross_tenant_or_unsafe_changes()
    {
        if (AdminConnectionString is null)
        {
            return;
        }

        await EnsureLifecycleSchemaAsync();
        var repository = CreateRepository();
        await repository.ProvisionAsync(
            new ProvisionTenantCommand("managed-a", "Managed A", "admin-a", "platform-admin"),
            CancellationToken.None);
        await repository.ProvisionAsync(
            new ProvisionTenantCommand("managed-b", "Managed B", "admin-b", "platform-admin"),
            CancellationToken.None);

        var finalAdmin = await Assert.ThrowsAsync<TenantLifecycleException>(() =>
            repository.RemoveMemberAsync(
                new RemoveMembershipCommand("managed-a", "admin-a", "admin-a"),
                CancellationToken.None));
        Assert.Equal("last_tenant_admin", finalAdmin.Code);

        await using var connection = new NpgsqlConnection(BuildConnectionString(AppUser, AppPassword));
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var context = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', 'managed-a', true);",
            connection,
            transaction))
        {
            await context.ExecuteNonQueryAsync();
        }

        var visibleTenantB = new NpgsqlCommand(
            "SELECT count(*) FROM tenant_memberships WHERE tenant_id = 'managed-b';",
            connection,
            transaction);
        Assert.Equal(0L, (long)(await visibleTenantB.ExecuteScalarAsync() ?? 0L));

        var forbiddenInsert = new NpgsqlCommand(
            """
            INSERT INTO tenant_memberships
                (tenant_id, user_id, role, status, created_at, created_by, updated_at)
            VALUES
                ('managed-b', 'intruder', 'User', 'Active', CURRENT_TIMESTAMP, 'intruder', CURRENT_TIMESTAMP);
            """,
            connection,
            transaction);
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            forbiddenInsert.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
        await transaction.RollbackAsync();

        await repository.SetStatusAsync(
            new SetTenantStatusCommand(
                "managed-a",
                TenantStatuses.Disabled,
                "platform-admin"),
            CancellationToken.None);
        var disabledAccess = await repository.EvaluateAccessAsync(
            "managed-a",
            "admin-a",
            CancellationToken.None);
        Assert.False(disabledAccess.TenantActive);
    }

    private static PostgresTenantLifecycleRepository CreateRepository()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = BuildConnectionString(AppUser, AppPassword),
                ["ConnectionStrings:PostgresPlatform"] = BuildConnectionString(
                    PlatformUser,
                    PlatformPassword)
            })
            .Build();
        return new PostgresTenantLifecycleRepository(configuration);
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

    private static async Task<T?> QueryScalarAsync<T>(
        string sql,
        Action<NpgsqlCommand> configure)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        configure(command);
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    private static async Task EnsureLifecycleSchemaAsync()
    {
        const string sql = """
            DROP TABLE IF EXISTS tenant_invitations CASCADE;
            DROP TABLE IF EXISTS tenant_memberships CASCADE;
            DROP TABLE IF EXISTS tenants CASCADE;

            DO
            $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'lifecycle_test_app') THEN
                    CREATE ROLE lifecycle_test_app LOGIN PASSWORD 'lifecycle-test-app-password'
                        NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'lifecycle_test_platform') THEN
                    CREATE ROLE lifecycle_test_platform LOGIN PASSWORD 'lifecycle-test-platform-password'
                        NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS;
                END IF;
            END
            $$;

            ALTER ROLE lifecycle_test_app PASSWORD 'lifecycle-test-app-password';
            ALTER ROLE lifecycle_test_platform PASSWORD 'lifecycle-test-platform-password';

            CREATE TABLE tenants
            (
                tenant_id TEXT PRIMARY KEY,
                display_name TEXT NOT NULL,
                status TEXT NOT NULL,
                created_at TIMESTAMPTZ NOT NULL,
                created_by TEXT NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL,
                disabled_at TIMESTAMPTZ NULL,
                disabled_by TEXT NULL
            );

            CREATE TABLE tenant_memberships
            (
                tenant_id TEXT NOT NULL REFERENCES tenants (tenant_id) ON DELETE CASCADE,
                user_id TEXT NOT NULL,
                role TEXT NOT NULL CHECK (role IN ('User', 'Admin')),
                status TEXT NOT NULL CHECK (status IN ('Active', 'Removed')),
                created_at TIMESTAMPTZ NOT NULL,
                created_by TEXT NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL,
                removed_at TIMESTAMPTZ NULL,
                removed_by TEXT NULL,
                PRIMARY KEY (tenant_id, user_id)
            );

            CREATE TABLE tenant_invitations
            (
                id UUID PRIMARY KEY,
                tenant_id TEXT NOT NULL REFERENCES tenants (tenant_id) ON DELETE CASCADE,
                invitee_user_id TEXT NOT NULL,
                role TEXT NOT NULL CHECK (role IN ('User', 'Admin')),
                token_hash CHAR(64) NOT NULL UNIQUE,
                status TEXT NOT NULL CHECK (status IN ('Pending', 'Accepted', 'Revoked', 'Expired')),
                expires_at TIMESTAMPTZ NOT NULL,
                created_at TIMESTAMPTZ NOT NULL,
                created_by TEXT NOT NULL,
                accepted_at TIMESTAMPTZ NULL,
                accepted_by TEXT NULL,
                revoked_at TIMESTAMPTZ NULL,
                revoked_by TEXT NULL
            );

            CREATE UNIQUE INDEX ux_lifecycle_pending_invitee
                ON tenant_invitations (tenant_id, invitee_user_id)
                WHERE status = 'Pending';

            GRANT USAGE ON SCHEMA public TO lifecycle_test_app, lifecycle_test_platform;
            GRANT SELECT ON tenants TO lifecycle_test_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON tenant_memberships, tenant_invitations
                TO lifecycle_test_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON tenants, tenant_memberships, tenant_invitations
                TO lifecycle_test_platform;

            ALTER TABLE tenants ENABLE ROW LEVEL SECURITY;
            ALTER TABLE tenants FORCE ROW LEVEL SECURITY;
            ALTER TABLE tenant_memberships ENABLE ROW LEVEL SECURITY;
            ALTER TABLE tenant_memberships FORCE ROW LEVEL SECURITY;
            ALTER TABLE tenant_invitations ENABLE ROW LEVEL SECURITY;
            ALTER TABLE tenant_invitations FORCE ROW LEVEL SECURITY;

            CREATE POLICY lifecycle_app_tenants
                ON tenants FOR SELECT TO lifecycle_test_app
                USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''));
            CREATE POLICY lifecycle_platform_tenants
                ON tenants FOR ALL TO lifecycle_test_platform USING (true) WITH CHECK (true);
            CREATE POLICY lifecycle_app_memberships
                ON tenant_memberships FOR ALL TO lifecycle_test_app
                USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''))
                WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''));
            CREATE POLICY lifecycle_platform_memberships
                ON tenant_memberships FOR ALL TO lifecycle_test_platform USING (true) WITH CHECK (true);
            CREATE POLICY lifecycle_app_invitations
                ON tenant_invitations FOR ALL TO lifecycle_test_app
                USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''))
                WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''));
            CREATE POLICY lifecycle_platform_invitations
                ON tenant_invitations FOR ALL TO lifecycle_test_platform USING (true) WITH CHECK (true);
            """;

        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
