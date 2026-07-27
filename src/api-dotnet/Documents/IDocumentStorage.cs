namespace EnterpriseDocumentAssistant.Api.Documents;

public interface IDocumentStorage
{
    Task<StoredDocument> SaveAsync(IFormFile file, CancellationToken cancellationToken);

    Task DeleteAsync(StoredDocument document, CancellationToken cancellationToken);
}

public sealed record StoredDocument(
    string OriginalFileName,
    string ContentType,
    long SizeInBytes,
    string StoragePath);
