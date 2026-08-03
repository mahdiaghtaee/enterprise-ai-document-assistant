using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.WebUtilities;

namespace EnterpriseDocumentAssistant.Api.Security;

public static class TenantStatuses
{
    public const string Active = "Active";
    public const string Disabled = "Disabled";
}

public static class TenantMembershipStatuses
{
    public const string Active = "Active";
    public const string Removed = "Removed";
}

public static class TenantInvitationStatuses
{
    public const string Pending = "Pending";
    public const string Accepted = "Accepted";
    public const string Revoked = "Revoked";
    public const string Expired = "Expired";
}

public sealed class TenantLifecycleOptions
{
    public const string SectionName = "TenantLifecycle";

    public bool AllowUnmanagedClaimsFallback { get; set; } = true;

    public int DefaultInvitationLifetimeHours { get; set; } = 48;

    public int MaximumInvitationLifetimeHours { get; set; } = 168;

    public void Validate()
    {
        if (DefaultInvitationLifetimeHours is < 1 or > 168)
        {
            throw new InvalidOperationException(
                "TenantLifecycle:DefaultInvitationLifetimeHours must be between 1 and 168.");
        }

        if (MaximumInvitationLifetimeHours is < 1 or > 720)
        {
            throw new InvalidOperationException(
                "TenantLifecycle:MaximumInvitationLifetimeHours must be between 1 and 720.");
        }

        if (DefaultInvitationLifetimeHours > MaximumInvitationLifetimeHours)
        {
            throw new InvalidOperationException(
                "TenantLifecycle:DefaultInvitationLifetimeHours cannot exceed MaximumInvitationLifetimeHours.");
        }
    }
}

public sealed record TenantRecord(
    string TenantId,
    string DisplayName,
    string Status,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DisabledAt,
    string? DisabledBy);

public sealed record TenantMembershipRecord(
    string TenantId,
    string UserId,
    string Role,
    string Status,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? RemovedAt,
    string? RemovedBy);

public sealed record TenantInvitationRecord(
    Guid Id,
    string TenantId,
    string InviteeUserId,
    string Role,
    string Status,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset? AcceptedAt,
    string? AcceptedBy,
    DateTimeOffset? RevokedAt,
    string? RevokedBy);

public sealed record TenantAccessEvaluation(
    bool IsManaged,
    bool TenantExists,
    bool TenantActive,
    bool MembershipActive,
    string? MembershipRole)
{
    public bool CanUseTenant => TenantExists && TenantActive && MembershipActive;

    public static TenantAccessEvaluation Unmanaged() => new(
        IsManaged: false,
        TenantExists: false,
        TenantActive: false,
        MembershipActive: false,
        MembershipRole: null);
}

public sealed record ProvisionTenantCommand(
    string TenantId,
    string DisplayName,
    string InitialAdminUserId,
    string ActorUserId);

public sealed record SetTenantStatusCommand(
    string TenantId,
    string Status,
    string ActorUserId);

public sealed record SetMembershipRoleCommand(
    string TenantId,
    string UserId,
    string Role,
    string ActorUserId);

public sealed record RemoveMembershipCommand(
    string TenantId,
    string UserId,
    string ActorUserId);

public sealed record CreateTenantInvitationCommand(
    string TenantId,
    string InviteeUserId,
    string Role,
    string TokenHash,
    DateTimeOffset ExpiresAt,
    string ActorUserId);

public sealed record RevokeTenantInvitationCommand(
    string TenantId,
    Guid InvitationId,
    string ActorUserId);

public sealed record AcceptTenantInvitationCommand(
    string TenantId,
    string InviteeUserId,
    string TokenHash,
    string ActorUserId);

public interface ITenantLifecycleRepository
{
    Task<TenantRecord> ProvisionAsync(
        ProvisionTenantCommand command,
        CancellationToken cancellationToken);

    Task<TenantRecord> SetStatusAsync(
        SetTenantStatusCommand command,
        CancellationToken cancellationToken);

