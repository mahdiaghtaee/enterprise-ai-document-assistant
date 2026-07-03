namespace EnterpriseDocumentAssistant.Api.Documents;

public interface ISemanticIndexStore
{
    Task UpsertAsync(IReadOnlyList<SemanticIndexRecord> records, CancellationToken cancellationToken);

    Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(SemanticSearchRequest request, CancellationToken cancellationToken);
}

public sealed record SemanticIndexRecord(
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    string Text,
    IReadOnlyList<float> Embedding)
{
    public int Dimensions => Embedding.Count;

    public void Validate()
    {
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
            throw new ArgumentOutOfRangeException(nameof(ChunkIndex), "Chunk index cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(Text))
        {
            throw new ArgumentException("Text is required.", nameof(Text));
        }

        if (Embedding.Count == 0)
        {
            throw new ArgumentException("Embedding values are required.", nameof(Embedding));
        }
    }
}

public sealed record SemanticSearchRequest(
    IReadOnlyList<float> QueryEmbedding,
    int TopK = 5)
{
    public void Validate()
    {
        if (QueryEmbedding.Count == 0)
        {
            throw new ArgumentException("Query embedding is required.", nameof(QueryEmbedding));
        }

        if (TopK <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(TopK), "TopK must be greater than zero.");
        }
    }
}

public sealed record SemanticSearchResult(
    SemanticIndexRecord Record,
    float Score);

public sealed class InMemorySemanticIndexStore : ISemanticIndexStore
{
    private readonly List<SemanticIndexRecord> _records = new();

    public Task UpsertAsync(IReadOnlyList<SemanticIndexRecord> records, CancellationToken cancellationToken)
    {
        foreach (var record in records)
        {
            record.Validate();
            _records.RemoveAll(existing => existing.DocumentId == record.DocumentId && existing.ChunkIndex == record.ChunkIndex);
            _records.Add(record);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(SemanticSearchRequest request, CancellationToken cancellationToken)
    {
        request.Validate();

        IReadOnlyList<SemanticSearchResult> results = _records
            .Where(record => record.Dimensions == request.QueryEmbedding.Count)
            .Select(record => new SemanticSearchResult(record, CalculateScore(request.QueryEmbedding, record.Embedding)))
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Record.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.Record.ChunkIndex)
            .Take(request.TopK)
            .ToArray();

        return Task.FromResult(results);
    }

    private static float CalculateScore(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        var dot = 0f;
        var leftLength = 0f;
        var rightLength = 0f;

        for (var index = 0; index < left.Count; index++)
        {
            dot += left[index] * right[index];
            leftLength += left[index] * left[index];
            rightLength += right[index] * right[index];
        }

        if (leftLength == 0 || rightLength == 0)
        {
            return 0;
        }

        return dot / (MathF.Sqrt(leftLength) * MathF.Sqrt(rightLength));
    }
}
