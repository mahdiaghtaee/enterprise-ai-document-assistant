using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EnterpriseDocumentAssistant.Api.Tests;

public sealed class AskEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AskEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Ask_ReturnsBadRequest_WhenQuestionIsEmpty()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/documents/ask", new
        {
            question = "",
            topK = 3
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ask_ReturnsBadRequest_WhenTopKIsInvalid()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/documents/ask", new
        {
            question = "Who approves vendor contracts?",
            topK = 0
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ask_ReturnsFallbackAnswer_WhenNoDocumentsAreIndexed()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/documents/ask", new
        {
            question = "Who approves vendor contracts?",
            topK = 3
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AskResponse>();

        Assert.NotNull(body);
        Assert.Equal("Who approves vendor contracts?", body.Question);
        Assert.Equal(0, body.SourceCount);
        Assert.Contains("could not find enough indexed document context", body.Answer);
    }

    private sealed record AskResponse(
        string Question,
        string Answer,
        int SourceCount);
}
