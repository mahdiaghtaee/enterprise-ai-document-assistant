namespace EnterpriseDocumentAssistant.Api.Documents;

public sealed record DocumentRecord(
    Guid Id,
    string FileName,
    string? ContentType,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record CreateDocumentRequest(
    string FileName,
    string? ContentType);
