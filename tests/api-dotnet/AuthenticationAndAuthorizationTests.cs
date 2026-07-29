using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EnterpriseDocumentAssistant.Api.Documents;
using EnterpriseDocumentAssistant.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnterpriseDocumentAssistant.Api.Tests;

public sealed class AuthenticationAndAuthorizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthenticationAndAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Document_endpoints_reject_unauthenticated_requests()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/documents");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Document_policy_rejects_tokens_without_subject_claim()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestToken.Create("missing-subject", "User", includeSubject: false));

        var response = await client.GetAsync("/api/documents");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Document_policy_rejects_tokens_without_tenant_claim()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestToken.Create("missing-tenant", "User", includeTenant: false));

        var response = await client.GetAsync("/api/documents");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Me_returns_authenticated_tenant_and_access_scope()
    {
        using var client = JwtTestToken.CreateAuthenticatedClient(
            _factory,
            "user-a",
            "User",
            "tenant-a");

        var response = await client.GetAsync("/api/auth/me");
        response.EnsureSuccessStatusCode();
        var principal = await response.Content.ReadFromJsonAsync<AuthenticatedPrincipal>();

        Assert.NotNull(principal);
        Assert.Equal("user-a", principal!.UserId);
        Assert.Equal("tenant-a", principal.TenantId);
        Assert.Contains("User", principal.Roles);
        Assert.False(principal.CanAccessAllTenants);
        Assert.False(principal.CanAccessAllDocumentsInTenant);
    }

    [Fact]
    public async Task User_search_does_not_return_another_tenants_document()
    {
        var tenantADocumentId = Guid.NewGuid();
        var tenantBDocumentId = Guid.NewGuid();
        await StoreSemanticRecordsAsync(
            (tenantADocumentId, "tenant-a.txt", "tenant a policy", "tenant-a", "user-a"),
            (tenantBDocumentId, "tenant-b.txt", "vendor contract approval secret", "tenant-b", "user-b"));

        using var client = JwtTestToken.CreateAuthenticatedClient(
            _factory,
            "user-a",
            "User",
            "tenant-a");
        var response = await client.PostAsJsonAsync(
            "/api/documents/search",
            new DocumentSearchRequest("vendor contract approval secret", TopK: 10));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<DocumentSearchResponse>();

        Assert.NotNull(result);
        Assert.DoesNotContain(result!.Results, match => match.DocumentId == tenantBDocumentId);
        Assert.Contains(result.Results, match => match.DocumentId == tenantADocumentId);
    }

    [Fact]
    public async Task Tenant_admin_can_access_all_users_only_inside_its_tenant()
    {
        var sameTenantDocumentId = Guid.NewGuid();
        var otherTenantDocumentId = Guid.NewGuid();
        await StoreSemanticRecordsAsync(
            (sameTenantDocumentId, "same-tenant.txt", "shared tenant sample", "tenant-a", "user-b"),
            (otherTenantDocumentId, "other-tenant.txt", "shared tenant sample", "tenant-b", "user-c"));

        using var client = JwtTestToken.CreateAuthenticatedClient(
            _factory,
            "tenant-admin",
            "Admin",
            "tenant-a");
        var response = await client.PostAsJsonAsync(
            "/api/documents/search",
            new DocumentSearchRequest("shared tenant sample", TopK: 20));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<DocumentSearchResponse>();

        Assert.NotNull(result);
        Assert.Contains(result!.Results, match => match.DocumentId == sameTenantDocumentId);
        Assert.DoesNotContain(result.Results, match => match.DocumentId == otherTenantDocumentId);
    }

    [Fact]
    public async Task Platform_admin_search_can_return_documents_from_all_tenants()
    {
        var documentId = Guid.NewGuid();
        await StoreSemanticRecordsAsync(
            (documentId, "platform-visible.txt", "platform visibility sample", "tenant-b", "user-b"));

        using var client = JwtTestToken.CreateAuthenticatedClient(
            _factory,
            "platform-admin",
            AppRoles.PlatformAdmin,
            "platform");
        var response = await client.PostAsJsonAsync(
            "/api/documents/search",
            new DocumentSearchRequest("platform visibility sample", TopK: 20));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<DocumentSearchResponse>();

        Assert.NotNull(result);
        Assert.Contains(result!.Results, match => match.DocumentId == documentId);
    }

    [Fact]
    public void In_memory_repository_filters_documents_by_tenant_and_owner()
    {
        var repository = new InMemoryDocumentRepository();
        var ownerA = repository.Add("a.txt", "text/plain", 1, "a", "tenant-a", "user-a");
        var ownerB = repository.Add("b.txt", "text/plain", 1, "b", "tenant-a", "user-b");
        var tenantB = repository.Add("c.txt", "text/plain", 1, "c", "tenant-b", "user-a");

        Assert.Equal(new[] { ownerA }, repository.GetAll("tenant-a", "user-a"));
        Assert.Equal(2, repository.GetAll("tenant-a").Count);
        Assert.Null(repository.GetById(ownerB.Id, "tenant-a", "user-a"));
        Assert.Null(repository.GetById(tenantB.Id, "tenant-a"));
        Assert.Equal(3, repository.GetAll(bypassTenantIsolation: true).Count);
    }

    private async Task StoreSemanticRecordsAsync(
        params (Guid DocumentId, string FileName, string Text, string TenantId, string OwnerId)[] inputs)
    {
        using var scope = _factory.Services.CreateScope();
        var embeddingGenerator = scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator>();
        var store = scope.ServiceProvider.GetRequiredService<ISemanticIndexStore>();
        var embeddings = await embeddingGenerator.GenerateAsync(
            new EmbeddingRequest(inputs.Select(input => new EmbeddingInput(
                input.DocumentId,
                input.FileName,
                0,
                input.Text)).ToArray()),
            CancellationToken.None);

        await store.UpsertAsync(
            embeddings.Vectors.Select((vector, index) => new SemanticIndexRecord(
                vector.DocumentId,
                vector.FileName,
                vector.ChunkIndex,
                vector.Text,
                vector.Values,
                inputs[index].TenantId,
                inputs[index].OwnerId)).ToArray(),
            CancellationToken.None);
    }

    private sealed record AuthenticatedPrincipal(
        string UserId,
        string TenantId,
        IReadOnlyList<string> Roles,
        bool CanAccessAllTenants,
        bool CanAccessAllDocumentsInTenant);
}
