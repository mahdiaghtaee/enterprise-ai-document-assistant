namespace EnterpriseDocumentAssistant.Api.Audit;

public interface IAuditEventRepository
{
    Task<AuditEventRecord> AppendAsync(
        AuditEventWrite auditEvent,
        bool bypassTenantIsolation,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditEventRecord>> GetRecentAsync(
        AuditEventQuery query,
        CancellationToken cancellationToken);

    Task<AuditIntegrityResult> VerifyIntegrityAsync(
        AuditIntegrityQuery query,
        CancellationToken cancellationToken);
}
