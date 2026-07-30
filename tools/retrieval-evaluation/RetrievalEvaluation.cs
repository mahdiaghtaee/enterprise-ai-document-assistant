using System.Diagnostics;
using System.Text.Json;
using EnterpriseDocumentAssistant.Api.Documents;

namespace EnterpriseDocumentAssistant.RetrievalEvaluation;

public static class RetrievalEvaluationCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static async Task<int> RunAsync(
        string[] args,
        IEmbeddingGenerator embeddingGenerator,
        ISemanticIndexStore semanticIndexStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(embeddingGenerator);
        ArgumentNullException.ThrowIfNull(semanticIndexStore);

        if (args.Any(argument => argument is "--help" or "-h"))
        {
            PrintUsage();
            return 0;
        }

        try
        {
            var options = RetrievalEvaluationCommandOptions.Parse(args);
            var dataset = await LoadAsync<RetrievalEvaluationDataset>(options.DatasetPath, cancellationToken);
            var baseline = await LoadAsync<RetrievalEvaluationBaseline>(options.BaselinePath, cancellationToken);

            dataset.Validate();
            baseline.Validate();

            var evaluator = new RetrievalEvaluator(embeddingGenerator, semanticIndexStore);
            var report = await evaluator.EvaluateAsync(dataset, baseline, cancellationToken);
            var serializedReport = JsonSerializer.Serialize(report, JsonOptions);
            var outputPath = Path.GetFullPath(options.OutputPath);
            var outputDirectory = Path.GetDirectoryName(outputPath);

            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            await File.WriteAllTextAsync(
                outputPath,
                serializedReport + Environment.NewLine,
                cancellationToken);

            Console.WriteLine(serializedReport);
            return report.Baseline.Passed ? 0 : 2;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine("Retrieval evaluation was cancelled.");
            return 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Retrieval evaluation failed: {exception.Message}");
            return 1;
        }
    }

    private static async Task<T> LoadAsync<T>(string path, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Evaluation input was not found: {fullPath}", fullPath);
        }

        var json = await File.ReadAllTextAsync(fullPath, cancellationToken);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidDataException($"Evaluation input is empty or invalid: {fullPath}");
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Usage: dotnet run --project tools/retrieval-evaluation -- " +
            "[--dataset <path>] [--baseline <path>] [--output <path>]");
    }
}

public sealed record RetrievalEvaluationCommandOptions(
    string DatasetPath,
    string BaselinePath,
    string OutputPath)
{
    public static RetrievalEvaluationCommandOptions Parse(IReadOnlyList<string> args)
    {
        var datasetPath = "evaluation/retrieval/corpus.v1.json";
        var baselinePath = "evaluation/retrieval/baseline.v1.json";
        var outputPath = "artifacts/retrieval-evaluation.json";

        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--dataset":
                    datasetPath = ReadValue(args, ref index, "--dataset");
                    break;
                case "--baseline":
                    baselinePath = ReadValue(args, ref index, "--baseline");
                    break;
                case "--output":
                    outputPath = ReadValue(args, ref index, "--output");
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[index]}");
            }
        }

        return new RetrievalEvaluationCommandOptions(datasetPath, baselinePath, outputPath);
    }

    private static string ReadValue(IReadOnlyList<string> args, ref int index, string option)
    {
        index++;

        if (index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        return args[index];
    }
}

