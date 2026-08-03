using EnterpriseDocumentAssistant.Api.Documents;
using Xunit;

namespace EnterpriseDocumentAssistant.Api.Tests;

public sealed class GroundedAnswerServiceTests
{
    [Fact]
    public async Task GenerateAsync_returns_grounded_deterministic_answer_with_valid_citation()
    {
        var service = CreateService(new DeterministicAnswerGenerator());
        var sources = new[]
        {
            Source(score: 0.95, text: "The Finance Director approves vendor contracts.")
        };

        var result = await service.GenerateAsync(
            "Who approves vendor contracts?",
            sources,
            CancellationToken.None);

        Assert.Equal(GroundedAnswerStatuses.Answered, result.Status);
        Assert.Equal("deterministic", result.Provider);
        Assert.Equal("local-extractive-v1", result.Model);
        Assert.True(result.IsGrounded);
        Assert.Null(result.ReasonCode);
        Assert.Contains("[S1]", result.Answer);
        Assert.Equal(new[] { 1 }, result.CitedSourceNumbers);
    }

    [Fact]
    public async Task GenerateAsync_returns_insufficient_evidence_without_sources()
    {
        var generator = new RecordingAnswerGenerator(
            new AnswerGeneratorResult("unused [S1]", GroundedAnswerStatuses.Answered));
        var service = CreateService(generator);

        var result = await service.GenerateAsync(
            "Who approves vendor contracts?",
            Array.Empty<DocumentAskSource>(),
            CancellationToken.None);

        Assert.Equal(GroundedAnswerStatuses.InsufficientEvidence, result.Status);
        Assert.Equal(GroundedAnswerReasonCodes.NoEvidence, result.ReasonCode);
        Assert.False(result.IsGrounded);
        Assert.Equal(0, generator.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_returns_low_confidence_without_calling_provider()
    {
        var generator = new RecordingAnswerGenerator(
            new AnswerGeneratorResult("unused [S1]", GroundedAnswerStatuses.Answered));
        var service = CreateService(generator, options => options.MinimumSourceScore = 0.50);

        var result = await service.GenerateAsync(
            "Who approves vendor contracts?",
            [Source(score: 0.49)],
            CancellationToken.None);

        Assert.Equal(GroundedAnswerStatuses.InsufficientEvidence, result.Status);
        Assert.Equal(GroundedAnswerReasonCodes.LowConfidence, result.ReasonCode);
        Assert.Equal(0, generator.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_returns_conflicting_evidence_for_near_tied_documents()
    {
        var generator = new RecordingAnswerGenerator(
            new AnswerGeneratorResult("unused [S1]", GroundedAnswerStatuses.Answered));
        var service = CreateService(generator, options =>
        {
            options.RejectNearTieEvidence = true;
            options.ConflictScoreDelta = 0.01;
        });

        var result = await service.GenerateAsync(
            "Who approves vendor contracts?",
            [
                Source(documentId: Guid.NewGuid(), score: 0.91, text: "Finance approves contracts."),
                Source(documentId: Guid.NewGuid(), score: 0.905, text: "Legal approves contracts.")
            ],
            CancellationToken.None);

        Assert.Equal(GroundedAnswerStatuses.InsufficientEvidence, result.Status);
        Assert.Equal(GroundedAnswerReasonCodes.ConflictingEvidence, result.ReasonCode);
        Assert.Equal(0, generator.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_rejects_provider_answer_without_source_citation()
    {
        var service = CreateService(new RecordingAnswerGenerator(
            new AnswerGeneratorResult(
                "The Finance Director approves vendor contracts.",
                GroundedAnswerStatuses.Answered)));

        var exception = await Assert.ThrowsAsync<AnswerProviderException>(() =>
            service.GenerateAsync(
                "Who approves vendor contracts?",
                [Source(score: 0.95)],
                CancellationToken.None));

        Assert.Equal("answer_provider_ungrounded_response", exception.Code);
        Assert.Equal(502, exception.StatusCode);
        Assert.False(exception.Retryable);
    }

    [Fact]
    public async Task GenerateAsync_rejects_citation_outside_supplied_source_range()
    {
        var service = CreateService(new RecordingAnswerGenerator(
            new AnswerGeneratorResult(
                "The answer is supported by [S2].",
                GroundedAnswerStatuses.Answered)));

        var exception = await Assert.ThrowsAsync<AnswerProviderException>(() =>
            service.GenerateAsync(
                "Who approves vendor contracts?",
                [Source(score: 0.95)],
                CancellationToken.None));

        Assert.Equal("answer_provider_ungrounded_response", exception.Code);
    }

    [Fact]
    public async Task GenerateAsync_maps_provider_decline_to_insufficient_evidence()
    {
        var service = CreateService(new RecordingAnswerGenerator(
            AnswerGeneratorResult.Insufficient(GroundedAnswerReasonCodes.ProviderDeclined)));

        var result = await service.GenerateAsync(
            "Who approves vendor contracts?",
            [Source(score: 0.95)],
            CancellationToken.None);

        Assert.Equal(GroundedAnswerStatuses.InsufficientEvidence, result.Status);
        Assert.Equal(GroundedAnswerReasonCodes.ProviderDeclined, result.ReasonCode);
        Assert.False(result.IsGrounded);
    }

    [Fact]
    public async Task GenerateAsync_limits_sources_and_context_before_provider_call()
    {
        var generator = new RecordingAnswerGenerator(
            new AnswerGeneratorResult("Supported by [S1].", GroundedAnswerStatuses.Answered));
        var service = CreateService(generator, options =>
        {
            options.MaxSources = 2;
            options.MaxContextCharacters = 256;
        });

        var result = await service.GenerateAsync(
            "Who approves vendor contracts?",
            [
                Source(score: 0.95, text: new string('A', 200)),
                Source(score: 0.80, text: new string('B', 200)),
                Source(score: 0.70, text: new string('C', 200))
            ],
            CancellationToken.None);

        Assert.True(result.IsGrounded);
        var request = Assert.IsType<AnswerGenerationRequest>(generator.LastRequest);
        Assert.Equal(2, request.Sources.Count);
        Assert.Equal(256, request.Sources.Sum(source => source.Text.Length));
        Assert.Equal(new[] { 1, 2 }, request.Sources.Select(source => source.Number));
    }

    private static GroundedAnswerService CreateService(
        IAnswerGenerator generator,
        Action<AnswerGenerationOptions>? configure = null)
    {
        var options = new AnswerGenerationOptions
        {
            MinimumSourceScore = 0.20,
            MaxSources = 5,
            MaxContextCharacters = 12_000,
            RejectNearTieEvidence = false
        };
        configure?.Invoke(options);
        options.Validate();
        return new GroundedAnswerService(generator, options);
    }

    private static DocumentAskSource Source(
        Guid? documentId = null,
        double score = 0.90,
        string text = "The Finance Director approves vendor contracts.") => new(
            documentId ?? Guid.NewGuid(),
            "policy.txt",
            0,
            score,
            text);

    private sealed class RecordingAnswerGenerator : IAnswerGenerator
    {
        private readonly AnswerGeneratorResult _result;

        public RecordingAnswerGenerator(AnswerGeneratorResult result)
        {
            _result = result;
        }

        public string ProviderName => "test-provider";

        public string? Model => "test-model";

        public int CallCount { get; private set; }

        public AnswerGenerationRequest? LastRequest { get; private set; }

        public Task<AnswerGeneratorResult> GenerateAsync(
            AnswerGenerationRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }
}
