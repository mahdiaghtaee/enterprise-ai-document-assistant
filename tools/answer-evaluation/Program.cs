using System.Diagnostics;
using System.Text.Json;
using EnterpriseDocumentAssistant.Api.Documents;

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    WriteIndented = true,
    PropertyNameCaseInsensitive = true
};

try
{
    var command = EvaluationCommand.Parse(args);
    var dataset = await LoadAsync<AnswerEvaluationDataset>(command.DatasetPath, jsonOptions);
    var baseline = await LoadAsync<AnswerEvaluationBaseline>(command.BaselinePath, jsonOptions);
    dataset.Validate();
    baseline.Validate();

    if (!string.Equals(dataset.Version, baseline.DatasetVersion, StringComparison.Ordinal))
    {
        throw new InvalidDataException(
            $"Dataset version '{dataset.Version}' does not match baseline '{baseline.DatasetVersion}'.");
    }

    var report = await AnswerEvaluator.EvaluateAsync(dataset, baseline, CancellationToken.None);
    var reportJson = JsonSerializer.Serialize(report, jsonOptions);
    var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(command.OutputPath));
    if (!string.IsNullOrWhiteSpace(outputDirectory))
    {
        Directory.CreateDirectory(outputDirectory);
    }

    await File.WriteAllTextAsync(command.OutputPath, reportJson + Environment.NewLine);
    Console.WriteLine(reportJson);
    return report.Comparison.Passed ? 0 : 2;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Answer evaluation was cancelled.");
    return 130;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Answer evaluation failed: {exception.Message}");
    return 1;
}

static async Task<T> LoadAsync<T>(string path, JsonSerializerOptions options)
{
    await using var stream = File.OpenRead(path);
    return await JsonSerializer.DeserializeAsync<T>(stream, options)
        ?? throw new InvalidDataException($"'{path}' did not contain a valid {typeof(T).Name} object.");
}

internal sealed record EvaluationCommand(
    string DatasetPath,
    string BaselinePath,
    string OutputPath)
{
    public static EvaluationCommand Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["--dataset"] = "evaluation/answers/cases.v1.json",
            ["--baseline"] = "evaluation/answers/baseline.v1.json",
            ["--output"] = "artifacts/answer-evaluation.json"
        };

        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !values.ContainsKey(args[index]))
            {
                throw new ArgumentException(
                    "Arguments must be --dataset <path>, --baseline <path>, or --output <path>.");
            }

            values[args[index]] = args[index + 1];
        }

        return new EvaluationCommand(
            values["--dataset"],
            values["--baseline"],
            values["--output"]);
    }
}

internal sealed record AnswerEvaluationDataset(
    string Version,
    AnswerEvaluationOptions Options,
    IReadOnlyList<AnswerEvaluationCase> Cases)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Version))
        {
            throw new InvalidDataException("Dataset version is required.");
        }

        ArgumentNullException.ThrowIfNull(Options);
        ArgumentNullException.ThrowIfNull(Cases);
        Options.Validate();

        if (Cases.Count == 0)
        {
            throw new InvalidDataException("At least one answer-evaluation case is required.");
        }

        if (Cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != Cases.Count)
        {
            throw new InvalidDataException("Answer-evaluation case identifiers must be unique.");
        }

        foreach (var item in Cases)
        {
            item.Validate();
        }

        var requiredCategories = new[]
        {
            "grounded_answer",
            "insufficient_evidence",
            "grounding_rejection"
        };
        var presentCategories = Cases.Select(item => item.Category).ToHashSet(StringComparer.Ordinal);
        var missingCategories = requiredCategories.Where(category => !presentCategories.Contains(category)).ToArray();
        if (missingCategories.Length > 0)
        {
            throw new InvalidDataException(
                $"Dataset is missing required categories: {string.Join(", ", missingCategories)}.");
        }
    }

    public AnswerGenerationOptions ToRuntimeOptions() => new()
    {
        MinimumSourceScore = Options.MinimumSourceScore,
        MaxSources = Options.MaxSources,
        MaxContextCharacters = Options.MaxContextCharacters,
        RejectNearTieEvidence = Options.RejectNearTieEvidence,
        ConflictScoreDelta = Options.ConflictScoreDelta
    };
}

internal sealed record AnswerEvaluationOptions(
    double MinimumSourceScore,
    int MaxSources,
    int MaxContextCharacters,
    bool RejectNearTieEvidence,
    double ConflictScoreDelta)
{
    public void Validate()
    {
        var runtimeOptions = new AnswerGenerationOptions
        {
            MinimumSourceScore = MinimumSourceScore,
            MaxSources = MaxSources,
            MaxContextCharacters = MaxContextCharacters,
            RejectNearTieEvidence = RejectNearTieEvidence,
            ConflictScoreDelta = ConflictScoreDelta
        };
        runtimeOptions.Validate();
    }
}

