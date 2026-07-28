using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EnterpriseDocumentAssistant.Api.Documents;
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
    public async Task Me_returns_authenticated_user_and_role()
    {
        using var client = JwtTestToken.CreateAuthenticatedClient(_factory, "user-a", "User");

        var response = await client.GetAsync("/api/auth/me");
        response.EnsureSuccessStatusCode();
        var principal = await response.Content.ReadFromJsonAsync<AuthenticatedPrincipal>();

        Assert.NotNull(principal);
        Assert.Equal("user-a", principal!.UserId);
        Assert.Contains("User", principal.Roles);
        Assert.False(principal.CanAccessAllDocuments);
    }

    [Fact]
    public async Task Search_does_not_return_another_users_document()
    {
        var ownerADocumentId = Guid.NewGuid();
        var ownerBDocumentId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var embeddingGenerator = scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator>();
        var store = scope.ServiceProvider.GetRequiredService<ISemanticIndexStore>();
        var embeddings = await embeddingGenerator.GenerateAsync(
            new EmbeddingRequest(
                new[]
                {
                    new EmbeddingInput(ownerADocumentId, "owner-a.txt", 0, "alpha private policy"),
                    new EmbeddingInput(ownerBDocumentId, "owner-b.txt", 0, "vendor contract approval secret")
                }),
            CancellationToken.None);

        await store.UpsertAsync(
            new[]
            {
                ToRecord(embeddings.Vectors[0], "user-a"),
                ToRecord(embeddings.Vectors[1], "user-b")
            },
            CancellationToken.None);

        using var client = JwtTestToken.CreateAuthenticatedClient(_factory, "user-a", "User");
        var response = await client.PostAsJsonAsync(
            "/api/documents/search",
            new DocumentSearchRequest("vendor contract approval secret", TopK: 10));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<DocumentSearchResponse>();

        Assert.NotNull(result);
        Assert.DoesNotContain(result!.Results, match => match.DocumentId == ownerBDocumentId);
        Assert.Contains(result.Results, match => match.DocumentId == ownerADocumentId);
    }

    [Fact]
    public async Task Admin_search_can_return_documents_from_all_owners()
    {
        var documentId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var embeddingGenerator = scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator>();
        var store = scope.ServiceProvider.GetRequiredService<ISemanticIndexStore>();
        var embeddings = await embeddingGenerator.GenerateAsync(
            new EmbeddingRequest(
                new[]
                {
                    new EmbeddingInput(documentId, "admin-visible.txt", 0, "administrator visibility sample")
                }),
            CancellationToken.None);
        await store.UpsertAsync(
            new[] { ToRecord(embeddings.Vectors[0], "user-b") },
            CancellationToken.None);

        using var client = JwtTestToken.CreateAuthenticatedClient(_factory, "admin-user", "Admin");
        var response = await client.PostAsJsonAsync(
            "/api/documents/search",
            new DocumentSearchRequest("administrator visibility sample", TopK: 20));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<DocumentSearchResponse>();

        Assert.NotNull(result);
        Assert.Contains(result!.Results, match => match.DocumentId == documentId);
    }

    [Fact]
    public void In_memory_repository_filters_documents_by_owner()
    {
        var repository = new InMemoryDocumentRepository();
        var ownerA = repository.Add("a.txt", "text/plain", 1, "a", "user-a");
        var ownerB = repository.Add("b.txt", "text/plain", 1, "b", "user-b");

        Assert.Equal(new[] { ownerA }, repository.GetAll("user-a"));
        Assert.Null(repository.GetById(ownerB.Id, "user-a"));
        Assert.Equal(2, repository.GetAll().Count);
    }

    private static SemanticIndexRecord ToRecord(EmbeddingVector vector, string ownerId) =>
        new(
            vector.DocumentId,
            vector.FileName,
            vector.ChunkIndex,
            vector.Text,
            vector.Values,
            ownerId);

    private sealed record AuthenticatedPrincipal(
        string UserId,
        IReadOnlyList<string> Roles,
        bool CanAccessAllDocuments);
}
