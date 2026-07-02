namespace EnterpriseDocumentAssistant.Api.Documents;

public sealed record DocumentRecord(
    Guid Id,
    string FileName,
    string? ContentType,
    long SizeInBytes,
    string StoragePath,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record CreateDocumentRequest(
    string FileName,
    string? ContentType);

public sealed record UploadDocumentResponse(
    Guid Id,
    string FileName,
    string Status,
    string? IndexingStatus,
    DocumentTextExtractionSummary? TextExtraction,
    DocumentChunkingSummary? Chunking);
