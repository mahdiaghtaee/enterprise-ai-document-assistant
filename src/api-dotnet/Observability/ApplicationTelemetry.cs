using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EnterpriseDocumentAssistant.Api.Observability;

public static class ApplicationTelemetry
{
    public const string ActivitySourceName = "EnterpriseDocumentAssistant.Api";
    public const string MeterName = "EnterpriseDocumentAssistant.Api";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> AuthorizationDenied = Meter.CreateCounter<long>(
        "document_assistant.authorization.denied",
        unit: "{request}",
        description: "Number of rejected authorization decisions.");

    public static readonly Counter<long> UploadsQueued = Meter.CreateCounter<long>(
        "document_assistant.uploads.queued",
        unit: "{document}",
        description: "Number of uploads durably queued for background ingestion.");

    public static readonly Counter<long> SearchRequests = Meter.CreateCounter<long>(
        "document_assistant.search.requests",
        unit: "{request}",
        description: "Number of semantic-search requests.");

    public static readonly Histogram<double> SearchDuration = Meter.CreateHistogram<double>(
        "document_assistant.search.duration",
        unit: "ms",
        description: "Semantic-search duration in milliseconds.");

    public static readonly Histogram<long> SearchResultCount = Meter.CreateHistogram<long>(
        "document_assistant.search.results",
        unit: "{result}",
        description: "Number of visible results returned by semantic search.");

    public static readonly Counter<long> AskRequests = Meter.CreateCounter<long>(
        "document_assistant.ask.requests",
        unit: "{request}",
        description: "Number of grounded-answer requests.");

    public static readonly Histogram<double> AskDuration = Meter.CreateHistogram<double>(
        "document_assistant.ask.duration",
        unit: "ms",
        description: "Grounded-answer retrieval and generation duration in milliseconds.");

    public static readonly Counter<long> AnswerGenerationResults = Meter.CreateCounter<long>(
        "document_assistant.answer_generation.results",
        unit: "{result}",
        description: "Number of answered or insufficient-evidence generation results.");

    public static readonly Counter<long> AnswerGenerationFailures = Meter.CreateCounter<long>(
        "document_assistant.answer_generation.failures",
        unit: "{failure}",
        description: "Number of controlled answer-provider failures.");

    public static readonly Histogram<double> AnswerGenerationDuration = Meter.CreateHistogram<double>(
        "document_assistant.answer_generation.duration",
        unit: "ms",
        description: "Answer generation duration after retrieval in milliseconds.");

    public static readonly Histogram<long> AnswerInputTokens = Meter.CreateHistogram<long>(
        "document_assistant.answer_generation.input_tokens",
        unit: "{token}",
        description: "Provider-reported answer-generation input tokens when available.");

    public static readonly Histogram<long> AnswerOutputTokens = Meter.CreateHistogram<long>(
        "document_assistant.answer_generation.output_tokens",
        unit: "{token}",
        description: "Provider-reported answer-generation output tokens when available.");

    public static readonly Counter<long> IngestionCompleted = Meter.CreateCounter<long>(
        "document_assistant.ingestion.completed",
        unit: "{job}",
        description: "Number of completed ingestion jobs.");

    public static readonly Counter<long> IngestionRetried = Meter.CreateCounter<long>(
        "document_assistant.ingestion.retried",
        unit: "{job}",
        description: "Number of ingestion jobs returned to the pending queue.");

    public static readonly Counter<long> IngestionFailed = Meter.CreateCounter<long>(
        "document_assistant.ingestion.failed",
        unit: "{job}",
        description: "Number of terminal ingestion failures.");

    public static readonly Counter<long> IngestionRecovered = Meter.CreateCounter<long>(
        "document_assistant.ingestion.recovered",
        unit: "{job}",
        description: "Number of abandoned ingestion jobs recovered by the worker.");

    public static readonly Histogram<double> IngestionDuration = Meter.CreateHistogram<double>(
        "document_assistant.ingestion.duration",
        unit: "ms",
        description: "Background ingestion processing duration in milliseconds.");

    public static readonly Counter<long> AuditEventsPersisted = Meter.CreateCounter<long>(
        "document_assistant.audit.persisted",
        unit: "{event}",
        description: "Number of audit events persisted by application code.");

    public static readonly Counter<long> AuditPersistenceFailures = Meter.CreateCounter<long>(
        "document_assistant.audit.persistence_failures",
        unit: "{event}",
        description: "Number of application audit persistence failures.");

    public static KeyValuePair<string, object?> Tag(string name, object? value) => new(name, value);
}
