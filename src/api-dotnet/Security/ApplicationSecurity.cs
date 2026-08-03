using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace EnterpriseDocumentAssistant.Api.Security;

public static class AppRoles
{
    public const string User = "User";
    public const string Admin = "Admin";
    public const string PlatformAdmin = "PlatformAdmin";
}

public static class AuthorizationPolicies
{
    public const string DocumentAccess = "DocumentAccess";
    public const string AdminOnly = "AdminOnly";
    public const string TenantAdminOnly = "TenantAdminOnly";
    public const string InvitationAcceptance = "InvitationAcceptance";
    public const string PlatformAdminOnly = "PlatformAdminOnly";
}

public sealed class JwtOptions
{
    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    public string SigningKey { get; init; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer))
        {
            throw new InvalidOperationException("Jwt:Issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException("Jwt:Audience is required.");
        }

        if (string.IsNullOrWhiteSpace(SigningKey) || Encoding.UTF8.GetByteCount(SigningKey) < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey must contain at least 32 UTF-8 bytes.");
        }
    }
}

public static class ApplicationSecurityServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationSecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var jwtOptions = configuration.GetSection("Jwt").Get<JwtOptions>()
            ?? throw new InvalidOperationException("Jwt configuration is required.");
        jwtOptions.Validate();

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = JwtRegisteredClaimNames.Name,
                    RoleClaimType = "role"
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                AuthorizationPolicies.DocumentAccess,
                policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim(JwtRegisteredClaimNames.Sub)
                    .RequireClaim(TenantClaims.TenantId)
                    .RequireAssertion(context =>
                        context.User.IsInRole(AppRoles.User) ||
                        context.User.IsInRole(AppRoles.Admin) ||
                        context.User.IsInRole(AppRoles.PlatformAdmin))
                    .AddRequirements(new ActiveTenantMembershipRequirement()));
            options.AddPolicy(
                AuthorizationPolicies.AdminOnly,
                policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim(JwtRegisteredClaimNames.Sub)
                    .RequireClaim(TenantClaims.TenantId)
                    .RequireRole(AppRoles.Admin, AppRoles.PlatformAdmin)
                    .AddRequirements(new ActiveTenantMembershipRequirement(AppRoles.Admin)));
            options.AddPolicy(
                AuthorizationPolicies.TenantAdminOnly,
                policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim(JwtRegisteredClaimNames.Sub)
                    .RequireClaim(TenantClaims.TenantId)
                    .RequireRole(AppRoles.Admin, AppRoles.PlatformAdmin)
                    .AddRequirements(new ActiveTenantMembershipRequirement(AppRoles.Admin)));
            options.AddPolicy(
                AuthorizationPolicies.InvitationAcceptance,
                policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim(JwtRegisteredClaimNames.Sub)
                    .RequireClaim(TenantClaims.TenantId)
                    .RequireRole(AppRoles.User, AppRoles.Admin));
            options.AddPolicy(
                AuthorizationPolicies.PlatformAdminOnly,
                policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim(JwtRegisteredClaimNames.Sub)
                    .RequireClaim(TenantClaims.TenantId)
                    .RequireRole(AppRoles.PlatformAdmin));
        });

        return services;
    }
}
