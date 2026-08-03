using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using EnterpriseDocumentAssistant.Api;
using EnterpriseDocumentAssistant.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EnterpriseDocumentAssistant.Api.Tests;

public sealed class TenantLifecycleTests
{
    [Fact]
    public async Task Provision_creates_active_tenant_and_initial_admin()
    {
        var repository = new InMemoryTenantLifecycleRepository();

        var tenant = await repository.ProvisionAsync(
            new ProvisionTenantCommand("tenant-a", "Tenant A", "admin-a", "platform-admin"),
            CancellationToken.None);
        var access = await repository.EvaluateAccessAsync(
            "tenant-a",
            "admin-a",
            CancellationToken.None);

        Assert.Equal(TenantStatuses.Active, tenant.Status);
        Assert.True(access.IsManaged);
        Assert.True(access.CanUseTenant);
        Assert.Equal(AppRoles.Admin, access.MembershipRole);
    }

    [Fact]
    public async Task Final_admin_cannot_be_removed_or_downgraded()
    {
        var repository = new InMemoryTenantLifecycleRepository();
        await repository.ProvisionAsync(
            new ProvisionTenantCommand("tenant-a", "Tenant A", "admin-a", "platform-admin"),
            CancellationToken.None);

        var remove = await Assert.ThrowsAsync<TenantLifecycleException>(() =>
            repository.RemoveMemberAsync(
                new RemoveMembershipCommand("tenant-a", "admin-a", "admin-a"),
                CancellationToken.None));
        var downgrade = await Assert.ThrowsAsync<TenantLifecycleException>(() =>
            repository.SetMemberRoleAsync(
                new SetMembershipRoleCommand("tenant-a", "admin-a", AppRoles.User, "admin-a"),
                CancellationToken.None));

        Assert.Equal("last_tenant_admin", remove.Code);
        Assert.Equal("last_tenant_admin", downgrade.Code);
    }

    [Fact]
    public async Task Invitation_is_one_time_and_bound_to_authenticated_subject()
    {
        var repository = new InMemoryTenantLifecycleRepository();
        var options = new TenantLifecycleOptions();
        var tokens = new TenantInvitationTokenService(options);
        await repository.ProvisionAsync(
            new ProvisionTenantCommand("tenant-a", "Tenant A", "admin-a", "platform-admin"),
            CancellationToken.None);
        var secret = tokens.Create(24, DateTimeOffset.UtcNow);
        await repository.CreateInvitationAsync(
            new CreateTenantInvitationCommand(
                "tenant-a",
                "user-a",
                AppRoles.User,
                secret.TokenHash,
                secret.ExpiresAt,
                "admin-a"),
            CancellationToken.None);

        var mismatch = await Assert.ThrowsAsync<TenantLifecycleException>(() =>
            repository.AcceptInvitationAsync(
                new AcceptTenantInvitationCommand(
                    "tenant-a",
                    "user-b",
                    TenantInvitationTokenService.Hash(secret.Token),
                    "user-b"),
                CancellationToken.None));
        Assert.Equal("invitation_subject_mismatch", mismatch.Code);

        var membership = await repository.AcceptInvitationAsync(
            new AcceptTenantInvitationCommand(
                "tenant-a",
                "user-a",
                TenantInvitationTokenService.Hash(secret.Token),
                "user-a"),
            CancellationToken.None);
        Assert.Equal(TenantMembershipStatuses.Active, membership.Status);

        var replay = await Assert.ThrowsAsync<TenantLifecycleException>(() =>
            repository.AcceptInvitationAsync(
                new AcceptTenantInvitationCommand(
                    "tenant-a",
                    "user-a",
                    TenantInvitationTokenService.Hash(secret.Token),
                    "user-a"),
                CancellationToken.None));
        Assert.Equal("invitation_not_pending", replay.Code);
    }

    [Fact]
    public async Task Removed_membership_and_disabled_tenant_fail_authorization()
    {
        var repository = new InMemoryTenantLifecycleRepository();
        var options = new TenantLifecycleOptions { AllowUnmanagedClaimsFallback = false };
        var handler = new ActiveTenantMembershipHandler(repository, options);
        await repository.ProvisionAsync(
            new ProvisionTenantCommand("tenant-a", "Tenant A", "admin-a", "platform-admin"),
            CancellationToken.None);
        var secret = new TenantInvitationTokenService(options).Create(24, DateTimeOffset.UtcNow);
        await repository.CreateInvitationAsync(
            new CreateTenantInvitationCommand(
                "tenant-a",
                "user-a",
                AppRoles.User,
                secret.TokenHash,
                secret.ExpiresAt,
                "admin-a"),
            CancellationToken.None);
        await repository.AcceptInvitationAsync(
            new AcceptTenantInvitationCommand(
                "tenant-a",
                "user-a",
                secret.TokenHash,
                "user-a"),
            CancellationToken.None);

        Assert.True(await AuthorizeAsync(handler, Principal("user-a", AppRoles.User, "tenant-a")));

        await repository.RemoveMemberAsync(
            new RemoveMembershipCommand("tenant-a", "user-a", "admin-a"),
            CancellationToken.None);
        Assert.False(await AuthorizeAsync(handler, Principal("user-a", AppRoles.User, "tenant-a")));

        await repository.SetStatusAsync(
            new SetTenantStatusCommand("tenant-a", TenantStatuses.Disabled, "platform-admin"),
            CancellationToken.None);
        Assert.False(await AuthorizeAsync(handler, Principal("admin-a", AppRoles.Admin, "tenant-a")));
    }