public sealed record RetrievalEvaluationDataset(
    string Version,
    int TopK,
    string TenantId,
    string OwnerId,
    IReadOnlyList<RetrievalEvaluationDocument> Documents,
    IReadOnlyList<RetrievalEvaluationQuery> Queries)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Version))
        {
            throw new InvalidDataException("Dataset version is required.");
        }

        if (TopK <= 0)
        {
            throw new InvalidDataException("Dataset TopK must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(TenantId) || string.IsNullOrWhiteSpace(OwnerId))
        {
            throw new InvalidDataException("Dataset tenant and owner identifiers are required.");
        }

        if (Documents is null || Documents.Count == 0)
        {
            throw new InvalidDataException("At least one evaluation document is required.");
        }

        if (Queries is null || Queries.Count == 0)
        {
            throw new InvalidDataException("At least one evaluation query is required.");
        }

        var knownChunks = new HashSet<RetrievalRelevantChunk>();
        var documentIds = new HashSet<Guid>();

        foreach (var document in Documents)
        {
            document.Validate();

            if (!documentIds.Add(document.DocumentId))
            {
                throw new InvalidDataException($"Duplicate document id: {document.DocumentId}");
            }

            foreach (var chunk in document.Chunks)
            {
                knownChunks.Add(new RetrievalRelevantChunk(document.DocumentId, chunk.ChunkIndex));
            }
        }

        var queryIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var query in Queries)
        {
            query.Validate();

            if (!queryIds.Add(query.Id))
            {
                throw new InvalidDataException($"Duplicate query id: {query.Id}");
            }

            if (string.IsNullOrWhiteSpace(query.Query))
            {
                if (query.Relevant.Count != 0)
                {
                    throw new InvalidDataException($"Empty query '{query.Id}' cannot declare relevant chunks.");
                }

                continue;
            }

            if (query.Relevant.Count == 0)
            {
                throw new InvalidDataException($"Scored query '{query.Id}' must declare at least one relevant chunk.");
            }

            foreach (var relevant in query.Relevant)
            {
                if (!knownChunks.Contains(relevant))
                {
                    throw new InvalidDataException(
                        $"Query '{query.Id}' references missing chunk {relevant.DocumentId}/{relevant.ChunkIndex}.");
                }
            }
        }
    }
}

public sealed record RetrievalEvaluationDocument(
    Guid DocumentId,
    string FileName,
    IReadOnlyList<RetrievalEvaluationChunk> Chunks)
{
    public void Validate()
    {
        if (DocumentId == Guid.Empty)
        {
            throw new InvalidDataException("Evaluation document id is required.");
        }

        if (string.IsNullOrWhiteSpace(FileName))
        {
            throw new InvalidDataException($"Evaluation document {DocumentId} requires a file name.");
        }

        if (Chunks is null || Chunks.Count == 0)
        {
            throw new InvalidDataException($"Evaluation document {DocumentId} requires at least one chunk.");
        }

        var chunkIndexes = new HashSet<int>();

        foreach (var chunk in Chunks)
        {
            chunk.Validate();

            if (!chunkIndexes.Add(chunk.ChunkIndex))
            {
                throw new InvalidDataException(
                    $"Evaluation document {DocumentId} has duplicate chunk index {chunk.ChunkIndex}.");
            }
        }
    }
}

public sealed record RetrievalEvaluationChunk(int ChunkIndex, string Text)
{
    public void Validate()
    {
        if (ChunkIndex < 0)
        {
            throw new InvalidDataException("Evaluation chunk index cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(Text))
        {
            throw new InvalidDataException($"Evaluation chunk {ChunkIndex} requires text.");
        }
    }
}

public sealed record RetrievalEvaluationQuery(
    string Id,
    string Category,
    string Query,
    IReadOnlyList<RetrievalRelevantChunk> Relevant)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new InvalidDataException("Evaluation query id is required.");
        }

        if (string.IsNullOrWhiteSpace(Category))
        {
            throw new InvalidDataException($"Evaluation query '{Id}' requires a category.");
        }

        if (Relevant is null)
        {
            throw new InvalidDataException($"Evaluation query '{Id}' requires a relevant-chunk array.");
        }

        if (Relevant.Count != Relevant.Distinct().Count())
        {
            throw new InvalidDataException($"Evaluation query '{Id}' contains duplicate relevant chunks.");
        }
    }
}

public sealed record RetrievalRelevantChunk(Guid DocumentId, int ChunkIndex);

public sealed record RetrievalEvaluationBaseline(
    string DatasetVersion,
    int TopK,
    RetrievalBaselineObserved Observed,
    RetrievalEvaluationThresholds Thresholds)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DatasetVersion))
        {
            throw new InvalidDataException("Baseline dataset version is required.");
        }

        if (TopK <= 0)
        {
            throw new InvalidDataException("Baseline TopK must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(Observed);
        ArgumentNullException.ThrowIfNull(Thresholds);
        Observed.Validate();
        Thresholds.Validate();
    }
}