internal sealed record AnswerEvaluationCase(
    string Id,
    string Category,
    string Question,
    string GeneratorMode,
    string? ScriptedAnswer,
    IReadOnlyList<AnswerEvaluationSource> Sources,
    AnswerEvaluationExpected Expected)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Category))
        {
            throw new InvalidDataException("Each evaluation case requires an id and category.");
        }

        if (string.IsNullOrWhiteSpace(Question))
        {
            throw new InvalidDataException($"Evaluation case '{Id}' requires a question.");
        }

        if (GeneratorMode is not ("deterministic" or "scripted_answer" or "scripted_insufficient"))
        {
            throw new InvalidDataException(
                $"Evaluation case '{Id}' has unsupported generator mode '{GeneratorMode}'.");
        }

        if (GeneratorMode == "scripted_answer" && string.IsNullOrWhiteSpace(ScriptedAnswer))
        {
            throw new InvalidDataException(
                $"Evaluation case '{Id}' requires scriptedAnswer for scripted_answer mode.");
        }

        ArgumentNullException.ThrowIfNull(Sources);
        ArgumentNullException.ThrowIfNull(Expected);
        foreach (var source in Sources)
        {
            source.Validate(Id);
        }
        Expected.Validate(Id);
    }
}

internal sealed record AnswerEvaluationSource(
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    double Score,
    string Text)
{
    public void Validate(string caseId)
    {
        if (DocumentId == Guid.Empty || string.IsNullOrWhiteSpace(FileName) ||
            ChunkIndex < 0 || !double.IsFinite(Score) || string.IsNullOrWhiteSpace(Text))
        {
            throw new InvalidDataException($"Evaluation case '{caseId}' contains an invalid source.");
        }
    }

    public DocumentAskSource ToDocumentSource() => new(
        DocumentId,
        FileName,
        ChunkIndex,
        Score,
        Text);
}

internal sealed record AnswerEvaluationExpected(
    string Outcome,
    string? ReasonCode,
    string? FailureCode,
    IReadOnlyList<int>? CitedSourceNumbers,
    bool? ProviderCallExpected)
{
    public void Validate(string caseId)
    {
        if (Outcome is not ("answered" or "insufficient_evidence" or "provider_failure"))
        {
            throw new InvalidDataException(
                $"Evaluation case '{caseId}' has unsupported expected outcome '{Outcome}'.");
        }
    }
}

internal sealed record AnswerEvaluationBaseline(
    string DatasetVersion,
    AnswerObservedBaseline Observed,
    AnswerEvaluationThresholds Thresholds)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DatasetVersion))
        {
            throw new InvalidDataException("Baseline dataset version is required.");
        }

        ArgumentNullException.ThrowIfNull(Observed);
        ArgumentNullException.ThrowIfNull(Thresholds);
        Observed.Validate();
        Thresholds.Validate();
    }
}

internal sealed record AnswerObservedBaseline(
    double CaseAccuracy,
    double GroundedAnswerAccuracy,
    double InsufficientEvidenceAccuracy,
    double GroundingRejectionAccuracy,
    double ProviderCallExpectationAccuracy)
{
    public void Validate()
    {
        ValidateUnit(CaseAccuracy, nameof(CaseAccuracy));
        ValidateUnit(GroundedAnswerAccuracy, nameof(GroundedAnswerAccuracy));
        ValidateUnit(InsufficientEvidenceAccuracy, nameof(InsufficientEvidenceAccuracy));
        ValidateUnit(GroundingRejectionAccuracy, nameof(GroundingRejectionAccuracy));
        ValidateUnit(ProviderCallExpectationAccuracy, nameof(ProviderCallExpectationAccuracy));
    }

    internal static void ValidateUnit(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new InvalidDataException($"{name} must be between zero and one.");
        }
    }
}