    [Fact]
    public async Task Stale_admin_claim_cannot_elevate_user_membership()
    {
        var repository = new InMemoryTenantLifecycleRepository();
        var options = new TenantLifecycleOptions { AllowUnmanagedClaimsFallback = false };
        var handler = new ActiveTenantMembershipHandler(repository, options);
        await repository.ProvisionAsync(
            new ProvisionTenantCommand("tenant-a", "Tenant A", "admin-a", "platform-admin"),
            CancellationToken.None);
        var secret = new TenantInvitationTokenService(options).Create(24, DateTimeOffset.UtcNow);
        await repository.CreateInvitationAsync(
            new CreateTenantInvitationCommand(
                "tenant-a",
                "user-a",
                AppRoles.User,
                secret.TokenHash,
                secret.ExpiresAt,
                "admin-a"),
            CancellationToken.None);
        await repository.AcceptInvitationAsync(
            new AcceptTenantInvitationCommand("tenant-a", "user-a", secret.TokenHash, "user-a"),
            CancellationToken.None);

        Assert.False(await AuthorizeAsync(
            handler,
            Principal("user-a", AppRoles.Admin, "tenant-a")));
    }

    [Theory]
    [InlineData("Combined", true, true)]
    [InlineData("Api", true, false)]
    [InlineData("Worker", false, true)]
    [InlineData("worker", false, true)]
    public void Hosting_mode_selects_expected_process_boundaries(
        string configuredMode,
        bool runsApi,
        bool runsWorker)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApplicationMode"] = configuredMode
            })
            .Build();

        var mode = ApplicationHostingMode.FromConfiguration(configuration);

        Assert.Equal(runsApi, mode.RunsApi);
        Assert.Equal(runsWorker, mode.RunsWorker);
    }

    [Fact]
    public void Invalid_hosting_mode_and_invitation_lifetime_fail_closed()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApplicationMode"] = "unsupported"
            })
            .Build();
        Assert.Throws<InvalidOperationException>(() =>
            ApplicationHostingMode.FromConfiguration(configuration));

        var options = new TenantLifecycleOptions
        {
            DefaultInvitationLifetimeHours = 48,
            MaximumInvitationLifetimeHours = 24
        };
        Assert.Throws<InvalidOperationException>(options.Validate);

        var tokenService = new TenantInvitationTokenService(new TenantLifecycleOptions());
        var exception = Assert.Throws<TenantLifecycleException>(() =>
            tokenService.Create(0, DateTimeOffset.UtcNow));
        Assert.Equal("invalid_invitation_lifetime", exception.Code);
    }

    [Fact]
    public void Invitation_hash_rejects_blank_token_and_is_stable()
    {
        var first = TenantInvitationTokenService.Hash("one-time-token");
        var second = TenantInvitationTokenService.Hash("one-time-token");

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.Throws<TenantLifecycleException>(() =>
            TenantInvitationTokenService.Hash(" "));
    }

    private static async Task<bool> AuthorizeAsync(
        ActiveTenantMembershipHandler handler,
        ClaimsPrincipal principal)
    {
        var requirement = new ActiveTenantMembershipRequirement();
        var context = new AuthorizationHandlerContext([requirement], principal, resource: null);
        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    private static ClaimsPrincipal Principal(string userId, string role, string tenantId)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId),
                new Claim("role", role),
                new Claim(TenantClaims.TenantId, tenantId)
            ],
            authenticationType: "test",
            nameType: "name",
            roleType: "role");
        return new ClaimsPrincipal(identity);
    }
}

public sealed class TenantLifecycleEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TenantLifecycleEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Provision_invite_accept_remove_and_deactivate_take_effect_immediately()
    {
        using var platform = JwtTestToken.CreateAuthenticatedClient(
            _factory,
            "platform-admin",
            AppRoles.PlatformAdmin,
            "platform");
        var provision = await platform.PostAsJsonAsync(
            "/api/platform/tenants",
            new ProvisionTenantRequest("managed-tenant", "Managed tenant", "tenant-admin"));
        Assert.Equal(HttpStatusCode.Created, provision.StatusCode);

        using var admin = JwtTestToken.CreateAuthenticatedClient(
            _factory,
            "tenant-admin",
            AppRoles.Admin,
            "managed-tenant");
        var invitationResponse = await admin.PostAsJsonAsync(
            "/api/tenant/invitations",
            new CreateTenantInvitationRequest("member-a", AppRoles.User, 24));
        Assert.Equal(HttpStatusCode.Created, invitationResponse.StatusCode);
        var invitation = await invitationResponse.Content.ReadFromJsonAsync<TenantInvitationCreatedResponse>();
        Assert.NotNull(invitation);
        Assert.False(string.IsNullOrWhiteSpace(invitation!.Token));

        using var member = JwtTestToken.CreateAuthenticatedClient(
            _factory,
            "member-a",
            AppRoles.User,
            "managed-tenant");
        var beforeAcceptance = await member.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Forbidden, beforeAcceptance.StatusCode);

        var accepted = await member.PostAsJsonAsync(
            "/api/tenant/invitations/accept",
            new AcceptTenantInvitationRequest(invitation.Token));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        var afterAcceptance = await member.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, afterAcceptance.StatusCode);

        var removed = await admin.DeleteAsync("/api/tenant/members/member-a");
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/auth/me")).StatusCode);

        var disabled = await platform.PostAsJsonAsync(
            "/api/platform/tenants/managed-tenant/status",
            new SetTenantStatusRequest(TenantStatuses.Disabled));
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/auth/me")).StatusCode);
    }
}
