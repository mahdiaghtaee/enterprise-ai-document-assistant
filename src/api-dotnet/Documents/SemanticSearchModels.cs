namespace EnterpriseDocumentAssistant.Api.Documents;

public sealed record DocumentSearchRequest(
    string Query,
    int TopK = 5);

public sealed record DocumentSearchResponse(
    string Query,
    int ResultCount,
    IReadOnlyList<DocumentSearchMatch> Results);

public sealed record DocumentSearchMatch(
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    string Text,
    float Score);