internal sealed record AnswerEvaluationThresholds(
    double MinimumCaseAccuracy,
    double MinimumGroundedAnswerAccuracy,
    double MinimumInsufficientEvidenceAccuracy,
    double MinimumGroundingRejectionAccuracy,
    double MinimumProviderCallExpectationAccuracy,
    double MaximumMeanLatencyMs)
{
    public void Validate()
    {
        AnswerObservedBaseline.ValidateUnit(MinimumCaseAccuracy, nameof(MinimumCaseAccuracy));
        AnswerObservedBaseline.ValidateUnit(MinimumGroundedAnswerAccuracy, nameof(MinimumGroundedAnswerAccuracy));
        AnswerObservedBaseline.ValidateUnit(MinimumInsufficientEvidenceAccuracy, nameof(MinimumInsufficientEvidenceAccuracy));
        AnswerObservedBaseline.ValidateUnit(MinimumGroundingRejectionAccuracy, nameof(MinimumGroundingRejectionAccuracy));
        AnswerObservedBaseline.ValidateUnit(MinimumProviderCallExpectationAccuracy, nameof(MinimumProviderCallExpectationAccuracy));

        if (!double.IsFinite(MaximumMeanLatencyMs) || MaximumMeanLatencyMs <= 0)
        {
            throw new InvalidDataException("MaximumMeanLatencyMs must be a positive finite value.");
        }
    }
}

internal static class AnswerEvaluator
{
    public static async Task<AnswerEvaluationReport> EvaluateAsync(
        AnswerEvaluationDataset dataset,
        AnswerEvaluationBaseline baseline,
        CancellationToken cancellationToken)
    {
        var results = new List<AnswerEvaluationCaseResult>(dataset.Cases.Count);

        foreach (var item in dataset.Cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var generator = new CountingAnswerGenerator(CreateGenerator(item));
            var service = new GroundedAnswerService(generator, dataset.ToRuntimeOptions());
            var stopwatch = Stopwatch.StartNew();
            string outcome;
            string? reasonCode = null;
            string? failureCode = null;
            IReadOnlyCollection<int> citedSources = [];

            try
            {
                var result = await service.GenerateAsync(
                    item.Question,
                    item.Sources.Select(source => source.ToDocumentSource()).ToArray(),
                    cancellationToken);
                outcome = result.Status;
                reasonCode = result.ReasonCode;
                citedSources = result.CitedSourceNumbers;
            }
            catch (AnswerProviderException exception)
            {
                outcome = "provider_failure";
                failureCode = exception.Code;
            }

            stopwatch.Stop();
            var passed = MatchesExpected(
                item.Expected,
                outcome,
                reasonCode,
                failureCode,
                citedSources,
                generator.CallCount);

            results.Add(new AnswerEvaluationCaseResult(
                item.Id,
                item.Category,
                item.Expected.Outcome,
                outcome,
                reasonCode,
                failureCode,
                citedSources,
                generator.CallCount,
                stopwatch.Elapsed.TotalMilliseconds,
                passed));
        }

        var metrics = AnswerEvaluationMetrics.Create(results, dataset.Cases);
        var comparison = Compare(metrics, baseline);
        return new AnswerEvaluationReport(
            dataset.Version,
            DateTimeOffset.UtcNow,
            results.Count,
            metrics,
            comparison,
            results);
    }

    private static IAnswerGenerator CreateGenerator(AnswerEvaluationCase item) => item.GeneratorMode switch
    {
        "deterministic" => new DeterministicAnswerGenerator(),
        "scripted_answer" => new ScriptedAnswerGenerator(
            new AnswerGeneratorResult(item.ScriptedAnswer!, GroundedAnswerStatuses.Answered)),
        "scripted_insufficient" => new ScriptedAnswerGenerator(
            AnswerGeneratorResult.Insufficient(GroundedAnswerReasonCodes.ProviderDeclined)),
        _ => throw new InvalidOperationException($"Unsupported generator mode '{item.GeneratorMode}'.")
    };

    private static bool MatchesExpected(
        AnswerEvaluationExpected expected,
        string outcome,
        string? reasonCode,
        string? failureCode,
        IReadOnlyCollection<int> citedSources,
        int providerCalls)
    {
        if (!string.Equals(expected.Outcome, outcome, StringComparison.Ordinal) ||
            !string.Equals(expected.ReasonCode, reasonCode, StringComparison.Ordinal) ||
            !string.Equals(expected.FailureCode, failureCode, StringComparison.Ordinal))
        {
            return false;
        }

        if (expected.CitedSourceNumbers is not null &&
            !expected.CitedSourceNumbers.Order().SequenceEqual(citedSources.Order()))
        {
            return false;
        }

        if (expected.ProviderCallExpected is bool callExpected && (providerCalls > 0) != callExpected)
        {
            return false;
        }

        return true;
    }