public sealed record RetrievalBaselineObserved(
    double PrecisionAtK,
    double RecallAtK,
    double MeanReciprocalRank,
    double EmptyQueryAccuracy)
{
    public void Validate()
    {
        ValidateUnitInterval(PrecisionAtK, nameof(PrecisionAtK));
        ValidateUnitInterval(RecallAtK, nameof(RecallAtK));
        ValidateUnitInterval(MeanReciprocalRank, nameof(MeanReciprocalRank));
        ValidateUnitInterval(EmptyQueryAccuracy, nameof(EmptyQueryAccuracy));
    }

    private static void ValidateUnitInterval(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new InvalidDataException($"Baseline {name} must be between zero and one.");
        }
    }
}

public sealed record RetrievalEvaluationThresholds(
    double MinimumPrecisionAtK,
    double MinimumRecallAtK,
    double MinimumMeanReciprocalRank,
    double MinimumEmptyQueryAccuracy,
    double MaximumMeanLatencyMs)
{
    public void Validate()
    {
        ValidateUnitInterval(MinimumPrecisionAtK, nameof(MinimumPrecisionAtK));
        ValidateUnitInterval(MinimumRecallAtK, nameof(MinimumRecallAtK));
        ValidateUnitInterval(MinimumMeanReciprocalRank, nameof(MinimumMeanReciprocalRank));
        ValidateUnitInterval(MinimumEmptyQueryAccuracy, nameof(MinimumEmptyQueryAccuracy));

        if (!double.IsFinite(MaximumMeanLatencyMs) || MaximumMeanLatencyMs <= 0)
        {
            throw new InvalidDataException("MaximumMeanLatencyMs must be a positive finite value.");
        }
    }

    private static void ValidateUnitInterval(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new InvalidDataException($"Threshold {name} must be between zero and one.");
        }
    }
}

public sealed class RetrievalEvaluator
{
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly ISemanticIndexStore _semanticIndexStore;

    public RetrievalEvaluator(
        IEmbeddingGenerator embeddingGenerator,
        ISemanticIndexStore semanticIndexStore)
    {
        _embeddingGenerator = embeddingGenerator;
        _semanticIndexStore = semanticIndexStore;
    }

