using EnterpriseDocumentAssistant.Api.Security;

namespace EnterpriseDocumentAssistant.Api.Documents;

public static class DocumentOwnership
{
    public const string LegacyOwnerId = "legacy-system";

    public static string Normalize(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new ArgumentException("Document owner id is required.", nameof(ownerId));
        }

        return ownerId.Trim();
    }
}

public sealed record DocumentRecord(
    Guid Id,
    string FileName,
    string? ContentType,
    long SizeInBytes,
    string StoragePath,
    string Status,
    DateTimeOffset CreatedAt,
    string TenantId = TenantIsolation.LegacyTenantId,
    string OwnerId = DocumentOwnership.LegacyOwnerId);

public sealed record CreateDocumentRequest(
    string FileName,
    string? ContentType);

public sealed record UploadDocumentResponse(
    Guid Id,
    string FileName,
    string Status,
    string? IndexingStatus,
    DocumentTextExtractionSummary? TextExtraction,
    DocumentChunkingSummary? Chunking,
    EmbeddingSummary? Embeddings = null,
    long? IngestionJobId = null,
    string? ProcessingStatusUrl = null);

public sealed record DocumentProcessingStatusResponse(
    long JobId,
    Guid DocumentId,
    string Status,
    int AttemptCount,
    int MaxAttempts,
    DateTimeOffset AvailableAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? FailedAt,
    string? LastErrorCode,
    string? LastErrorSummary,
    DateTimeOffset UpdatedAt,
    bool IsTerminal)
{
    public static DocumentProcessingStatusResponse FromJob(DocumentIngestionJob job)
    {
        return new DocumentProcessingStatusResponse(
            job.Id,
            job.DocumentId,
            job.Status.ToString(),
            job.AttemptCount,
            job.MaxAttempts,
            job.AvailableAt,
            job.StartedAt,
            job.CompletedAt,
            job.FailedAt,
            job.LastErrorCode,
            job.LastErrorSummary,
            job.UpdatedAt,
            job.Status is DocumentIngestionStatus.Completed or DocumentIngestionStatus.Failed);
    }
}

public sealed record EmbeddingSummary(
    string Model,
    int VectorCount,
    int Dimensions);
