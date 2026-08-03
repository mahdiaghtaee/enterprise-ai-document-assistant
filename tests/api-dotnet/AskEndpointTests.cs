using System.Net;
using System.Net.Http.Json;
using EnterpriseDocumentAssistant.Api.Documents;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        using var client = JwtTestToken.CreateAuthenticatedClient(_factory);

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
        using var client = JwtTestToken.CreateAuthenticatedClient(_factory);

        var response = await client.PostAsJsonAsync("/api/documents/ask", new
        {
            question = "Who approves vendor contracts?",
            topK = 0
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ask_ReturnsInsufficientEvidence_WhenNoDocumentsAreIndexed()
    {
        using var client = JwtTestToken.CreateAuthenticatedClient(_factory);

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
        Assert.Contains("could not find enough indexed document evidence", body.Answer);
        Assert.Equal(GroundedAnswerStatuses.InsufficientEvidence, body.AnswerStatus);
        Assert.Equal("deterministic", body.AnswerProvider);
        Assert.Equal("local-extractive-v1", body.AnswerModel);
        Assert.False(body.IsGrounded);
        Assert.Equal(GroundedAnswerReasonCodes.NoEvidence, body.ReasonCode);
    }

    [Fact]
    public async Task Ask_ReturnsControlledProviderFailure_WithoutProviderDetails()
    {
        const string providerDetail = "secret-provider-response-body";
        using var configuredFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGroundedAnswerService>();
                services.AddSingleton<IGroundedAnswerService>(new FailingGroundedAnswerService(providerDetail));
            });
        });
        using var client = JwtTestToken.CreateAuthenticatedClient(configuredFactory);

        var response = await client.PostAsJsonAsync("/api/documents/ask", new
        {
            question = "Who approves vendor contracts?",
            topK = 3
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AskFailureResponse>();

        Assert.NotNull(body);
        Assert.Equal("answer_provider_unavailable", body.Code);
        Assert.True(body.Retryable);
        Assert.Equal(0, body.SourceCount);
        Assert.Contains("could not be reached", body.Message);
        Assert.DoesNotContain(providerDetail, body.Message);
        Assert.DoesNotContain(providerDetail, await response.Content.ReadAsStringAsync());
    }

    private sealed record AskResponse(
        string Question,
        string Answer,
        int SourceCount,
        string AnswerStatus,
        string AnswerProvider,
        string? AnswerModel,
        bool IsGrounded,
        string? ReasonCode);

    private sealed record AskFailureResponse(
        string Question,
        string Message,
        string Code,
        bool Retryable,
        int SourceCount);

    private sealed class FailingGroundedAnswerService : IGroundedAnswerService
    {
        private readonly string _providerDetail;

        public FailingGroundedAnswerService(string providerDetail)
        {
            _providerDetail = providerDetail;
        }

        public Task<GroundedAnswerResult> GenerateAsync(
            string question,
            IReadOnlyList<DocumentAskSource> sources,
            CancellationToken cancellationToken)
        {
            throw new AnswerProviderException(
                "answer_provider_unavailable",
                "The configured answer provider could not be reached.",
                StatusCodes.Status503ServiceUnavailable,
                retryable: true,
                new HttpRequestException(_providerDetail));
        }
    }
}
