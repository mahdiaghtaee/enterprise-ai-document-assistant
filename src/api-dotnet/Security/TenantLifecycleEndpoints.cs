using System.Security.Claims;
using EnterpriseDocumentAssistant.Api.Audit;
using EnterpriseDocumentAssistant.Api.Observability;

namespace EnterpriseDocumentAssistant.Api.Security;

public sealed record ProvisionTenantRequest(
    string TenantId,
    string DisplayName,
    string InitialAdminUserId);

public sealed record SetTenantStatusRequest(string Status);

public sealed record SetTenantMemberRoleRequest(string Role);

public sealed record CreateTenantInvitationRequest(
    string InviteeUserId,
    string Role,
    int? LifetimeHours = null);

public sealed record AcceptTenantInvitationRequest(string Token);

public sealed record TenantInvitationCreatedResponse(
    Guid InvitationId,
    string TenantId,
    string InviteeUserId,
    string Role,
    string Status,
    DateTimeOffset ExpiresAt,
    string Token);

public static class TenantLifecycleEndpointExtensions
{
    public static IEndpointRouteBuilder MapTenantLifecycleEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var platform = endpoints.MapGroup("/api/platform/tenants")
            .RequireAuthorization(AuthorizationPolicies.PlatformAdminOnly);

        platform.MapPost("/", async (
            ProvisionTenantRequest request,
            ClaimsPrincipal principal,
            ITenantLifecycleRepository repository,
            IAuditEventRepository auditRepository,
            ICorrelationContextAccessor correlation,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
            await ExecuteAsync(async () =>
            {
                var access = DocumentAccessContext.FromPrincipal(principal);
                var tenant = await repository.ProvisionAsync(
                    new ProvisionTenantCommand(
                        request.TenantId,
                        request.DisplayName,
                        request.InitialAdminUserId,
                        access.UserId),
                    cancellationToken);
                await RecordAsync(
                    auditRepository,
                    access,
                    correlation,
                    AuditEventTypes.TenantProvisioned,
                    "provision",
                    "tenant",
                    tenant.TenantId,
                    "success",
                    new Dictionary<string, object?>
                    {
                        ["initialAdminUserId"] = request.InitialAdminUserId.Trim()
                    },
                    logger,
                    cancellationToken);
                return Results.Created($"/api/platform/tenants/{tenant.TenantId}", tenant);
            }));

        platform.MapPost("/{tenantId}/status", async (
            string tenantId,
            SetTenantStatusRequest request,
            ClaimsPrincipal principal,
            ITenantLifecycleRepository repository,
            IAuditEventRepository auditRepository,
            ICorrelationContextAccessor correlation,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
            await ExecuteAsync(async () =>
            {
                var access = DocumentAccessContext.FromPrincipal(principal);
                var tenant = await repository.SetStatusAsync(
                    new SetTenantStatusCommand(tenantId, request.Status, access.UserId),
                    cancellationToken);
                await RecordAsync(
                    auditRepository,
                    access,
                    correlation,
                    tenant.Status == TenantStatuses.Active
                        ? AuditEventTypes.TenantReactivated
                        : AuditEventTypes.TenantDeactivated,
                    tenant.Status == TenantStatuses.Active ? "reactivate" : "deactivate",
                    "tenant",
                    tenant.TenantId,
                    "success",
                    new Dictionary<string, object?> { ["status"] = tenant.Status },
                    logger,
                    cancellationToken);
                return Results.Ok(tenant);
            }));

        endpoints.MapPost("/api/tenant/invitations/accept", async (
            AcceptTenantInvitationRequest request,
            ClaimsPrincipal principal,
            ITenantLifecycleRepository repository,
            IAuditEventRepository auditRepository,
            ICorrelationContextAccessor correlation,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
            await ExecuteAsync(async () =>
            {
                var access = DocumentAccessContext.FromPrincipal(principal);
                var membership = await repository.AcceptInvitationAsync(
                    new AcceptTenantInvitationCommand(
                        access.TenantId,
                        access.UserId,
                        TenantInvitationTokenService.Hash(request.Token),
                        access.UserId),
                    cancellationToken);
                await RecordAsync(
                    auditRepository,
                    access,
                    correlation,
                    AuditEventTypes.TenantInvitationAccepted,
                    "accept_invitation",
                    "tenant_membership",
                    membership.UserId,
                    "success",
                    new Dictionary<string, object?> { ["membershipRole"] = membership.Role },
                    logger,
                    cancellationToken);
                return Results.Ok(membership);
            }))
            .RequireAuthorization(AuthorizationPolicies.InvitationAcceptance);

        var tenant = endpoints.MapGroup("/api/tenant")
            .RequireAuthorization(AuthorizationPolicies.TenantAdminOnly);

        tenant.MapGet("/members", async (
            ClaimsPrincipal principal,
            ITenantLifecycleRepository repository,
            CancellationToken cancellationToken) =>
            await ExecuteAsync(async () =>
            {
                var access = DocumentAccessContext.FromPrincipal(principal);
                var members = await repository.ListMembersAsync(
                    access.TenantId,
                    cancellationToken);
                return Results.Ok(members);
            }));

        tenant.MapPut("/members/{userId}/role", async (
            string userId,
            SetTenantMemberRoleRequest request,
            ClaimsPrincipal principal,
            ITenantLifecycleRepository repository,
            IAuditEventRepository auditRepository,
            ICorrelationContextAccessor correlation,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
            await ExecuteAsync(async () =>
            {
                var access = DocumentAccessContext.FromPrincipal(principal);
                var member = await repository.SetMemberRoleAsync(
                    new SetMembershipRoleCommand(
                        access.TenantId,
                        userId,
                        request.Role,
                        access.UserId),
                    cancellationToken);
                await RecordAsync(
                    auditRepository,
                    access,
                    correlation,
                    AuditEventTypes.TenantMembershipRoleChanged,
                    "change_role",
                    "tenant_membership",
                    member.UserId,
                    "success",
                    new Dictionary<string, object?> { ["membershipRole"] = member.Role },
                    logger,
                    cancellationToken);
                return Results.Ok(member);
            }));

        tenant.MapDelete("/members/{userId}", async (
            string userId,
            ClaimsPrincipal principal,
            ITenantLifecycleRepository repository,
            IAuditEventRepository auditRepository,
            ICorrelationContextAccessor correlation,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
            await ExecuteAsync(async () =>
            {
                var access = DocumentAccessContext.FromPrincipal(principal);
                var member = await repository.RemoveMemberAsync(
                    new RemoveMembershipCommand(
                        access.TenantId,
                        userId,
                        access.UserId),
                    cancellationToken);
                await RecordAsync(
                    auditRepository,
                    access,
                    correlation,
                    AuditEventTypes.TenantMembershipRemoved,
                    "remove_member",
                    "tenant_membership",
                    member.UserId,
                    "success",
                    details: null,
                    logger,
                    cancellationToken);
                return Results.Ok(member);
            }));

        tenant.MapPost("/invitations", async (
            CreateTenantInvitationRequest request,
            ClaimsPrincipal principal,
            ITenantLifecycleRepository repository,
            TenantInvitationTokenService tokenService,
            IAuditEventRepository auditRepository,
            ICorrelationContextAccessor correlation,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
            await ExecuteAsync(async () =>
            {
                var access = DocumentAccessContext.FromPrincipal(principal);
                var secret = tokenService.Create(request.LifetimeHours, DateTimeOffset.UtcNow);
                var invitation = await repository.CreateInvitationAsync(
                    new CreateTenantInvitationCommand(
                        access.TenantId,
                        request.InviteeUserId,
                        request.Role,
                        secret.TokenHash,
                        secret.ExpiresAt,
                        access.UserId),
                    cancellationToken);
                await RecordAsync(
                    auditRepository,
                    access,
                    correlation,
                    AuditEventTypes.TenantInvitationCreated,
                    "create_invitation",
                    "tenant_invitation",
                    invitation.Id.ToString(),
                    "success",
                    new Dictionary<string, object?>
                    {
                        ["inviteeUserId"] = invitation.InviteeUserId,
                        ["membershipRole"] = invitation.Role,
                        ["expiresAt"] = invitation.ExpiresAt
                    },
                    logger,
                    cancellationToken);
                return Results.Created(
                    $"/api/tenant/invitations/{invitation.Id}",
                    new TenantInvitationCreatedResponse(
                        invitation.Id,
                        invitation.TenantId,
                        invitation.InviteeUserId,
                        invitation.Role,
                        invitation.Status,
                        invitation.ExpiresAt,
                        secret.Token));
            }));

        tenant.MapGet("/invitations", async (
            ClaimsPrincipal principal,
            ITenantLifecycleRepository repository,
            CancellationToken cancellationToken) =>
            await ExecuteAsync(async () =>
            {
                var access = DocumentAccessContext.FromPrincipal(principal);
                var invitations = await repository.ListInvitationsAsync(
                    access.TenantId,
                    cancellationToken);
                return Results.Ok(invitations);
            }));

        tenant.MapPost("/invitations/{invitationId:guid}/revoke", async (
            Guid invitationId,
            ClaimsPrincipal principal,
            ITenantLifecycleRepository repository,
            IAuditEventRepository auditRepository,
            ICorrelationContextAccessor correlation,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
            await ExecuteAsync(async () =>
            {
                var access = DocumentAccessContext.FromPrincipal(principal);
                var invitation = await repository.RevokeInvitationAsync(
                    new RevokeTenantInvitationCommand(
                        access.TenantId,
                        invitationId,
                        access.UserId),
                    cancellationToken);
                await RecordAsync(
                    auditRepository,
                    access,
                    correlation,
                    AuditEventTypes.TenantInvitationRevoked,
                    "revoke_invitation",
                    "tenant_invitation",
                    invitation.Id.ToString(),
                    "success",
                    details: null,
                    logger,
                    cancellationToken);
                return Results.Ok(invitation);
            }));

        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (TenantLifecycleException exception)
        {
            return Results.Json(
                new
                {
                    message = exception.Message,
                    code = exception.Code
                },
                statusCode: exception.StatusCode);
        }
    }

    private static Task RecordAsync(
        IAuditEventRepository auditRepository,
        DocumentAccessContext access,
        ICorrelationContextAccessor correlation,
        string eventType,
        string action,
        string resourceType,
        string? resourceId,
        string outcome,
        IReadOnlyDictionary<string, object?>? details,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var correlationId = correlation.CorrelationId
            ?? CorrelationIdMiddleware.ResolveCorrelationId(null);
        return AuditEventRecorder.TryAppendAsync(
            auditRepository,
            AuditEventWrite.Create(
                access,
                correlationId,
                eventType,
                action,
                resourceType,
                resourceId,
                outcome,
                details),
            access.UsePrivilegedDatabase,
            logger,
            cancellationToken);
    }
}
