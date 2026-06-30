using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
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

public sealed class ApiIntegrationTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ApiIntegrationTests(ApiWebApplicationFactory factory)
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

public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(FindApiContentRoot());
    }

    private static string FindApiContentRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "api-dotnet");
            var projectFile = Path.Combine(candidate, "EnterpriseDocumentAssistant.Api.csproj");

            if (File.Exists(projectFile))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate src/api-dotnet content root.");
    }
}
