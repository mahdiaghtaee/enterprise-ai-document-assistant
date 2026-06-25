namespace EnterpriseDocumentAssistant.Api.Documents;

public interface IDocumentStorage
{
    Task<StoredDocument> SaveAsync(IFormFile file, CancellationToken cancellationToken);
}

public sealed record StoredDocument(
    string OriginalFileName,
    string ContentType,
    long SizeInBytes,
    string StoragePath);
