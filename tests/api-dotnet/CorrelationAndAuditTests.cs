using System.Net;
using System.Net.Http.Json;
using EnterpriseDocumentAssistant.Api.Audit;
using EnterpriseDocumentAssistant.Api.Documents;
using EnterpriseDocumentAssistant.Api.Observability;
using EnterpriseDocumentAssistant.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EnterpriseDocumentAssistant.Api.Tests;

public sealed class CorrelationAndAuditTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CorrelationAndAuditTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_echoes_valid_correlation_id()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "correlation-test-123");

        using var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        Assert.Equal(
            "correlation-test-123",
            response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.Equal("correlation-test-123", payload!.CorrelationId);
    }

    [Fact]
    public async Task Health_replaces_invalid_correlation_id()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation(
            CorrelationIdMiddleware.HeaderName,
            "invalid correlation value");

        using var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var correlationId = response.Headers
            .GetValues(CorrelationIdMiddleware.HeaderName)
            .Single();
        Assert.NotEqual("invalid correlation value", correlationId);
        Assert.Equal(32, correlationId.Length);
    }

    [Fact]
    public void Log_correlation_id_is_deterministic_and_does_not_embed_external_input()
    {
        const string externalCorrelationId = "external-correlation-123";

        var first = CorrelationIdMiddleware.CreateLogCorrelationId(externalCorrelationId);
        var second = CorrelationIdMiddleware.CreateLogCorrelationId(externalCorrelationId);

        Assert.Equal(first, second);
        Assert.Equal(32, first.Length);
        Assert.DoesNotContain(externalCorrelationId, first, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("^[0-9A-F]{32}$", first);
    }

    [Fact]
    public async Task Ordinary_user_cannot_read_audit_events()
    {
        using var client = JwtTestToken.CreateAuthenticatedClient(
            _factory,
            "audit-user",
            AppRoles.User,
            "audit-tenant");

        using var response = await client.GetAsync("/api/audit/events");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(response.Headers.Contains(CorrelationIdMiddleware.HeaderName));
    }

    [Fact]
    public async Task Ordinary_user_cannot_verify_audit_integrity()
    {
        using var client = JwtTestToken.CreateAuthenticatedClient(
            _factory,
            "audit-user",
            AppRoles.User,
            "audit-integrity-tenant");

        using var response = await client.GetAsync("/api/audit/integrity");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Tenant_admin_verifies_only_authenticated_tenant()
    {
        var tenant = $"audit-integrity-{Guid.NewGuid():N}";
        using var admin = JwtTestToken.CreateAuthenticatedClient(
            _factory,
            "tenant-admin",
            AppRoles.Admin,
            tenant);

        using var ownResponse = await admin.GetAsync("/api/audit/integrity");
        ownResponse.EnsureSuccessStatusCode();
        var result = await ownResponse.Content.ReadFromJsonAsync<AuditIntegrityResult>();

        Assert.NotNull(result);
        Assert.Equal(tenant, result.TenantId);
        Assert.True(result.IsValid);

        using var foreignResponse = await admin.GetAsync(
            "/api/audit/integrity?tenantId=another-tenant");
        Assert.Equal(HttpStatusCode.Forbidden, foreignResponse.StatusCode);
    }

    [Fact]
    public async Task Platform_admin_requires_explicit_tenant_for_integrity_verification()
    {
        var targetTenant = $"platform-target-{Guid.NewGuid():N}";
        using var platformAdmin = JwtTestToken.CreateAuthenticatedClient(
            _factory,
            "platform-admin",
            AppRoles.PlatformAdmin,
            "platform");

        using var missingTenant = await platformAdmin.GetAsync("/api/audit/integrity");
        Assert.Equal(HttpStatusCode.BadRequest, missingTenant.StatusCode);

        using var explicitTenant = await platformAdmin.GetAsync(
            $"/api/audit/integrity?tenantId={targetTenant}");
        explicitTenant.EnsureSuccessStatusCode();
        var result = await explicitTenant.Content.ReadFromJsonAsync<AuditIntegrityResult>();

        Assert.NotNull(result);
        Assert.Equal(targetTenant, result.TenantId);
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Tenant_admin_reads_only_its_tenant_and_platform_admin_reads_all()
    {
        var marker = Guid.NewGuid().ToString("N");
        var tenantA = $"tenant-a-{marker}";
        var tenantB = $"tenant-b-{marker}";
        var correlationId = $"search-{marker}";

        using (var userA = JwtTestToken.CreateAuthenticatedClient(
            _factory,
            "user-a",
            AppRoles.User,
            tenantA))
        {
            userA.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, correlationId);
            using var searchResponse = await userA.PostAsJsonAsync(
                "/api/documents/search",
                new DocumentSearchRequest("audit-safe-query", TopK: 3));
            searchResponse.EnsureSuccessStatusCode();
        }

        IReadOnlyList<AuditEventRecord> tenantAEvents;
        using (var tenantAAdmin = JwtTestToken.CreateAuthenticatedClient(
            _factory,
            "tenant-a-admin",
            AppRoles.Admin,
            tenantA))
        {
            tenantAEvents = await tenantAAdmin.GetFromJsonAsync<List<AuditEventRecord>>(
                "/api/audit/events?limit=100") ?? [];
        }

        Assert.Contains(
            tenantAEvents,
            item => item.EventType == AuditEventTypes.DocumentSearchExecuted &&
                    item.CorrelationId == correlationId &&
                    item.TenantId == tenantA);

        IReadOnlyList<AuditEventRecord> tenantBEvents;
        using (var tenantBAdmin = JwtTestToken.CreateAuthenticatedClient(
            _factory,
            "tenant-b-admin",
            AppRoles.Admin,
            tenantB))
        {
            tenantBEvents = await tenantBAdmin.GetFromJsonAsync<List<AuditEventRecord>>(
                "/api/audit/events?limit=100") ?? [];
        }

        Assert.DoesNotContain(tenantBEvents, item => item.CorrelationId == correlationId);

        IReadOnlyList<AuditEventRecord> platformEvents;
        using (var platformAdmin = JwtTestToken.CreateAuthenticatedClient(
            _factory,
            "platform-admin",
            AppRoles.PlatformAdmin,
            "platform"))
        {
            platformEvents = await platformAdmin.GetFromJsonAsync<List<AuditEventRecord>>(
                "/api/audit/events?limit=500") ?? [];
        }

        Assert.Contains(platformEvents, item => item.CorrelationId == correlationId);
    }

    private sealed record HealthResponse(
        string Service,
        string Status,
        DateTimeOffset CheckedAt,
        string CorrelationId,
        string? TraceId);
}