    Task<TenantAccessEvaluation> EvaluateAccessAsync(
        string tenantId,
        string userId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TenantMembershipRecord>> ListMembersAsync(
        string tenantId,
        CancellationToken cancellationToken);

    Task<TenantMembershipRecord> SetMemberRoleAsync(
        SetMembershipRoleCommand command,
        CancellationToken cancellationToken);

    Task<TenantMembershipRecord> RemoveMemberAsync(
        RemoveMembershipCommand command,
        CancellationToken cancellationToken);

    Task<TenantInvitationRecord> CreateInvitationAsync(
        CreateTenantInvitationCommand command,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TenantInvitationRecord>> ListInvitationsAsync(
        string tenantId,
        CancellationToken cancellationToken);

    Task<TenantInvitationRecord> RevokeInvitationAsync(
        RevokeTenantInvitationCommand command,
        CancellationToken cancellationToken);

    Task<TenantMembershipRecord> AcceptInvitationAsync(
        AcceptTenantInvitationCommand command,
        CancellationToken cancellationToken);
}

public sealed class TenantLifecycleException : InvalidOperationException
{
    public TenantLifecycleException(string code, string message, int statusCode)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }

    public int StatusCode { get; }
}

public sealed record TenantInvitationSecret(
    TenantInvitationRecord Invitation,
    string Token);

public sealed class TenantInvitationTokenService
{
    private readonly TenantLifecycleOptions _options;

    public TenantInvitationTokenService(TenantLifecycleOptions options)
    {
        _options = options;
    }

    public (string Token, string TokenHash, DateTimeOffset ExpiresAt) Create(
        int? lifetimeHours,
        DateTimeOffset now)
    {
        var hours = lifetimeHours ?? _options.DefaultInvitationLifetimeHours;
        if (hours is < 1 || hours > _options.MaximumInvitationLifetimeHours)
        {
            throw new TenantLifecycleException(
                "invalid_invitation_lifetime",
                $"Invitation lifetime must be between 1 and {_options.MaximumInvitationLifetimeHours} hours.",
                StatusCodes.Status400BadRequest);
        }

        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = WebEncoders.Base64UrlEncode(bytes);
        return (token, Hash(token), now.AddHours(hours));
    }

    public static string Hash(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new TenantLifecycleException(
                "invitation_token_required",
                "Invitation token is required.",
                StatusCodes.Status400BadRequest);
        }

        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token.Trim())))
            .ToLowerInvariant();
    }
}

public sealed record ActiveTenantMembershipRequirement(
    string? RequiredMembershipRole = null) : IAuthorizationRequirement;

