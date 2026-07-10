using System.Net;
using System.Net.Http.Json;
using EnterpriseDocumentAssistant.Api.Documents;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EnterpriseDocumentAssistant.Api.Tests;

public sealed class DocumentApiIntegrationTests : IClassFixture<DocumentApiFactory>
{
    private readonly HttpClient _client;

    public DocumentApiIntegrationTests(DocumentApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_returns_ok_service_status()
    {
        var response = await _client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.NotNull(payload);
        Assert.Equal("document-api", payload.Service);
        Assert.Equal("ok", payload.Status);
    }

    [Fact]
    public async Task Create_document_is_returned_by_document_list()
    {
        var fileName = $"integration-{Guid.NewGuid():N}.txt";
        var createResponse = await _client.PostAsJsonAsync(
            "/api/documents",
            new CreateDocumentRequest(fileName, "text/plain"));

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var listResponse = await _client.GetAsync("/api/documents");
        listResponse.EnsureSuccessStatusCode();

        var documents = await listResponse.Content.ReadFromJsonAsync<DocumentRecord[]>();

        Assert.NotNull(documents);
        Assert.Contains(documents, document => document.FileName == fileName);
    }

    [Theory]
    [InlineData("/api/documents/search", "{\"query\":\"\",\"topK\":3}")]
    [InlineData("/api/documents/search", "{\"query\":\"policy\",\"topK\":0}")]
    [InlineData("/api/documents/ask", "{\"question\":\"\",\"topK\":3}")]
    [InlineData("/api/documents/ask", "{\"question\":\"Who approves this?\",\"topK\":0}")]
    public async Task Invalid_search_and_ask_requests_return_bad_request(
        string path,
        string json)
    {
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _client.PostAsync(path, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record HealthResponse(string Service, string Status);
}

public sealed class DocumentApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDocumentRepository>();
            services.AddSingleton<IDocumentRepository, InMemoryDocumentRepository>();
        });
    }
}
