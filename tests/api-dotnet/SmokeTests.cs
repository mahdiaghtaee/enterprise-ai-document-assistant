using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EnterpriseDocumentAssistant.Api.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Test_project_is_configured()
    {
        Assert.True(true);
    }
}

public sealed class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_endpoint_returns_ok_status()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal("document-api", root.GetProperty("service").GetString());
        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.True(root.TryGetProperty("checkedAt", out _));
    }

    [Fact]
    public async Task Create_document_metadata_returns_created_document()
    {
        var request = new
        {
            fileName = "sample-policy.txt",
            contentType = "text/plain"
        };

        var response = await _client.PostAsJsonAsync("/api/documents", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal("sample-policy.txt", root.GetProperty("fileName").GetString());
        Assert.Equal("text/plain", root.GetProperty("contentType").GetString());
        Assert.Equal("metadata-only", root.GetProperty("status").GetString());
    }
}
