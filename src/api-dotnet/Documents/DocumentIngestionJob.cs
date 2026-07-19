namespace EnterpriseDocumentAssistant.Api.Documents;

public enum DocumentIngestionStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}

public static class DocumentIngestionDefaults
{
    public const int MaxAttempts = 3;
}

public sealed record DocumentIngestionJob(
    long Id,
    Guid DocumentId,
    DocumentIngestionStatus Status,
    int AttemptCount,
    int MaxAttempts,
    DateTimeOffset AvailableAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? FailedAt,
    string? LastErrorCode,
    string? LastErrorSummary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
