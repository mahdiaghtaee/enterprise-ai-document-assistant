using EnterpriseDocumentAssistant.Api.Security;

namespace EnterpriseDocumentAssistant.Api.Documents;

public sealed class InMemoryDocumentRepository : IDocumentRepository
{
    private readonly List<DocumentRecord> _documents = [];
    private readonly object _lock = new();

    public IReadOnlyCollection<DocumentRecord> GetAll(
        string? ownerId = null,
        string? tenantId = null,
        bool bypassTenantIsolation = false)
    {
        lock (_lock)
        {
            return _documents
                .Where(document => bypassTenantIsolation || tenantId is null || document.TenantId == tenantId)
                .Where(document => ownerId is null || document.OwnerId == ownerId)
                .ToArray();
        }
    }

    public DocumentRecord? GetById(
        Guid documentId,
        string? ownerId = null,
        string? tenantId = null,
        bool bypassTenantIsolation = false)
    {
        lock (_lock)
        {
            return _documents.FirstOrDefault(document =>
                document.Id == documentId &&
                (bypassTenantIsolation || tenantId is null || document.TenantId == tenantId) &&
                (ownerId is null || document.OwnerId == ownerId));
        }
    }

    public DocumentRecord Add(
        string fileName,
        string? contentType,
        long sizeInBytes,
        string storagePath,
        string ownerId = DocumentOwnership.LegacyOwnerId,
        string tenantId = TenantIsolation.LegacyTenantId)
    {
        var document = new DocumentRecord(
            Guid.NewGuid(),
            fileName.Trim(),
            string.IsNullOrWhiteSpace(contentType) ? null : contentType.Trim(),
            sizeInBytes,
            storagePath,
            "uploaded",
            DateTimeOffset.UtcNow,
            DocumentOwnership.Normalize(ownerId),
            TenantIsolation.Normalize(tenantId));

        lock (_lock)
        {
            _documents.Add(document);
        }

        return document;
    }

    public void UpdateStatus(Guid documentId, string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            throw new ArgumentException("Document status is required.", nameof(status));
        }

        lock (_lock)
        {
            var index = _documents.FindIndex(document => document.Id == documentId);
            if (index < 0)
            {
                return;
            }

            _documents[index] = _documents[index] with { Status = status.Trim() };
        }
    }
}
