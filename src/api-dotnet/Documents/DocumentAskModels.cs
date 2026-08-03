namespace EnterpriseDocumentAssistant.Api.Documents;

public sealed record DocumentAskRequest(
    string Question,
    int? TopK = null);

public sealed record DocumentAskResponse(
    string Question,
    string Answer,
    int SourceCount,
    IReadOnlyCollection<DocumentAskSource> Sources,
    string AnswerStatus = GroundedAnswerStatuses.Answered,
    string AnswerProvider = "deterministic",
    string? AnswerModel = null,
    bool IsGrounded = true,
    string? ReasonCode = null);

public sealed record DocumentAskFailureResponse(
    string Question,
    string Message,
    string Code,
    bool Retryable,
    int SourceCount,
    IReadOnlyCollection<DocumentAskSource> Sources);

public sealed record DocumentAskSource(
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    double Score,
    string Text);
