using EnterpriseDocumentAssistant.Api.Security;

namespace EnterpriseDocumentAssistant.Api.Documents;

public interface IDocumentRepository
{
    IReadOnlyCollection<DocumentRecord> GetAll(
        string? tenantId = null,
        string? ownerId = null,
        bool bypassTenantIsolation = false);

    DocumentRecord? GetById(
        Guid documentId,
        string? tenantId = null,
        string? ownerId = null,
        bool bypassTenantIsolation = false);

    DocumentRecord Add(
        string fileName,
        string? contentType,
        long sizeInBytes,
        string storagePath,
        string tenantId = TenantIsolation.LegacyTenantId,
        string ownerId = DocumentOwnership.LegacyOwnerId);

    void UpdateStatus(Guid documentId, string status);
}
