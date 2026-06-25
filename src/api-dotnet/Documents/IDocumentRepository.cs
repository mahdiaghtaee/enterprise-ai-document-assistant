namespace EnterpriseDocumentAssistant.Api.Documents;

public interface IDocumentRepository
{
    IReadOnlyCollection<DocumentRecord> GetAll();

    DocumentRecord Add(string fileName, string? contentType);
}
