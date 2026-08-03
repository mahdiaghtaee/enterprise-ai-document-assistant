using System.Text.RegularExpressions;
using EnterpriseDocumentAssistant.Api.Observability;

namespace EnterpriseDocumentAssistant.Api.Documents;

public static class AnswerGenerationProviders
{
    public const string Deterministic = "Deterministic";
    public const string OpenAiCompatible = "OpenAiCompatible";
}

public static class GroundedAnswerStatuses
{
    public const string Answered = "answered";
    public const string InsufficientEvidence = "insufficient_evidence";
}

public static class GroundedAnswerReasonCodes
{
    public const string NoEvidence = "no_evidence";
    public const string LowConfidence = "low_confidence";
    public const string ConflictingEvidence = "conflicting_evidence";
    public const string ProviderDeclined = "provider_declined";
}

public sealed class AnswerGenerationOptions
{
    public const string SectionName = "AnswerGeneration";

    public string Provider { get; set; } = AnswerGenerationProviders.Deterministic;

    public double MinimumSourceScore { get; set; } = 0.20;

    public int MaxSources { get; set; } = 5;

    public int MaxContextCharacters { get; set; } = 12_000;

    public bool RejectNearTieEvidence { get; set; } = true;

    public double ConflictScoreDelta { get; set; } = 0.000001;

    public OpenAiCompatibleAnswerOptions OpenAiCompatible { get; set; } = new();

    public bool UsesOpenAiCompatibleProvider =>
        string.Equals(Provider, AnswerGenerationProviders.OpenAiCompatible, StringComparison.OrdinalIgnoreCase);

    public void Validate()
    {
        if (!string.Equals(Provider, AnswerGenerationProviders.Deterministic, StringComparison.OrdinalIgnoreCase) &&
            !UsesOpenAiCompatibleProvider)
        {
            throw new InvalidOperationException(
                $"Unsupported answer-generation provider '{Provider}'. Supported values are " +
                $"'{AnswerGenerationProviders.Deterministic}' and '{AnswerGenerationProviders.OpenAiCompatible}'.");
        }

        if (!double.IsFinite(MinimumSourceScore) || MinimumSourceScore is < 0 or > 1)
        {
            throw new InvalidOperationException("AnswerGeneration:MinimumSourceScore must be between zero and one.");
        }

        if (MaxSources is < 1 or > 20)
        {
            throw new InvalidOperationException("AnswerGeneration:MaxSources must be between 1 and 20.");
        }

        if (MaxContextCharacters is < 256 or > 100_000)
        {
            throw new InvalidOperationException(
                "AnswerGeneration:MaxContextCharacters must be between 256 and 100000.");
        }

        if (!double.IsFinite(ConflictScoreDelta) || ConflictScoreDelta is < 0 or > 1)
        {
            throw new InvalidOperationException("AnswerGeneration:ConflictScoreDelta must be between zero and one.");
        }

        if (UsesOpenAiCompatibleProvider)
        {
            OpenAiCompatible.Validate();
        }
    }
}

public sealed class OpenAiCompatibleAnswerOptions
{
    public string Endpoint { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 20;

    public int MaxOutputTokens { get; set; } = 500;

    public void Validate()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException(
                "AnswerGeneration:OpenAiCompatible:Endpoint must be an absolute URI.");
        }

        var isLoopbackHttp = endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback;
        if (endpoint.Scheme != Uri.UriSchemeHttps && !isLoopbackHttp)
        {
            throw new InvalidOperationException(
                "AnswerGeneration:OpenAiCompatible:Endpoint must use HTTPS unless it is a loopback development endpoint.");
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "AnswerGeneration:OpenAiCompatible:ApiKey is required when the OpenAiCompatible provider is selected.");
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            throw new InvalidOperationException(
                "AnswerGeneration:OpenAiCompatible:Model is required when the OpenAiCompatible provider is selected.");
        }

        if (TimeoutSeconds is < 1 or > 120)
        {
            throw new InvalidOperationException(
                "AnswerGeneration:OpenAiCompatible:TimeoutSeconds must be between 1 and 120.");
        }

        if (MaxOutputTokens is < 1 or > 8_192)
        {
            throw new InvalidOperationException(
                "AnswerGeneration:OpenAiCompatible:MaxOutputTokens must be between 1 and 8192.");
        }
    }
}

public interface IAnswerGenerator
{
    string ProviderName { get; }

    string? Model { get; }

    Task<AnswerGeneratorResult> GenerateAsync(
        AnswerGenerationRequest request,
        CancellationToken cancellationToken);
}