    public async Task<RetrievalEvaluationReport> EvaluateAsync(
        RetrievalEvaluationDataset dataset,
        RetrievalEvaluationBaseline baseline,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(baseline);
        dataset.Validate();
        baseline.Validate();

        if (!string.Equals(dataset.Version, baseline.DatasetVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Dataset version '{dataset.Version}' does not match baseline '{baseline.DatasetVersion}'.");
        }

        if (dataset.TopK != baseline.TopK)
        {
            throw new InvalidDataException(
                $"Dataset TopK '{dataset.TopK}' does not match baseline TopK '{baseline.TopK}'.");
        }

        var inputs = dataset.Documents
            .SelectMany(document => document.Chunks.Select(chunk => new EmbeddingInput(
                document.DocumentId,
                document.FileName,
                chunk.ChunkIndex,
                chunk.Text)))
            .ToArray();
        var embeddings = await _embeddingGenerator.GenerateAsync(
            new EmbeddingRequest(inputs),
            cancellationToken);
        var records = embeddings.Vectors
            .Select(vector => new SemanticIndexRecord(
                vector.DocumentId,
                vector.FileName,
                vector.ChunkIndex,
                vector.Text,
                vector.Values,
                dataset.OwnerId,
                dataset.TenantId))
            .ToArray();

        await _semanticIndexStore.UpsertAsync(records, cancellationToken);

        var queryResults = new List<RetrievalQueryResult>(dataset.Queries.Count);

        foreach (var query in dataset.Queries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(query.Query))
            {
                queryResults.Add(new RetrievalQueryResult(
                    query.Id,
                    query.Category,
                    query.Query,
                    IsEmpty: true,
                    Passed: query.Relevant.Count == 0,
                    query.Relevant,
                    Results: [],
                    Metrics: null));
                continue;
            }

            var stopwatch = Stopwatch.StartNew();
            var queryEmbeddingResponse = await _embeddingGenerator.GenerateAsync(
                new EmbeddingRequest(
                    [new EmbeddingInput(Guid.NewGuid(), "evaluation-query", 0, query.Query)]),
                cancellationToken);
            var searchResults = await _semanticIndexStore.SearchAsync(
                new SemanticSearchRequest(
                    queryEmbeddingResponse.Vectors[0].Values,
                    dataset.TopK,
                    OwnerId: dataset.OwnerId,
                    TenantId: dataset.TenantId),
                cancellationToken);
            stopwatch.Stop();

            var relevant = query.Relevant.ToHashSet();
            var rankedResults = searchResults
                .Select((result, index) => new RetrievalRankedMatch(
                    Rank: index + 1,
                    result.Record.DocumentId,
                    result.Record.FileName,
                    result.Record.ChunkIndex,
                    result.Score,
                    IsRelevant: relevant.Contains(new RetrievalRelevantChunk(
                        result.Record.DocumentId,
                        result.Record.ChunkIndex))))
                .ToArray();
            var metrics = RetrievalMetricCalculator.Calculate(
                query.Relevant,
                rankedResults,
                dataset.TopK,
                stopwatch.Elapsed.TotalMilliseconds);

            queryResults.Add(new RetrievalQueryResult(
                query.Id,
                query.Category,
                query.Query,
                IsEmpty: false,
                Passed: metrics.RecallAtK > 0,
                query.Relevant,
                rankedResults,
                metrics));
        }

        var scoredResults = queryResults
            .Where(result => result.Metrics is not null)
            .Select(result => result.Metrics!)
            .ToArray();
        var emptyResults = queryResults.Where(result => result.IsEmpty).ToArray();
        var aggregate = RetrievalAggregateMetrics.Create(scoredResults, emptyResults);
        var comparison = RetrievalBaselineComparer.Compare(aggregate, baseline);

        return new RetrievalEvaluationReport(
            dataset.Version,
            embeddings.Model,
            dataset.TopK,
            dataset.Queries.Count,
            scoredResults.Length,
            emptyResults.Length,
            aggregate,
            comparison,
            queryResults);
    }
}

public static class RetrievalMetricCalculator
{
    public static RetrievalQueryMetrics Calculate(
        IReadOnlyCollection<RetrievalRelevantChunk> relevant,
        IReadOnlyList<RetrievalRankedMatch> rankedResults,
        int topK,
        double durationMs)
    {
        ArgumentNullException.ThrowIfNull(relevant);
        ArgumentNullException.ThrowIfNull(rankedResults);

        if (topK <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(topK));
        }

        if (relevant.Count == 0)
        {
            throw new ArgumentException("At least one relevant chunk is required for scored queries.", nameof(relevant));
        }

        var relevantLookup = relevant.ToHashSet();
        var retrievedRelevant = rankedResults.Count(result =>
            relevantLookup.Contains(new RetrievalRelevantChunk(result.DocumentId, result.ChunkIndex)));
        var firstRelevantRank = rankedResults
            .Where(result => relevantLookup.Contains(new RetrievalRelevantChunk(result.DocumentId, result.ChunkIndex)))
            .Select(result => result.Rank)
            .DefaultIfEmpty(0)
            .First();

        return new RetrievalQueryMetrics(
            PrecisionAtK: retrievedRelevant / (double)topK,
            RecallAtK: retrievedRelevant / (double)relevant.Count,
            ReciprocalRank: firstRelevantRank == 0 ? 0 : 1d / firstRelevantRank,
            DurationMs: durationMs);
    }
}

public static class RetrievalBaselineComparer
{
    private const double Epsilon = 0.000000001;

