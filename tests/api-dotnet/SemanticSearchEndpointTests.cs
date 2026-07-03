using System.Net;
using System.Net.Http.Json;
using EnterpriseDocumentAssistant.Api.Documents;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnterpriseDocumentAssistant.Api.Tests;

public sealed class SemanticSearchEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SemanticSearchEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Search_returns_bad_request_when_query_is_blank()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/documents/search",
            new DocumentSearchRequest("   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_returns_indexed_document_chunks()
    {
        var documentId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var embeddingGenerator = scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator>();
        var semanticIndexStore = scope.ServiceProvider.GetRequiredService<ISemanticIndexStore>();

        var embeddingResponse = await embeddingGenerator.GenerateAsync(
            new EmbeddingRequest(
                new[]
                {
                    new EmbeddingInput(documentId, "search-sample.txt", 0, "Enterprise AI document search sample")
                }),
            CancellationToken.None);

        var vector = embeddingResponse.Vectors[0];
        await semanticIndexStore.UpsertAsync(
            new[]
            {
                new SemanticIndexRecord(
                    vector.DocumentId,
                    vector.FileName,
                    vector.ChunkIndex,
                    vector.Text,
                    vector.Values)
            },
            CancellationToken.None);

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/documents/search",
            new DocumentSearchRequest("Enterprise AI search", TopK: 5));

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<DocumentSearchResponse>();

        Assert.NotNull(result);
        Assert.Equal("Enterprise AI search", result!.Query);
        Assert.Contains(result.Results, match =>
            match.DocumentId == documentId &&
            match.FileName == "search-sample.txt" &&
            match.ChunkIndex == 0 &&
            match.Text == "Enterprise AI document search sample");
    }
}