public interface IGroundedAnswerService
{
    Task<GroundedAnswerResult> GenerateAsync(
        string question,
        IReadOnlyList<DocumentAskSource> sources,
        CancellationToken cancellationToken);
}

public sealed record AnswerGenerationRequest(
    string Question,
    IReadOnlyList<GroundedAnswerSource> Sources)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Question))
        {
            throw new ArgumentException("Question is required.", nameof(Question));
        }

        if (Sources.Count == 0)
        {
            throw new ArgumentException("At least one grounded source is required.", nameof(Sources));
        }

        foreach (var source in Sources)
        {
            source.Validate();
        }
    }
}

public sealed record GroundedAnswerSource(
    int Number,
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    double Score,
    string Text)
{
    public void Validate()
    {
        if (Number <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Number));
        }

        if (DocumentId == Guid.Empty)
        {
            throw new ArgumentException("Document id is required.", nameof(DocumentId));
        }

        if (string.IsNullOrWhiteSpace(FileName))
        {
            throw new ArgumentException("File name is required.", nameof(FileName));
        }

        if (ChunkIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ChunkIndex));
        }

        if (!double.IsFinite(Score))
        {
            throw new ArgumentException("Source score must be finite.", nameof(Score));
        }

        if (string.IsNullOrWhiteSpace(Text))
        {
            throw new ArgumentException("Source text is required.", nameof(Text));
        }
    }
}

public sealed record AnswerGeneratorResult(
    string Answer,
    string Status,
    string? ReasonCode = null,
    AnswerTokenUsage? Usage = null)
{
    public static AnswerGeneratorResult Insufficient(string reasonCode) => new(
        GroundedAnswerService.InsufficientEvidenceMessage,
        GroundedAnswerStatuses.InsufficientEvidence,
        reasonCode);
}

public sealed record AnswerTokenUsage(
    int? InputTokens,
    int? OutputTokens,
    int? TotalTokens);

public sealed record GroundedAnswerResult(
    string Answer,
    string Status,
    string Provider,
    string? Model,
    bool IsGrounded,
    string? ReasonCode,
    IReadOnlyCollection<int> CitedSourceNumbers,
    AnswerTokenUsage? Usage);

public sealed class AnswerProviderException : Exception
{
    public AnswerProviderException(
        string code,
        string safeMessage,
        int statusCode,
        bool retryable,
        Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        Code = code;
        StatusCode = statusCode;
        Retryable = retryable;
    }

    public string Code { get; }

    public int StatusCode { get; }

    public bool Retryable { get; }
}

public sealed class DeterministicAnswerGenerator : IAnswerGenerator
{
    private const int MaximumExcerptLength = 400;

    public string ProviderName => "deterministic";

    public string? Model => "local-extractive-v1";

    public Task<AnswerGeneratorResult> GenerateAsync(
        AnswerGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        var source = request.Sources[0];
        var excerpt = source.Text.Length > MaximumExcerptLength
            ? source.Text[..MaximumExcerptLength] + "..."
            : source.Text;
        var safeFileName = string.Concat(source.FileName.Where(character => !char.IsControl(character)));
        var answer = $"Based on source [S1] from {safeFileName}: \"{excerpt}\"";

        return Task.FromResult(new AnswerGeneratorResult(
            answer,
            GroundedAnswerStatuses.Answered));
    }
}

public sealed class GroundedAnswerService : IGroundedAnswerService
{
    public const string InsufficientEvidenceMessage =
        "I could not find enough indexed document evidence to answer this question safely.";

