namespace EnterpriseDocumentAssistant.Api.Documents;

public interface IIngestionJobRepository
{
    Task<DocumentIngestionCreationResult> CreateDocumentWithPendingJobAsync(
        CreateDocumentIngestionRequest request,
        CancellationToken cancellationToken);

    Task<DocumentIngestionJob> CreatePendingAsync(
        Guid documentId,
        CancellationToken cancellationToken);
}

public sealed record CreateDocumentIngestionRequest(
    string FileName,
    string? ContentType,
    long SizeInBytes,
    string StoragePath,
    int MaxAttempts = DocumentIngestionDefaults.MaxAttempts);

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
