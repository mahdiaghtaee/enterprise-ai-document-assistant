using System.Diagnostics;
using EnterpriseDocumentAssistant.Api.Observability;
using EnterpriseDocumentAssistant.Api.Security;

namespace EnterpriseDocumentAssistant.Api.Audit;

public static class AuditEventTypes
{
    public const string DocumentsListed = "documents.listed";
    public const string DocumentMetadataCreated = "document.metadata_created";
    public const string DocumentUploadQueued = "document.upload_queued";
    public const string DocumentStatusRead = "document.status_read";
    public const string DocumentSearchExecuted = "document.search_executed";
    public const string DocumentAskExecuted = "document.ask_executed";
    public const string AuditEventsRead = "audit.events_read";
    public const string TenantProvisioned = "tenant.provisioned";
    public const string TenantDeactivated = "tenant.deactivated";
    public const string TenantReactivated = "tenant.reactivated";
    public const string TenantMembershipRoleChanged = "tenant.membership_role_changed";
    public const string TenantMembershipRemoved = "tenant.membership_removed";
    public const string TenantInvitationCreated = "tenant.invitation_created";
    public const string TenantInvitationAccepted = "tenant.invitation_accepted";
    public const string TenantInvitationRevoked = "tenant.invitation_revoked";
}

public sealed record AuditEventWrite(
    string TenantId,
    string ActorUserId,
    string ActorRole,
    string EventType,
    string Action,
    string ResourceType,
    string? ResourceId,
    string Outcome,
    string CorrelationId,
    string? TraceId,
    IReadOnlyDictionary<string, object?>? Details = null)
{
    public static AuditEventWrite Create(
        DocumentAccessContext access,
        string correlationId,
        string eventType,
        string action,
        string resourceType,
        string? resourceId,
        string outcome,
        IReadOnlyDictionary<string, object?>? details = null)
    {
        ArgumentNullException.ThrowIfNull(access);

        return new AuditEventWrite(
            access.TenantId,
            access.UserId,
            ResolveRole(access),
            NormalizeRequired(eventType, nameof(eventType), 100),
            NormalizeRequired(action, nameof(action), 100),
            NormalizeRequired(resourceType, nameof(resourceType), 100),
            NormalizeOptional(resourceId, 200),
            NormalizeRequired(outcome, nameof(outcome), 30),
            CorrelationIdMiddleware.ResolveCorrelationId(correlationId),
            Activity.Current?.TraceId.ToString(),
            details);
    }

    private static string ResolveRole(DocumentAccessContext access)
    {
        if (access.CanAccessAllTenants)
        {
            return AppRoles.PlatformAdmin;
        }

        return access.CanAccessAllDocumentsInTenant ? AppRoles.Admin : AppRoles.User;
    }

    private static string NormalizeRequired(string value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }
}

public sealed record AuditEventRecord(
    long Id,
    DateTimeOffset OccurredAt,
    string TenantId,
    string ActorUserId,
    string ActorRole,
    string EventType,
    string Action,
    string ResourceType,
    string? ResourceId,
    string Outcome,
    string CorrelationId,
    string? TraceId,
    IReadOnlyDictionary<string, object?> Details);

public sealed record AuditEventQuery(
    string? TenantId,
    bool BypassTenantIsolation,
    int Limit = 100)
{
    public void Validate()
    {
        if (Limit is <= 0 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(Limit), "Limit must be between 1 and 500.");
        }

        if (!BypassTenantIsolation)
        {
            TenantIsolation.Normalize(TenantId ?? string.Empty);
        }
    }
}
