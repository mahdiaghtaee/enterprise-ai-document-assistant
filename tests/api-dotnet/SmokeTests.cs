using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
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
}

public sealed class ApiWebApplicationFactory : WebApplicationFactory<global::Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseContentRoot(FindApiContentRoot());
        return base.CreateHost(builder);
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
