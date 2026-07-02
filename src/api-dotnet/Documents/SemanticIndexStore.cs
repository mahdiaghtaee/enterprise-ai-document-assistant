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
