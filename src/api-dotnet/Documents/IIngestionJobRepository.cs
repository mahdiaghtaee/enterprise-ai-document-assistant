using EnterpriseDocumentAssistant.Api.Security;

namespace EnterpriseDocumentAssistant.Api.Documents;

public interface IIngestionJobRepository
{
    Task<DocumentIngestionCreationResult> CreateDocumentWithPendingJobAsync(
        CreateDocumentIngestionRequest request,
        CancellationToken cancellationToken);

    Task<DocumentIngestionJob> CreatePendingAsync(
        Guid documentId,
        CancellationToken cancellationToken);

    Task<DocumentIngestionJob?> GetLatestForDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken);

    Task<DocumentIngestionJob?> ClaimNextAvailableAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<DocumentIngestionJob> MarkCompletedAsync(
        long jobId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    Task<DocumentIngestionJob> MarkFailedOrRetryAsync(
        long jobId,
        string errorCode,
        string errorSummary,
        bool retryable,
        DateTimeOffset retryAt,
        CancellationToken cancellationToken);

    Task ReturnToPendingAsync(
        long jobId,
        DateTimeOffset availableAt,
        CancellationToken cancellationToken);

    Task<int> RecoverAbandonedAsync(
        DateTimeOffset staleBefore,
        DateTimeOffset retryAt,
        CancellationToken cancellationToken);
}

public sealed record CreateDocumentIngestionRequest(
    string FileName,
    string? ContentType,
    long SizeInBytes,
    string StoragePath,
    int MaxAttempts = DocumentIngestionDefaults.MaxAttempts,
    string TenantId = TenantIsolation.LegacyTenantId,
    string OwnerId = DocumentOwnership.LegacyOwnerId);

public sealed record DocumentIngestionCreationResult(
    DocumentRecord Document,
    DocumentIngestionJob Job);

public sealed class ActiveDocumentIngestionJobException : InvalidOperationException
{
    public ActiveDocumentIngestionJobException(Guid documentId, Exception innerException)
        : base($"Document '{documentId}' already has an active ingestion job.", innerException)
    {
        DocumentId = documentId;
    }

    public Guid DocumentId { get; }
}
