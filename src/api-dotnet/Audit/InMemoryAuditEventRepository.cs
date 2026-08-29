namespace EnterpriseDocumentAssistant.Api.Audit;

public sealed class InMemoryAuditEventRepository : IAuditEventRepository
{
    private readonly List<AuditEventRecord> _events = [];
    private readonly object _lock = new();
    private long _nextId;

    public Task<AuditEventRecord> AppendAsync(
        AuditEventWrite auditEvent,
        bool bypassTenantIsolation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        cancellationToken.ThrowIfCancellationRequested();

        var record = new AuditEventRecord(
            Interlocked.Increment(ref _nextId),
            DateTimeOffset.UtcNow,
            auditEvent.TenantId,
            auditEvent.ActorUserId,
            auditEvent.ActorRole,
            auditEvent.EventType,
            auditEvent.Action,
            auditEvent.ResourceType,
            auditEvent.ResourceId,
            auditEvent.Outcome,
            auditEvent.CorrelationId,
            auditEvent.TraceId,
            auditEvent.Details ?? new Dictionary<string, object?>());

        lock (_lock)
        {
            _events.Add(record);
        }

        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<AuditEventRecord>> GetRecentAsync(
        AuditEventQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            IReadOnlyList<AuditEventRecord> result = _events
                .Where(item => query.BypassTenantIsolation || item.TenantId == query.TenantId)
                .OrderByDescending(item => item.OccurredAt)
                .ThenByDescending(item => item.Id)
                .Take(query.Limit)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<AuditIntegrityResult> VerifyIntegrityAsync(
        AuditIntegrityQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var tenantId = query.ValidateAndNormalize();

        lock (_lock)
        {
            var count = _events.LongCount(item => item.TenantId == tenantId);
            return Task.FromResult(new AuditIntegrityResult(
                tenantId,
                IsValid: true,
                CheckedCount: count,
                FirstBrokenSequence: null,
                HeadSequence: count));
        }
    }
}