    public static RetrievalBaselineComparison Compare(
        RetrievalAggregateMetrics metrics,
        RetrievalEvaluationBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(baseline);
        var failures = new List<string>();
        var thresholds = baseline.Thresholds;

        AddMinimumFailure(
            failures,
            "precisionAtK",
            metrics.PrecisionAtK,
            thresholds.MinimumPrecisionAtK);
        AddMinimumFailure(
            failures,
            "recallAtK",
            metrics.RecallAtK,
            thresholds.MinimumRecallAtK);
        AddMinimumFailure(
            failures,
            "meanReciprocalRank",
            metrics.MeanReciprocalRank,
            thresholds.MinimumMeanReciprocalRank);
        AddMinimumFailure(
            failures,
            "emptyQueryAccuracy",
            metrics.EmptyQueryAccuracy,
            thresholds.MinimumEmptyQueryAccuracy);

        if (metrics.MeanLatencyMs - Epsilon > thresholds.MaximumMeanLatencyMs)
        {
            failures.Add(
                $"meanLatencyMs {metrics.MeanLatencyMs:F6} exceeds maximum {thresholds.MaximumMeanLatencyMs:F6}.");
        }

        return new RetrievalBaselineComparison(
            Passed: failures.Count == 0,
            baseline.Observed,
            thresholds,
            failures);
    }

    private static void AddMinimumFailure(
        ICollection<string> failures,
        string metric,
        double actual,
        double minimum)
    {
        if (actual + Epsilon < minimum)
        {
            failures.Add($"{metric} {actual:F6} is below minimum {minimum:F6}.");
        }
    }
}

public sealed record RetrievalEvaluationReport(
    string DatasetVersion,
    string EmbeddingModel,
    int TopK,
    int QueryCount,
    int EvaluatedQueryCount,
    int EmptyQueryCount,
    RetrievalAggregateMetrics Metrics,
    RetrievalBaselineComparison Baseline,
    IReadOnlyList<RetrievalQueryResult> Queries);

public sealed record RetrievalAggregateMetrics(
    double PrecisionAtK,
    double RecallAtK,
    double MeanReciprocalRank,
    double EmptyQueryAccuracy,
    double MeanLatencyMs,
    double P95LatencyMs)
{
    public static RetrievalAggregateMetrics Create(
        IReadOnlyList<RetrievalQueryMetrics> scoredResults,
        IReadOnlyList<RetrievalQueryResult> emptyResults)
    {
        var precision = scoredResults.Count == 0
            ? 0
            : scoredResults.Average(result => result.PrecisionAtK);
        var recall = scoredResults.Count == 0
            ? 0
            : scoredResults.Average(result => result.RecallAtK);
        var meanReciprocalRank = scoredResults.Count == 0
            ? 0
            : scoredResults.Average(result => result.ReciprocalRank);
        var emptyQueryAccuracy = emptyResults.Count == 0
            ? 1
            : emptyResults.Count(result => result.Passed) / (double)emptyResults.Count;
        var meanLatency = scoredResults.Count == 0
            ? 0
            : scoredResults.Average(result => result.DurationMs);
        var p95Latency = CalculateP95(scoredResults.Select(result => result.DurationMs).ToArray());

        return new RetrievalAggregateMetrics(
            precision,
            recall,
            meanReciprocalRank,
            emptyQueryAccuracy,
            meanLatency,
            p95Latency);
    }

    private static double CalculateP95(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var ordered = values.OrderBy(value => value).ToArray();
        var index = Math.Max(0, (int)Math.Ceiling(ordered.Length * 0.95) - 1);
        return ordered[index];
    }
}

public sealed record RetrievalBaselineComparison(
    bool Passed,
    RetrievalBaselineObserved Observed,
    RetrievalEvaluationThresholds Thresholds,
    IReadOnlyList<string> Failures);

public sealed record RetrievalQueryResult(
    string Id,
    string Category,
    string Query,
    bool IsEmpty,
    bool Passed,
    IReadOnlyList<RetrievalRelevantChunk> ExpectedRelevant,
    IReadOnlyList<RetrievalRankedMatch> Results,
    RetrievalQueryMetrics? Metrics);

public sealed record RetrievalRankedMatch(
    int Rank,
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    float Score,
    bool IsRelevant);

public sealed record RetrievalQueryMetrics(
    double PrecisionAtK,
    double RecallAtK,
    double ReciprocalRank,
    double DurationMs);
