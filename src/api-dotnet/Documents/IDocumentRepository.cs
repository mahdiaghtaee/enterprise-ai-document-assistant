namespace EnterpriseDocumentAssistant.Api.Documents;

public interface IDocumentRepository
{
    IReadOnlyCollection<DocumentRecord> GetAll();

    DocumentRecord? GetById(Guid documentId);

    DocumentRecord Add(string fileName, string? contentType, long sizeInBytes, string storagePath);

    void UpdateStatus(Guid documentId, string status);
}
