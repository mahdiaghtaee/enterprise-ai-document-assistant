namespace EnterpriseDocumentAssistant.Api.Documents;

public sealed class InMemoryDocumentRepository : IDocumentRepository
{
    private readonly List<DocumentRecord> _documents = [];
    private readonly object _lock = new();

    public IReadOnlyCollection<DocumentRecord> GetAll()
    {
        lock (_lock)
        {
            return _documents.ToArray();
        }
    }

    public DocumentRecord Add(string fileName, string? contentType)
    {
        var document = new DocumentRecord(
            Guid.NewGuid(),
            fileName.Trim(),
            string.IsNullOrWhiteSpace(contentType) ? null : contentType.Trim(),
            "uploaded",
            DateTimeOffset.UtcNow);

        lock (_lock)
        {
            _documents.Add(document);
        }

        return document;
    }
}
