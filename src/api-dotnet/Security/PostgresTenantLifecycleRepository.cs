using EnterpriseDocumentAssistant.Api.Documents;
using Npgsql;
using NpgsqlTypes;

namespace EnterpriseDocumentAssistant.Api.Security;

public sealed class PostgresTenantLifecycleRepository : ITenantLifecycleRepository
{
    private readonly string _tenantConnectionString;
    private readonly string _platformConnectionString;

    public PostgresTenantLifecycleRepository(IConfiguration configuration)
    {
        _tenantConnectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");
        _platformConnectionString = configuration.GetConnectionString("PostgresPlatform")
            ?? configuration.GetConnectionString("PostgresPrivileged")
            ?? _tenantConnectionString;
    }

    public async Task<TenantRecord> ProvisionAsync(
        ProvisionTenantCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenantId = TenantIsolation.Normalize(command.TenantId);
        var displayName = InMemoryTenantLifecycleRepository.NormalizeRequired(
            command.DisplayName,
            "Display name");
        var initialAdmin = InMemoryTenantLifecycleRepository.NormalizeRequired(
            command.InitialAdminUserId,
            "Initial admin user id");
        var actor = InMemoryTenantLifecycleRepository.NormalizeRequired(
            command.ActorUserId,
            "Actor user id");
        var now = DateTimeOffset.UtcNow;

        const string tenantSql = """
            INSERT INTO tenants
                (tenant_id, display_name, status, created_at, created_by, updated_at)
            VALUES
                (@tenantId, @displayName, 'Active', @now, @actor, @now)
            RETURNING tenant_id,
                      display_name,
                      status,
                      created_at,
                      created_by,
                      updated_at,
                      disabled_at,
                      disabled_by;
            """;
        const string membershipSql = """
            INSERT INTO tenant_memberships
                (tenant_id, user_id, role, status, created_at, created_by, updated_at)
            VALUES
                (@tenantId, @userId, 'Admin', 'Active', @now, @actor, @now);
            """;

        await using var connection = new NpgsqlConnection(_platformConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            TenantRecord tenant;
            await using (var tenantCommand = new NpgsqlCommand(tenantSql, connection, transaction))
            {
                tenantCommand.Parameters.AddWithValue("tenantId", tenantId);
                tenantCommand.Parameters.AddWithValue("displayName", displayName);
                tenantCommand.Parameters.AddWithValue("now", now);
                tenantCommand.Parameters.AddWithValue("actor", actor);
                await using var reader = await tenantCommand.ExecuteReaderAsync(cancellationToken);
                await reader.ReadAsync(cancellationToken);
                tenant = ReadTenant(reader);
            }

            await using (var membershipCommand = new NpgsqlCommand(
                membershipSql,
                connection,
                transaction))
            {
                membershipCommand.Parameters.AddWithValue("tenantId", tenantId);
                membershipCommand.Parameters.AddWithValue("userId", initialAdmin);
                membershipCommand.Parameters.AddWithValue("now", now);
                membershipCommand.Parameters.AddWithValue("actor", actor);
                await membershipCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return tenant;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw InMemoryTenantLifecycleRepository.Conflict(
                "tenant_already_exists",
                "The tenant already exists.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<TenantRecord> SetStatusAsync(
        SetTenantStatusCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenantId = TenantIsolation.Normalize(command.TenantId);
        var status = InMemoryTenantLifecycleRepository.NormalizeTenantStatus(command.Status);
        var actor = InMemoryTenantLifecycleRepository.NormalizeRequired(
            command.ActorUserId,
            "Actor user id");
        var now = DateTimeOffset.UtcNow;

        const string sql = """
            UPDATE tenants
            SET status = @status,
                updated_at = @now,
                disabled_at = CASE WHEN @status = 'Disabled' THEN @now ELSE NULL END,
                disabled_by = CASE WHEN @status = 'Disabled' THEN @actor ELSE NULL END
            WHERE tenant_id = @tenantId
            RETURNING tenant_id,
                      display_name,
                      status,
                      created_at,
                      created_by,
                      updated_at,
                      disabled_at,
                      disabled_by;
            """;

        await using var connection = new NpgsqlConnection(_platformConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var commandDb = new NpgsqlCommand(sql, connection);
        commandDb.Parameters.AddWithValue("tenantId", tenantId);
        commandDb.Parameters.AddWithValue("status", status);
        commandDb.Parameters.AddWithValue("now", now);
        commandDb.Parameters.AddWithValue("actor", actor);
        await using var reader = await commandDb.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw InMemoryTenantLifecycleRepository.NotFound(
                "tenant_not_found",
                "The tenant was not found.");
        }

        return ReadTenant(reader);
    }

    public async Task<TenantAccessEvaluation> EvaluateAccessAsync(
        string tenantId,
        string userId,
        CancellationToken cancellationToken)
    {
        tenantId = TenantIsolation.Normalize(tenantId);
        userId = InMemoryTenantLifecycleRepository.NormalizeRequired(userId, "User id");

        const string sql = """
            SELECT tenants.status,
                   memberships.status,
                   memberships.role
            FROM tenants
            LEFT JOIN tenant_memberships AS memberships
                ON memberships.tenant_id = tenants.tenant_id
               AND memberships.user_id = @userId
            WHERE tenants.tenant_id = @tenantId
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_tenantConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgresTenantSession.ApplyAsync(connection, transaction, tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("userId", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
            return new TenantAccessEvaluation(
                IsManaged: true,
                TenantExists: false,
                TenantActive: false,
                MembershipActive: false,
                MembershipRole: null);
        }

        var tenantStatus = reader.GetString(0);
        var membershipStatus = reader.IsDBNull(1) ? null : reader.GetString(1);
        var role = reader.IsDBNull(2) ? null : reader.GetString(2);
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return new TenantAccessEvaluation(
            IsManaged: true,
            TenantExists: true,
            TenantActive: tenantStatus == TenantStatuses.Active,
            MembershipActive: membershipStatus == TenantMembershipStatuses.Active,
            MembershipRole: role);
    }

    public async Task<IReadOnlyList<TenantMembershipRecord>> ListMembersAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        tenantId = TenantIsolation.Normalize(tenantId);
        const string sql = """
            SELECT tenant_id,
                   user_id,
                   role,
                   status,
                   created_at,
                   created_by,
                   updated_at,
                   removed_at,
                   removed_by
            FROM tenant_memberships
            WHERE tenant_id = @tenantId
            ORDER BY user_id;
            """;

        await using var connection = new NpgsqlConnection(_tenantConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgresTenantSession.ApplyAsync(connection, transaction, tenantId, cancellationToken);
        await EnsureTenantExistsAsync(connection, transaction, tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<TenantMembershipRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadMembership(reader));
        }

        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<TenantMembershipRecord> SetMemberRoleAsync(
        SetMembershipRoleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenantId = TenantIsolation.Normalize(command.TenantId);
        var userId = InMemoryTenantLifecycleRepository.NormalizeRequired(command.UserId, "User id");
        var role = InMemoryTenantLifecycleRepository.NormalizeMembershipRole(command.Role);
        var actor = InMemoryTenantLifecycleRepository.NormalizeRequired(
            command.ActorUserId,
            "Actor user id");
        var now = DateTimeOffset.UtcNow;

        await using var connection = new NpgsqlConnection(_tenantConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgresTenantSession.ApplyAsync(connection, transaction, tenantId, cancellationToken);

        try
        {
            var current = await GetMemberForUpdateAsync(
                connection,
                transaction,
                tenantId,
                userId,
                cancellationToken);
            if (current is null || current.Status != TenantMembershipStatuses.Active)
            {
                throw InMemoryTenantLifecycleRepository.NotFound(
                    "membership_not_found",
                    "The active membership was not found.");
            }

            if (current.Role == AppRoles.Admin && role != AppRoles.Admin)
            {
                await EnsureAnotherActiveAdminAsync(
                    connection,
                    transaction,
                    tenantId,
                    userId,
                    cancellationToken);
            }

            const string sql = """
                UPDATE tenant_memberships
                SET role = @role,
                    status = 'Active',
                    updated_at = @now,
                    removed_at = NULL,
                    removed_by = NULL
                WHERE tenant_id = @tenantId
                  AND user_id = @userId
                RETURNING tenant_id,
                          user_id,
                          role,
                          status,
                          created_at,
                          created_by,
                          updated_at,
                          removed_at,
                          removed_by;
                """;
            await using var commandDb = new NpgsqlCommand(sql, connection, transaction);
            commandDb.Parameters.AddWithValue("tenantId", tenantId);
            commandDb.Parameters.AddWithValue("userId", userId);
            commandDb.Parameters.AddWithValue("role", role);
            commandDb.Parameters.AddWithValue("now", now);
            await using var reader = await commandDb.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var updated = ReadMembership(reader);
            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
            return updated;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<TenantMembershipRecord> RemoveMemberAsync(
        RemoveMembershipCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenantId = TenantIsolation.Normalize(command.TenantId);
        var userId = InMemoryTenantLifecycleRepository.NormalizeRequired(command.UserId, "User id");
        var actor = InMemoryTenantLifecycleRepository.NormalizeRequired(
            command.ActorUserId,
            "Actor user id");
        var now = DateTimeOffset.UtcNow;

        await using var connection = new NpgsqlConnection(_tenantConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgresTenantSession.ApplyAsync(connection, transaction, tenantId, cancellationToken);

        try
        {
            var current = await GetMemberForUpdateAsync(
                connection,
                transaction,
                tenantId,
                userId,
                cancellationToken);
            if (current is null || current.Status != TenantMembershipStatuses.Active)
            {
                throw InMemoryTenantLifecycleRepository.NotFound(
                    "membership_not_found",
                    "The active membership was not found.");
            }

            if (current.Role == AppRoles.Admin)
            {
                await EnsureAnotherActiveAdminAsync(
                    connection,
                    transaction,
                    tenantId,
                    userId,
                    cancellationToken);
            }

            const string sql = """
                UPDATE tenant_memberships
                SET status = 'Removed',
                    updated_at = @now,
                    removed_at = @now,
                    removed_by = @actor
                WHERE tenant_id = @tenantId
                  AND user_id = @userId
                RETURNING tenant_id,
                          user_id,
                          role,
                          status,
                          created_at,
                          created_by,
                          updated_at,
                          removed_at,
                          removed_by;
                """;
            await using var commandDb = new NpgsqlCommand(sql, connection, transaction);
            commandDb.Parameters.AddWithValue("tenantId", tenantId);
            commandDb.Parameters.AddWithValue("userId", userId);
            commandDb.Parameters.AddWithValue("now", now);
            commandDb.Parameters.AddWithValue("actor", actor);
            await using var reader = await commandDb.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var updated = ReadMembership(reader);
            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
            return updated;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<TenantInvitationRecord> CreateInvitationAsync(
        CreateTenantInvitationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenantId = TenantIsolation.Normalize(command.TenantId);
        var invitee = InMemoryTenantLifecycleRepository.NormalizeRequired(
            command.InviteeUserId,
            "Invitee user id");
        var role = InMemoryTenantLifecycleRepository.NormalizeMembershipRole(command.Role);
        var actor = InMemoryTenantLifecycleRepository.NormalizeRequired(
            command.ActorUserId,
            "Actor user id");
        var tokenHash = InMemoryTenantLifecycleRepository.NormalizeTokenHash(command.TokenHash);
        var now = DateTimeOffset.UtcNow;

        const string sql = """
            INSERT INTO tenant_invitations
                (id, tenant_id, invitee_user_id, role, token_hash, status,
                 expires_at, created_at, created_by)
            VALUES
                (@id, @tenantId, @invitee, @role, @tokenHash, 'Pending',
                 @expiresAt, @now, @actor)
            RETURNING id,
                      tenant_id,
                      invitee_user_id,
                      role,
                      status,
                      expires_at,
                      created_at,
                      created_by,
                      accepted_at,
                      accepted_by,
                      revoked_at,
                      revoked_by;
            """;

        await using var connection = new NpgsqlConnection(_tenantConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgresTenantSession.ApplyAsync(connection, transaction, tenantId, cancellationToken);

        try
        {
            await EnsureTenantActiveAsync(connection, transaction, tenantId, cancellationToken);
            await ExpirePendingInvitationsAsync(connection, transaction, tenantId, now, cancellationToken);
            await using var commandDb = new NpgsqlCommand(sql, connection, transaction);
            commandDb.Parameters.AddWithValue("id", Guid.NewGuid());
            commandDb.Parameters.AddWithValue("tenantId", tenantId);
            commandDb.Parameters.AddWithValue("invitee", invitee);
            commandDb.Parameters.AddWithValue("role", role);
            commandDb.Parameters.AddWithValue("tokenHash", tokenHash);
            commandDb.Parameters.AddWithValue("expiresAt", command.ExpiresAt);
            commandDb.Parameters.AddWithValue("now", now);
            commandDb.Parameters.AddWithValue("actor", actor);
            await using var reader = await commandDb.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var invitation = ReadInvitation(reader);
            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
            return invitation;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw InMemoryTenantLifecycleRepository.Conflict(
                "pending_invitation_exists",
                "A pending invitation already exists for this user.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<TenantInvitationRecord>> ListInvitationsAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        tenantId = TenantIsolation.Normalize(tenantId);
        var now = DateTimeOffset.UtcNow;
        const string sql = """
            SELECT id,
                   tenant_id,
                   invitee_user_id,
                   role,
                   status,
                   expires_at,
                   created_at,
                   created_by,
                   accepted_at,
                   accepted_by,
                   revoked_at,
                   revoked_by
            FROM tenant_invitations
            WHERE tenant_id = @tenantId
            ORDER BY created_at DESC, id;
            """;

        await using var connection = new NpgsqlConnection(_tenantConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgresTenantSession.ApplyAsync(connection, transaction, tenantId, cancellationToken);
        await EnsureTenantExistsAsync(connection, transaction, tenantId, cancellationToken);
        await ExpirePendingInvitationsAsync(connection, transaction, tenantId, now, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<TenantInvitationRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadInvitation(reader));
        }

        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<TenantInvitationRecord> RevokeInvitationAsync(
        RevokeTenantInvitationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenantId = TenantIsolation.Normalize(command.TenantId);
        var actor = InMemoryTenantLifecycleRepository.NormalizeRequired(
            command.ActorUserId,
            "Actor user id");
        var now = DateTimeOffset.UtcNow;
        const string sql = """
            UPDATE tenant_invitations
            SET status = 'Revoked',
                revoked_at = @now,
                revoked_by = @actor
            WHERE tenant_id = @tenantId
              AND id = @id
              AND status = 'Pending'
              AND expires_at > @now
            RETURNING id,
                      tenant_id,
                      invitee_user_id,
                      role,
                      status,
                      expires_at,
                      created_at,
                      created_by,
                      accepted_at,
                      accepted_by,
                      revoked_at,
                      revoked_by;
            """;

        await using var connection = new NpgsqlConnection(_tenantConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgresTenantSession.ApplyAsync(connection, transaction, tenantId, cancellationToken);
        await ExpirePendingInvitationsAsync(connection, transaction, tenantId, now, cancellationToken);
        await using var commandDb = new NpgsqlCommand(sql, connection, transaction);
        commandDb.Parameters.AddWithValue("tenantId", tenantId);
        commandDb.Parameters.AddWithValue("id", command.InvitationId);
        commandDb.Parameters.AddWithValue("now", now);
        commandDb.Parameters.AddWithValue("actor", actor);
        await using var reader = await commandDb.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw InMemoryTenantLifecycleRepository.NotFound(
                "invitation_not_found",
                "A pending invitation was not found.");
        }

        var invitation = ReadInvitation(reader);
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return invitation;
    }

    public async Task<TenantMembershipRecord> AcceptInvitationAsync(
        AcceptTenantInvitationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenantId = TenantIsolation.Normalize(command.TenantId);
        var invitee = InMemoryTenantLifecycleRepository.NormalizeRequired(
            command.InviteeUserId,
            "Invitee user id");
        var actor = InMemoryTenantLifecycleRepository.NormalizeRequired(
            command.ActorUserId,
            "Actor user id");
        var tokenHash = InMemoryTenantLifecycleRepository.NormalizeTokenHash(command.TokenHash);
        var now = DateTimeOffset.UtcNow;

        await using var connection = new NpgsqlConnection(_tenantConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgresTenantSession.ApplyAsync(connection, transaction, tenantId, cancellationToken);

        try
        {
            await EnsureTenantActiveAsync(connection, transaction, tenantId, cancellationToken);
            await ExpirePendingInvitationsAsync(connection, transaction, tenantId, now, cancellationToken);

            const string selectSql = """
                SELECT id,
                       tenant_id,
                       invitee_user_id,
                       role,
                       status,
                       expires_at,
                       created_at,
                       created_by,
                       accepted_at,
                       accepted_by,
                       revoked_at,
                       revoked_by
                FROM tenant_invitations
                WHERE tenant_id = @tenantId
                  AND token_hash = @tokenHash
                FOR UPDATE;
                """;
            TenantInvitationRecord invitation;
            await using (var selectCommand = new NpgsqlCommand(selectSql, connection, transaction))
            {
                selectCommand.Parameters.AddWithValue("tenantId", tenantId);
                selectCommand.Parameters.AddWithValue("tokenHash", tokenHash);
                await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw InMemoryTenantLifecycleRepository.NotFound(
                        "invitation_not_found",
                        "The invitation token is invalid.");
                }

                invitation = ReadInvitation(reader);
            }

            if (invitation.Status != TenantInvitationStatuses.Pending)
            {
                throw InMemoryTenantLifecycleRepository.Conflict(
                    "invitation_not_pending",
                    "The invitation is no longer pending.");
            }

            if (invitation.ExpiresAt <= now)
            {
                throw InMemoryTenantLifecycleRepository.Conflict(
                    "invitation_expired",
                    "The invitation has expired.");
            }

            if (!string.Equals(invitation.InviteeUserId, invitee, StringComparison.Ordinal) ||
                !string.Equals(invitee, actor, StringComparison.Ordinal))
            {
                throw new TenantLifecycleException(
                    "invitation_subject_mismatch",
                    "The invitation is not assigned to the authenticated user.",
                    StatusCodes.Status403Forbidden);
            }

            const string membershipSql = """
                INSERT INTO tenant_memberships
                    (tenant_id, user_id, role, status, created_at, created_by, updated_at,
                     removed_at, removed_by)
                VALUES
                    (@tenantId, @userId, @role, 'Active', @now, @createdBy, @now, NULL, NULL)
                ON CONFLICT (tenant_id, user_id)
                DO UPDATE SET
                    role = EXCLUDED.role,
                    status = 'Active',
                    updated_at = EXCLUDED.updated_at,
                    removed_at = NULL,
                    removed_by = NULL
                RETURNING tenant_id,
                          user_id,
                          role,
                          status,
                          created_at,
                          created_by,
                          updated_at,
                          removed_at,
                          removed_by;
                """;
            TenantMembershipRecord membership;
            await using (var membershipCommand = new NpgsqlCommand(
                membershipSql,
                connection,
                transaction))
            {
                membershipCommand.Parameters.AddWithValue("tenantId", tenantId);
                membershipCommand.Parameters.AddWithValue("userId", invitee);
                membershipCommand.Parameters.AddWithValue("role", invitation.Role);
                membershipCommand.Parameters.AddWithValue("now", now);
                membershipCommand.Parameters.AddWithValue("createdBy", invitation.CreatedBy);
                await using var reader = await membershipCommand.ExecuteReaderAsync(cancellationToken);
                await reader.ReadAsync(cancellationToken);
                membership = ReadMembership(reader);
            }

            const string invitationSql = """
                UPDATE tenant_invitations
                SET status = 'Accepted',
                    accepted_at = @now,
                    accepted_by = @actor
                WHERE id = @id;
                """;
            await using (var invitationCommand = new NpgsqlCommand(
                invitationSql,
                connection,
                transaction))
            {
                invitationCommand.Parameters.AddWithValue("now", now);
                invitationCommand.Parameters.AddWithValue("actor", actor);
                invitationCommand.Parameters.AddWithValue("id", invitation.Id);
                await invitationCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return membership;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<TenantMembershipRecord?> GetMemberForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        string userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT tenant_id,
                   user_id,
                   role,
                   status,
                   created_at,
                   created_by,
                   updated_at,
                   removed_at,
                   removed_by
            FROM tenant_memberships
            WHERE tenant_id = @tenantId
              AND user_id = @userId
            FOR UPDATE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("userId", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadMembership(reader) : null;
    }

    private static async Task EnsureAnotherActiveAdminAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        string excludedUserId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT user_id
            FROM tenant_memberships
            WHERE tenant_id = @tenantId
              AND role = 'Admin'
              AND status = 'Active'
            FOR UPDATE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var hasAnotherAdmin = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!string.Equals(reader.GetString(0), excludedUserId, StringComparison.Ordinal))
            {
                hasAnotherAdmin = true;
            }
        }

        if (!hasAnotherAdmin)
        {
            throw InMemoryTenantLifecycleRepository.Conflict(
                "last_tenant_admin",
                "The final active tenant administrator cannot be removed or downgraded.");
        }
    }

    private static async Task EnsureTenantExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT 1 FROM tenants WHERE tenant_id = @tenantId;";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            throw InMemoryTenantLifecycleRepository.NotFound(
                "tenant_not_found",
                "The tenant was not found.");
        }
    }

    private static async Task EnsureTenantActiveAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT status FROM tenants WHERE tenant_id = @tenantId;";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        var status = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (status is null)
        {
            throw InMemoryTenantLifecycleRepository.NotFound(
                "tenant_not_found",
                "The tenant was not found.");
        }

        if (status != TenantStatuses.Active)
        {
            throw InMemoryTenantLifecycleRepository.Conflict(
                "tenant_disabled",
                "The tenant is disabled.");
        }
    }

    private static async Task ExpirePendingInvitationsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE tenant_invitations
            SET status = 'Expired'
            WHERE tenant_id = @tenantId
              AND status = 'Pending'
              AND expires_at <= @now;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static TenantRecord ReadTenant(NpgsqlDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetFieldValue<DateTimeOffset>(3),
        reader.GetString(4),
        reader.GetFieldValue<DateTimeOffset>(5),
        reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
        reader.IsDBNull(7) ? null : reader.GetString(7));

    private static TenantMembershipRecord ReadMembership(NpgsqlDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetFieldValue<DateTimeOffset>(4),
        reader.GetString(5),
        reader.GetFieldValue<DateTimeOffset>(6),
        reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
        reader.IsDBNull(8) ? null : reader.GetString(8));

    private static TenantInvitationRecord ReadInvitation(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetFieldValue<DateTimeOffset>(5),
        reader.GetFieldValue<DateTimeOffset>(6),
        reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
        reader.IsDBNull(9) ? null : reader.GetString(9),
        reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
        reader.IsDBNull(11) ? null : reader.GetString(11));
}
