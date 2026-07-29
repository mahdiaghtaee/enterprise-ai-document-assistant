using EnterpriseDocumentAssistant.Api.Observability;

namespace EnterpriseDocumentAssistant.Api.Audit;

public static class AuditEventRecorder
{
    public static async Task TryAppendAsync(
        IAuditEventRepository repository,
        AuditEventWrite auditEvent,
        bool bypassTenantIsolation,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.AppendAsync(auditEvent, bypassTenantIsolation, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ApplicationTelemetry.AuditPersistenceFailures.Add(
                1,
                ApplicationTelemetry.Tag("audit.event_type", auditEvent.EventType));
            logger.LogCritical(
                exception,
                "Failed to persist application audit event {AuditEventType} for {ResourceType} {ResourceId}.",
                auditEvent.EventType,
                auditEvent.ResourceType,
                auditEvent.ResourceId);
        }
    }
}
