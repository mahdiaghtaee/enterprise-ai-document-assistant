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
    IReadOnlyList<float> Embedding);

public sealed record SemanticSearchRequest(
    IReadOnlyList<float> QueryEmbedding,
    int TopK = 5);

public sealed record SemanticSearchResult(
    SemanticIndexRecord Record,
    float Score);