public sealed class ActiveTenantMembershipHandler
    : AuthorizationHandler<ActiveTenantMembershipRequirement>
{
    private readonly ITenantLifecycleRepository _repository;
    private readonly TenantLifecycleOptions _options;

    public ActiveTenantMembershipHandler(
        ITenantLifecycleRepository repository,
        TenantLifecycleOptions options)
    {
        _repository = repository;
        _options = options;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveTenantMembershipRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        if (context.User.IsInRole(AppRoles.PlatformAdmin))
        {
            context.Succeed(requirement);
            return;
        }

        DocumentAccessContext access;
        try
        {
            access = DocumentAccessContext.FromPrincipal(context.User);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var evaluation = await _repository.EvaluateAccessAsync(
            access.TenantId,
            access.UserId,
            CancellationToken.None);

        if (!evaluation.IsManaged)
        {
            if (_options.AllowUnmanagedClaimsFallback)
            {
                context.Succeed(requirement);
            }

            return;
        }

        if (!evaluation.CanUseTenant)
        {
            return;
        }

        if (string.Equals(
            requirement.RequiredMembershipRole,
            AppRoles.Admin,
            StringComparison.Ordinal) &&
            !string.Equals(evaluation.MembershipRole, AppRoles.Admin, StringComparison.Ordinal))
        {
            return;
        }

        // A stale JWT that still claims Admin must never elevate a durable User membership.
        if (context.User.IsInRole(AppRoles.Admin) &&
            !string.Equals(evaluation.MembershipRole, AppRoles.Admin, StringComparison.Ordinal))
        {
            return;
        }

        context.Succeed(requirement);
    }
}

public sealed class InMemoryTenantLifecycleRepository : ITenantLifecycleRepository
{
    private readonly ConcurrentDictionary<string, TenantRecord> _tenants = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string TenantId, string UserId), TenantMembershipRecord> _members = new();
    private readonly ConcurrentDictionary<Guid, (TenantInvitationRecord Invitation, string TokenHash)> _invitations = new();
    private readonly object _gate = new();

    public Task<TenantRecord> ProvisionAsync(
        ProvisionTenantCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tenantId = TenantIsolation.Normalize(command.TenantId);
        var displayName = NormalizeRequired(command.DisplayName, "Display name");
        var adminUserId = NormalizeRequired(command.InitialAdminUserId, "Initial admin user id");
        var actor = NormalizeRequired(command.ActorUserId, "Actor user id");
        var now = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            if (_tenants.ContainsKey(tenantId))
            {
                throw Conflict("tenant_already_exists", "The tenant already exists.");
            }

            var tenant = new TenantRecord(
                tenantId,
                displayName,
                TenantStatuses.Active,
                now,
                actor,
                now,
                null,
                null);
            _tenants[tenantId] = tenant;
            _members[(tenantId, adminUserId)] = new TenantMembershipRecord(
                tenantId,
                adminUserId,
                AppRoles.Admin,
                TenantMembershipStatuses.Active,
                now,
                actor,
                now,
                null,
                null);
            return Task.FromResult(tenant);
        }
    }

    public Task<TenantRecord> SetStatusAsync(
        SetTenantStatusCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tenantId = TenantIsolation.Normalize(command.TenantId);
        var status = NormalizeTenantStatus(command.Status);
        var actor = NormalizeRequired(command.ActorUserId, "Actor user id");
        var now = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            if (!_tenants.TryGetValue(tenantId, out var tenant))
            {
                throw NotFound("tenant_not_found", "The tenant was not found.");
            }

            var updated = tenant with
            {
                Status = status,
                UpdatedAt = now,
                DisabledAt = status == TenantStatuses.Disabled ? now : null,
                DisabledBy = status == TenantStatuses.Disabled ? actor : null
            };
            _tenants[tenantId] = updated;
            return Task.FromResult(updated);
        }
    }

    public Task<TenantAccessEvaluation> EvaluateAccessAsync(
        string tenantId,
        string userId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        tenantId = TenantIsolation.Normalize(tenantId);
        userId = NormalizeRequired(userId, "User id");

        if (!_tenants.TryGetValue(tenantId, out var tenant))
        {
            return Task.FromResult(TenantAccessEvaluation.Unmanaged());
        }

        _members.TryGetValue((tenantId, userId), out var membership);
        return Task.FromResult(new TenantAccessEvaluation(
            IsManaged: true,
            TenantExists: true,
            TenantActive: tenant.Status == TenantStatuses.Active,
            MembershipActive: membership?.Status == TenantMembershipStatuses.Active,
            MembershipRole: membership?.Role));
    }

    public Task<IReadOnlyList<TenantMembershipRecord>> ListMembersAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        tenantId = TenantIsolation.Normalize(tenantId);
        EnsureTenant(tenantId);
        IReadOnlyList<TenantMembershipRecord> result = _members.Values
            .Where(member => member.TenantId == tenantId)
            .OrderBy(member => member.UserId, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult(result);
    }

    public Task<TenantMembershipRecord> SetMemberRoleAsync(
        SetMembershipRoleCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tenantId = TenantIsolation.Normalize(command.TenantId);
        var userId = NormalizeRequired(command.UserId, "User id");
        var role = NormalizeMembershipRole(command.Role);
        var actor = NormalizeRequired(command.ActorUserId, "Actor user id");
        var now = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            EnsureTenant(tenantId);
            if (!_members.TryGetValue((tenantId, userId), out var membership) ||
                membership.Status != TenantMembershipStatuses.Active)
            {
                throw NotFound("membership_not_found", "The active membership was not found.");
            }

            EnsureNotLastAdmin(tenantId, membership, role, removing: false);
            var updated = membership with
            {
                Role = role,
                UpdatedAt = now,
                RemovedAt = null,
                RemovedBy = null
            };
            _members[(tenantId, userId)] = updated;
            return Task.FromResult(updated);
        }
    }

    public Task<TenantMembershipRecord> RemoveMemberAsync(
        RemoveMembershipCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tenantId = TenantIsolation.Normalize(command.TenantId);
        var userId = NormalizeRequired(command.UserId, "User id");
        var actor = NormalizeRequired(command.ActorUserId, "Actor user id");
        var now = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            EnsureTenant(tenantId);
            if (!_members.TryGetValue((tenantId, userId), out var membership) ||
                membership.Status != TenantMembershipStatuses.Active)
            {
                throw NotFound("membership_not_found", "The active membership was not found.");
            }

            EnsureNotLastAdmin(tenantId, membership, membership.Role, removing: true);
            var updated = membership with
            {
                Status = TenantMembershipStatuses.Removed,
                UpdatedAt = now,
                RemovedAt = now,
                RemovedBy = actor
            };
            _members[(tenantId, userId)] = updated;
            return Task.FromResult(updated);
        }
    }

    public Task<TenantInvitationRecord> CreateInvitationAsync(
        CreateTenantInvitationCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tenantId = TenantIsolation.Normalize(command.TenantId);
        var invitee = NormalizeRequired(command.InviteeUserId, "Invitee user id");
        var role = NormalizeMembershipRole(command.Role);
        var actor = NormalizeRequired(command.ActorUserId, "Actor user id");
        var hash = NormalizeTokenHash(command.TokenHash);
        var now = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            EnsureTenant(tenantId);
            if (_invitations.Values.Any(value =>
                value.Invitation.TenantId == tenantId &&
                value.Invitation.InviteeUserId == invitee &&
                value.Invitation.Status == TenantInvitationStatuses.Pending &&
                value.Invitation.ExpiresAt > now))
            {
                throw Conflict("pending_invitation_exists", "A pending invitation already exists for this user.");
            }

            var invitation = new TenantInvitationRecord(
                Guid.NewGuid(),
                tenantId,
                invitee,
                role,
                TenantInvitationStatuses.Pending,
                command.ExpiresAt,
                now,
                actor,
                null,
                null,
                null,
                null);
            _invitations[invitation.Id] = (invitation, hash);
            return Task.FromResult(invitation);
        }
    }

    public Task<IReadOnlyList<TenantInvitationRecord>> ListInvitationsAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        tenantId = TenantIsolation.Normalize(tenantId);
        EnsureTenant(tenantId);
        var now = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            foreach (var pair in _invitations.ToArray())
            {
                if (pair.Value.Invitation.TenantId == tenantId &&
                    pair.Value.Invitation.Status == TenantInvitationStatuses.Pending &&
                    pair.Value.Invitation.ExpiresAt <= now)
                {
                    _invitations[pair.Key] = (
                        pair.Value.Invitation with { Status = TenantInvitationStatuses.Expired },
                        pair.Value.TokenHash);
                }
            }

            IReadOnlyList<TenantInvitationRecord> result = _invitations.Values
                .Select(value => value.Invitation)
                .Where(invitation => invitation.TenantId == tenantId)
                .OrderByDescending(invitation => invitation.CreatedAt)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<TenantInvitationRecord> RevokeInvitationAsync(
        RevokeTenantInvitationCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tenantId = TenantIsolation.Normalize(command.TenantId);
        var actor = NormalizeRequired(command.ActorUserId, "Actor user id");
        var now = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            if (!_invitations.TryGetValue(command.InvitationId, out var stored) ||
                stored.Invitation.TenantId != tenantId)
            {
                throw NotFound("invitation_not_found", "The invitation was not found.");
            }

            if (stored.Invitation.Status != TenantInvitationStatuses.Pending)
            {
                throw Conflict("invitation_not_pending", "Only a pending invitation can be revoked.");
            }

            var updated = stored.Invitation with
            {
                Status = TenantInvitationStatuses.Revoked,
                RevokedAt = now,
                RevokedBy = actor
            };
            _invitations[command.InvitationId] = (updated, stored.TokenHash);
            return Task.FromResult(updated);
        }
    }

    public Task<TenantMembershipRecord> AcceptInvitationAsync(
        AcceptTenantInvitationCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tenantId = TenantIsolation.Normalize(command.TenantId);
        var invitee = NormalizeRequired(command.InviteeUserId, "Invitee user id");
        var actor = NormalizeRequired(command.ActorUserId, "Actor user id");
        var hash = NormalizeTokenHash(command.TokenHash);
        var now = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            EnsureTenantActive(tenantId);
            var pair = _invitations.FirstOrDefault(candidate =>
                candidate.Value.Invitation.TenantId == tenantId &&
                CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(candidate.Value.TokenHash),
                    Convert.FromHexString(hash)));

            if (pair.Key == Guid.Empty)
            {
                throw NotFound("invitation_not_found", "The invitation token is invalid.");
            }

            var stored = pair.Value;
            if (stored.Invitation.Status != TenantInvitationStatuses.Pending)
            {
                throw Conflict("invitation_not_pending", "The invitation is no longer pending.");
            }

            if (stored.Invitation.ExpiresAt <= now)
            {
                _invitations[pair.Key] = (
                    stored.Invitation with { Status = TenantInvitationStatuses.Expired },
                    stored.TokenHash);
                throw Conflict("invitation_expired", "The invitation has expired.");
            }

            if (!string.Equals(stored.Invitation.InviteeUserId, invitee, StringComparison.Ordinal) ||
                !string.Equals(invitee, actor, StringComparison.Ordinal))
            {
                throw new TenantLifecycleException(
                    "invitation_subject_mismatch",
                    "The invitation is not assigned to the authenticated user.",
                    StatusCodes.Status403Forbidden);
            }

            var membership = new TenantMembershipRecord(
                tenantId,
                invitee,
                stored.Invitation.Role,
                TenantMembershipStatuses.Active,
                now,
                stored.Invitation.CreatedBy,
                now,
                null,
                null);
            _members[(tenantId, invitee)] = membership;
            _invitations[pair.Key] = (
                stored.Invitation with
                {
                    Status = TenantInvitationStatuses.Accepted,
                    AcceptedAt = now,
                    AcceptedBy = actor
                },
                stored.TokenHash);
            return Task.FromResult(membership);
        }
    }

    private void EnsureTenant(string tenantId)
    {
        if (!_tenants.ContainsKey(tenantId))
        {
            throw NotFound("tenant_not_found", "The tenant was not found.");
        }
    }

    private void EnsureTenantActive(string tenantId)
    {
        if (!_tenants.TryGetValue(tenantId, out var tenant))
        {
            throw NotFound("tenant_not_found", "The tenant was not found.");
        }

        if (tenant.Status != TenantStatuses.Active)
        {
            throw Conflict("tenant_disabled", "The tenant is disabled.");
        }
    }

    private void EnsureNotLastAdmin(
        string tenantId,
        TenantMembershipRecord target,
        string replacementRole,
        bool removing)
    {
        if (target.Role != AppRoles.Admin ||
            (!removing && replacementRole == AppRoles.Admin))
        {
            return;
        }

        var activeAdminCount = _members.Values.Count(member =>
            member.TenantId == tenantId &&
            member.Status == TenantMembershipStatuses.Active &&
            member.Role == AppRoles.Admin);
        if (activeAdminCount <= 1)
        {
            throw Conflict(
                "last_tenant_admin",
                "The final active tenant administrator cannot be removed or downgraded.");
        }
    }

    internal static string NormalizeMembershipRole(string role)
    {
        if (string.Equals(role?.Trim(), AppRoles.User, StringComparison.Ordinal))
        {
            return AppRoles.User;
        }

        if (string.Equals(role?.Trim(), AppRoles.Admin, StringComparison.Ordinal))
        {
            return AppRoles.Admin;
        }

        throw new TenantLifecycleException(
            "invalid_membership_role",
            "Membership role must be User or Admin.",
            StatusCodes.Status400BadRequest);
    }

    internal static string NormalizeTenantStatus(string status)
    {
        if (string.Equals(status?.Trim(), TenantStatuses.Active, StringComparison.Ordinal))
        {
            return TenantStatuses.Active;
        }

        if (string.Equals(status?.Trim(), TenantStatuses.Disabled, StringComparison.Ordinal))
        {
            return TenantStatuses.Disabled;
        }

        throw new TenantLifecycleException(
            "invalid_tenant_status",
            "Tenant status must be Active or Disabled.",
            StatusCodes.Status400BadRequest);
    }

    internal static string NormalizeRequired(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new TenantLifecycleException(
                "required_value_missing",
                $"{label} is required.",
                StatusCodes.Status400BadRequest);
        }

        var normalized = value.Trim();
        if (normalized.Length > 200)
        {
            throw new TenantLifecycleException(
                "value_too_long",
                $"{label} cannot exceed 200 characters.",
                StatusCodes.Status400BadRequest);
        }

        return normalized;
    }

    internal static string NormalizeTokenHash(string tokenHash)
    {
        var normalized = NormalizeRequired(tokenHash, "Invitation token hash").ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new TenantLifecycleException(
                "invalid_invitation_token_hash",
                "Invitation token hash must be a SHA-256 hexadecimal value.",
                StatusCodes.Status400BadRequest);
        }

        return normalized;
    }

    internal static TenantLifecycleException NotFound(string code, string message) =>
        new(code, message, StatusCodes.Status404NotFound);

    internal static TenantLifecycleException Conflict(string code, string message) =>
        new(code, message, StatusCodes.Status409Conflict);
}

public static class TenantLifecycleServiceCollectionExtensions
{
    public static IServiceCollection AddTenantLifecycle(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new TenantLifecycleOptions();
        configuration.GetSection(TenantLifecycleOptions.SectionName).Bind(options);
        options.Validate();
        services.AddSingleton(options);

        if (!string.IsNullOrWhiteSpace(configuration.GetConnectionString("Postgres")))
        {
            services.AddSingleton<ITenantLifecycleRepository, PostgresTenantLifecycleRepository>();
        }
        else
        {
            services.AddSingleton<ITenantLifecycleRepository, InMemoryTenantLifecycleRepository>();
        }

        services.AddSingleton<TenantInvitationTokenService>();
        services.AddSingleton<IAuthorizationHandler, ActiveTenantMembershipHandler>();
        return services;
    }
}
