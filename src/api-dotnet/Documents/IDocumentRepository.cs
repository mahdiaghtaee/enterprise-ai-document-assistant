namespace EnterpriseDocumentAssistant.Api.Documents;

public interface IDocumentRepository
{
    IReadOnlyCollection<DocumentRecord> GetAll(string? ownerId = null);

    DocumentRecord? GetById(Guid documentId, string? ownerId = null);

    DocumentRecord Add(
        string fileName,
        string? contentType,
        long sizeInBytes,
        string storagePath,
        string ownerId = DocumentOwnership.LegacyOwnerId);

    void UpdateStatus(Guid documentId, string status);
}