    private static AnswerBaselineComparison Compare(
        AnswerEvaluationMetrics metrics,
        AnswerEvaluationBaseline baseline)
    {
        var failures = new List<string>();
        var thresholds = baseline.Thresholds;
        AddMinimum(failures, "caseAccuracy", metrics.CaseAccuracy, thresholds.MinimumCaseAccuracy);
        AddMinimum(failures, "groundedAnswerAccuracy", metrics.GroundedAnswerAccuracy, thresholds.MinimumGroundedAnswerAccuracy);
        AddMinimum(failures, "insufficientEvidenceAccuracy", metrics.InsufficientEvidenceAccuracy, thresholds.MinimumInsufficientEvidenceAccuracy);
        AddMinimum(failures, "groundingRejectionAccuracy", metrics.GroundingRejectionAccuracy, thresholds.MinimumGroundingRejectionAccuracy);
        AddMinimum(failures, "providerCallExpectationAccuracy", metrics.ProviderCallExpectationAccuracy, thresholds.MinimumProviderCallExpectationAccuracy);

        if (metrics.MeanLatencyMs > thresholds.MaximumMeanLatencyMs)
        {
            failures.Add(
                $"meanLatencyMs {metrics.MeanLatencyMs:F6} exceeds maximum {thresholds.MaximumMeanLatencyMs:F6}.");
        }

        return new AnswerBaselineComparison(
            failures.Count == 0,
            baseline.Observed,
            thresholds,
            failures);
    }

    private static void AddMinimum(
        ICollection<string> failures,
        string metric,
        double observed,
        double minimum)
    {
        if (observed + 0.000000001 < minimum)
        {
            failures.Add($"{metric} {observed:F6} is below minimum {minimum:F6}.");
        }
    }
}

internal sealed record AnswerEvaluationMetrics(
    double CaseAccuracy,
    double GroundedAnswerAccuracy,
    double InsufficientEvidenceAccuracy,
    double GroundingRejectionAccuracy,
    double ProviderCallExpectationAccuracy,
    double MeanLatencyMs)
{
    public static AnswerEvaluationMetrics Create(
        IReadOnlyList<AnswerEvaluationCaseResult> results,
        IReadOnlyList<AnswerEvaluationCase> cases)
    {
        var expectedCallIds = cases
            .Where(item => item.Expected.ProviderCallExpected.HasValue)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var providerExpectationResults = results
            .Where(result => expectedCallIds.Contains(result.Id))
            .ToArray();

        return new AnswerEvaluationMetrics(
            Accuracy(results),
            Accuracy(results.Where(result => result.Category == "grounded_answer")),
            Accuracy(results.Where(result => result.Category == "insufficient_evidence")),
            Accuracy(results.Where(result => result.Category == "grounding_rejection")),
            Accuracy(providerExpectationResults),
            results.Average(result => result.DurationMs));
    }

    private static double Accuracy(IEnumerable<AnswerEvaluationCaseResult> source)
    {
        var values = source.ToArray();
        return values.Length == 0 ? 1 : values.Count(item => item.Passed) / (double)values.Length;
    }
}

internal sealed record AnswerEvaluationReport(
    string DatasetVersion,
    DateTimeOffset EvaluatedAtUtc,
    int CaseCount,
    AnswerEvaluationMetrics Metrics,
    AnswerBaselineComparison Comparison,
    IReadOnlyList<AnswerEvaluationCaseResult> Cases);

internal sealed record AnswerBaselineComparison(
    bool Passed,
    AnswerObservedBaseline BaselineObserved,
    AnswerEvaluationThresholds Thresholds,
    IReadOnlyList<string> Failures);

internal sealed record AnswerEvaluationCaseResult(
    string Id,
    string Category,
    string ExpectedOutcome,
    string ActualOutcome,
    string? ReasonCode,
    string? FailureCode,
    IReadOnlyCollection<int> CitedSourceNumbers,
    int ProviderCallCount,
    double DurationMs,
    bool Passed);

internal sealed class CountingAnswerGenerator : IAnswerGenerator
{
    private readonly IAnswerGenerator _inner;

    public CountingAnswerGenerator(IAnswerGenerator inner)
    {
        _inner = inner;
    }

    public string ProviderName => _inner.ProviderName;

    public string? Model => _inner.Model;

    public int CallCount { get; private set; }

    public async Task<AnswerGeneratorResult> GenerateAsync(
        AnswerGenerationRequest request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        return await _inner.GenerateAsync(request, cancellationToken);
    }
}

internal sealed class ScriptedAnswerGenerator : IAnswerGenerator
{
    private readonly AnswerGeneratorResult _result;

    public ScriptedAnswerGenerator(AnswerGeneratorResult result)
    {
        _result = result;
    }

    public string ProviderName => "scripted-provider";

    public string? Model => "evaluation-model";

    public Task<AnswerGeneratorResult> GenerateAsync(
        AnswerGenerationRequest request,
        CancellationToken cancellationToken)
    {
        request.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_result);
    }
}