    private static readonly Regex CitationPattern = new(
        @"\[S(?<number>[1-9][0-9]*)\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IAnswerGenerator _generator;
    private readonly AnswerGenerationOptions _options;

    public GroundedAnswerService(
        IAnswerGenerator generator,
        AnswerGenerationOptions options)
    {
        _generator = generator;
        _options = options;
    }

    public async Task<GroundedAnswerResult> GenerateAsync(
        string question,
        IReadOnlyList<DocumentAskSource> sources,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new ArgumentException("Question is required.", nameof(question));
        }

        ArgumentNullException.ThrowIfNull(sources);

        var usableSources = sources
            .Where(source => !string.IsNullOrWhiteSpace(source.Text))
            .Take(_options.MaxSources)
            .ToArray();

        if (usableSources.Length == 0)
        {
            return Insufficient(GroundedAnswerReasonCodes.NoEvidence);
        }

        if (usableSources[0].Score < _options.MinimumSourceScore)
        {
            return Insufficient(GroundedAnswerReasonCodes.LowConfidence);
        }

        if (_options.RejectNearTieEvidence &&
            usableSources.Length > 1 &&
            usableSources[0].DocumentId != usableSources[1].DocumentId &&
            Math.Abs(usableSources[0].Score - usableSources[1].Score) <= _options.ConflictScoreDelta)
        {
            return Insufficient(GroundedAnswerReasonCodes.ConflictingEvidence);
        }

        var boundedSources = BuildBoundedSources(usableSources, _options.MaxContextCharacters);
        if (boundedSources.Count == 0)
        {
            return Insufficient(GroundedAnswerReasonCodes.NoEvidence);
        }

        var generatorResult = await _generator.GenerateAsync(
            new AnswerGenerationRequest(question.Trim(), boundedSources),
            cancellationToken);

        if (string.Equals(
            generatorResult.Status,
            GroundedAnswerStatuses.InsufficientEvidence,
            StringComparison.Ordinal))
        {
            return new GroundedAnswerResult(
                InsufficientEvidenceMessage,
                GroundedAnswerStatuses.InsufficientEvidence,
                _generator.ProviderName,
                _generator.Model,
                IsGrounded: false,
                generatorResult.ReasonCode ?? GroundedAnswerReasonCodes.ProviderDeclined,
                CitedSourceNumbers: [],
                generatorResult.Usage);
        }

        if (!string.Equals(generatorResult.Status, GroundedAnswerStatuses.Answered, StringComparison.Ordinal))
        {
            throw new AnswerProviderException(
                "answer_provider_invalid_status",
                "The answer provider returned an unsupported result status.",
                StatusCodes.Status502BadGateway,
                retryable: false);
        }

        var citations = ParseCitations(generatorResult.Answer);
        if (citations.Count == 0 || citations.Any(number => number > boundedSources.Count))
        {
            throw new AnswerProviderException(
                "answer_provider_ungrounded_response",
                "The answer provider returned a response without valid source citations.",
                StatusCodes.Status502BadGateway,
                retryable: false);
        }

        return new GroundedAnswerResult(
            generatorResult.Answer,
            GroundedAnswerStatuses.Answered,
            _generator.ProviderName,
            _generator.Model,
            IsGrounded: true,
            ReasonCode: null,
            citations,
            generatorResult.Usage);
    }

    private GroundedAnswerResult Insufficient(string reasonCode) => new(
        InsufficientEvidenceMessage,
        GroundedAnswerStatuses.InsufficientEvidence,
        _generator.ProviderName,
        _generator.Model,
        IsGrounded: false,
        reasonCode,
        CitedSourceNumbers: [],
        Usage: null);

    private static IReadOnlyList<GroundedAnswerSource> BuildBoundedSources(
        IReadOnlyList<DocumentAskSource> sources,
        int maximumCharacters)
    {
        var remainingCharacters = maximumCharacters;
        var bounded = new List<GroundedAnswerSource>(sources.Count);

        foreach (var source in sources)
        {
            if (remainingCharacters <= 0)
            {
                break;
            }

            var normalizedText = source.Text.Trim();
            if (normalizedText.Length == 0)
            {
                continue;
            }

            var selectedText = normalizedText.Length > remainingCharacters
                ? normalizedText[..remainingCharacters]
                : normalizedText;

            bounded.Add(new GroundedAnswerSource(
                bounded.Count + 1,
                source.DocumentId,
                source.FileName,
                source.ChunkIndex,
                source.Score,
                selectedText));
            remainingCharacters -= selectedText.Length;
        }

        return bounded;
    }

    private static IReadOnlyCollection<int> ParseCitations(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return [];
        }

        return CitationPattern.Matches(answer)
            .Select(match => int.Parse(match.Groups["number"].Value))
            .Distinct()
            .Order()
            .ToArray();
    }
}

public static class AnswerGenerationServiceCollectionExtensions
{
    public static IServiceCollection AddConfiguredAnswerGeneration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new AnswerGenerationOptions();
        configuration.GetSection(AnswerGenerationOptions.SectionName).Bind(options);
        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton<DeterministicAnswerGenerator>();

        if (options.UsesOpenAiCompatibleProvider)
        {
            services.AddHttpClient<OpenAiCompatibleAnswerGenerator>(client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddHttpMessageHandler<CorrelationPropagationHandler>();
            services.AddTransient<IAnswerGenerator>(provider =>
                provider.GetRequiredService<OpenAiCompatibleAnswerGenerator>());
        }
        else
        {
            services.AddSingleton<IAnswerGenerator>(provider =>
                provider.GetRequiredService<DeterministicAnswerGenerator>());
        }

        services.AddTransient<IGroundedAnswerService, GroundedAnswerService>();
        return services;
    }
}
