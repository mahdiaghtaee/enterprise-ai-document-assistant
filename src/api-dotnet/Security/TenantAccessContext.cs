using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace EnterpriseDocumentAssistant.Api.Security;

public static class TenantClaims
{
    public const string TenantId = "tenant_id";
}

public static class TenantIsolation
{
    public const string LegacyTenantId = "legacy-tenant";

    public static string Normalize(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        return tenantId.Trim();
    }
}

public sealed record DocumentAccessContext(
    string UserId,
    string TenantId,
    bool CanAccessAllTenants,
    bool CanAccessAllDocumentsInTenant)
{
    public string? TenantFilter => CanAccessAllTenants ? null : TenantId;

    public string? OwnerFilter =>
        CanAccessAllTenants || CanAccessAllDocumentsInTenant ? null : UserId;

    public bool UsePrivilegedDatabase => CanAccessAllTenants;

    public static DocumentAccessContext FromPrincipal(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (principal.Identity?.IsAuthenticated != true)
        {
            throw new InvalidOperationException("An authenticated principal is required.");
        }

        var userId = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var tenantId = principal.FindFirst(TenantClaims.TenantId)?.Value;

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new InvalidOperationException("The authenticated token does not contain a subject claim.");
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new InvalidOperationException("The authenticated token does not contain a tenant_id claim.");
        }

        var isPlatformAdmin = principal.IsInRole(AppRoles.PlatformAdmin);
        var isTenantAdmin = principal.IsInRole(AppRoles.Admin);

        return new DocumentAccessContext(
            userId.Trim(),
            TenantIsolation.Normalize(tenantId),
            isPlatformAdmin,
            isPlatformAdmin || isTenantAdmin);
    }
}
