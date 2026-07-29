using EnterpriseDocumentAssistant.Api.Security;

namespace EnterpriseDocumentAssistant.Api.Documents;

public interface IDocumentRepository
{
    IReadOnlyCollection<DocumentRecord> GetAll(
        string? ownerId = null,
        string? tenantId = null,
        bool bypassTenantIsolation = false);

    DocumentRecord? GetById(
        Guid documentId,
        string? ownerId = null,
        string? tenantId = null,
        bool bypassTenantIsolation = false);

    DocumentRecord Add(
        string fileName,
        string? contentType,
        long sizeInBytes,
        string storagePath,
        string ownerId = DocumentOwnership.LegacyOwnerId,
        string tenantId = TenantIsolation.LegacyTenantId);

    void UpdateStatus(Guid documentId, string status);
}
