using EnterpriseDocumentAssistant.Api.Documents;
using Xunit;

namespace EnterpriseDocumentAssistant.RetrievalEvaluation.Tests;

public sealed class RetrievalEvaluationTests
{
    [Fact]
    public void Metric_calculator_reports_precision_recall_and_reciprocal_rank()
    {
        var firstDocumentId = Guid.NewGuid();
        var secondDocumentId = Guid.NewGuid();
        var relevant = new[]
        {
            new RetrievalRelevantChunk(firstDocumentId, 0),
            new RetrievalRelevantChunk(secondDocumentId, 1)
        };
        var ranked = new[]
        {
            new RetrievalRankedMatch(1, firstDocumentId, "first.txt", 0, 1f, true),
            new RetrievalRankedMatch(2, Guid.NewGuid(), "other.txt", 0, 0.9f, false),
            new RetrievalRankedMatch(3, secondDocumentId, "second.txt", 1, 0.8f, true)
        };

        var metrics = RetrievalMetricCalculator.Calculate(relevant, ranked, topK: 3, durationMs: 2.5);

        Assert.Equal(2d / 3d, metrics.PrecisionAtK, precision: 10);
        Assert.Equal(1d, metrics.RecallAtK, precision: 10);
        Assert.Equal(1d, metrics.ReciprocalRank, precision: 10);
        Assert.Equal(2.5d, metrics.DurationMs, precision: 10);
    }

    [Fact]
    public void Baseline_comparer_reports_quality_regressions()
    {
        var metrics = new RetrievalAggregateMetrics(
            PrecisionAtK: 0.2,
            RecallAtK: 0.5,
            MeanReciprocalRank: 0.6,
            EmptyQueryAccuracy: 1,
            MeanLatencyMs: 10,
            P95LatencyMs: 12);
        var baseline = CreateBaseline(
            minimumPrecision: 0.3,
            minimumRecall: 0.7,
            minimumMrr: 0.8,
            minimumEmptyAccuracy: 1,
            maximumMeanLatency: 100);

        var comparison = RetrievalBaselineComparer.Compare(metrics, baseline);

        Assert.False(comparison.Passed);
        Assert.Contains(comparison.Failures, failure => failure.StartsWith("precisionAtK", StringComparison.Ordinal));
        Assert.Contains(comparison.Failures, failure => failure.StartsWith("recallAtK", StringComparison.Ordinal));
        Assert.Contains(comparison.Failures, failure => failure.StartsWith("meanReciprocalRank", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Evaluator_uses_real_deterministic_embedding_and_handles_empty_queries()
    {
        var documentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var dataset = new RetrievalEvaluationDataset(
            Version: "test-v1",
            TopK: 1,
            TenantId: "test-tenant",
            OwnerId: "test-owner",
            Documents:
            [
                new RetrievalEvaluationDocument(
                    documentId,
                    "exact.txt",
                    [new RetrievalEvaluationChunk(0, "exact retrieval query")])
            ],
            Queries:
            [
                new RetrievalEvaluationQuery(
                    "exact",
                    "exact",
                    "exact retrieval query",
                    [new RetrievalRelevantChunk(documentId, 0)]),
                new RetrievalEvaluationQuery(
                    "empty",
                    "empty",
                    string.Empty,
                    [])
            ]);
        var baseline = new RetrievalEvaluationBaseline(
            "test-v1",
            1,
            new RetrievalBaselineObserved(1, 1, 1, 1),
            new RetrievalEvaluationThresholds(1, 1, 1, 1, 1000));
        var evaluator = new RetrievalEvaluator(
            new DeterministicEmbeddingGenerator(),
            new InMemorySemanticIndexStore());

        var report = await evaluator.EvaluateAsync(dataset, baseline, CancellationToken.None);

        Assert.True(report.Baseline.Passed);
        Assert.Equal(1d, report.Metrics.PrecisionAtK, precision: 10);
        Assert.Equal(1d, report.Metrics.RecallAtK, precision: 10);
        Assert.Equal(1d, report.Metrics.MeanReciprocalRank, precision: 10);
        Assert.Equal(1d, report.Metrics.EmptyQueryAccuracy, precision: 10);
        Assert.True(Assert.Single(report.Queries, query => query.Id == "empty").Passed);
    }

    [Fact]
    public void Dataset_validation_rejects_relevant_chunks_outside_the_corpus()
    {
        var documentId = Guid.NewGuid();
        var dataset = new RetrievalEvaluationDataset(
            Version: "test-v1",
            TopK: 3,
            TenantId: "test-tenant",
            OwnerId: "test-owner",
            Documents:
            [
                new RetrievalEvaluationDocument(
                    documentId,
                    "document.txt",
                    [new RetrievalEvaluationChunk(0, "document text")])
            ],
            Queries:
            [
                new RetrievalEvaluationQuery(
                    "missing",
                    "exact",
                    "document text",
                    [new RetrievalRelevantChunk(Guid.NewGuid(), 0)])
            ]);

        var exception = Assert.Throws<InvalidDataException>(dataset.Validate);

        Assert.Contains("references missing chunk", exception.Message, StringComparison.Ordinal);
    }

    private static RetrievalEvaluationBaseline CreateBaseline(
        double minimumPrecision,
        double minimumRecall,
        double minimumMrr,
        double minimumEmptyAccuracy,
        double maximumMeanLatency)
    {
        return new RetrievalEvaluationBaseline(
            "test-v1",
            3,
            new RetrievalBaselineObserved(0.5, 0.5, 0.5, 1),
            new RetrievalEvaluationThresholds(
                minimumPrecision,
                minimumRecall,
                minimumMrr,
                minimumEmptyAccuracy,
                maximumMeanLatency));
    }
}
