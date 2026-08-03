# Health, Audit, and Observability

This document describes the implemented operational-diagnostics foundation for the Enterprise AI Document Assistant.

## Operational goals

The system should answer four questions without inspecting private document content:

1. Is each process running?
2. Are required dependencies reachable?
3. Which request, trace, tenant, document, ingestion job, or provider operation failed?
4. Which authenticated actor performed a security-relevant document operation?

The implementation separates telemetry from the durable audit ledger. Telemetry is optimized for diagnosis and aggregation; audit events are append-only business/security records.

## Correlation and trace context

Every ASP.NET Core and FastAPI response includes:

```text
X-Correlation-ID: <validated identifier>
```

A client-supplied value is accepted only when it:

- is between 1 and 128 characters;
- contains only letters, digits, `.`, `_`, `:`, or `-`.

Missing or invalid values are replaced with a generated 32-character identifier. The validated identifier is propagated through response headers, audit events, OpenTelemetry activity tags, and outgoing service requests, including optional answer-provider calls. Standard W3C `traceparent` propagation is handled by OpenTelemetry HTTP instrumentation.

The ASP.NET Core log scope does not write externally supplied correlation text directly. It stores a deterministic 128-bit prefix of a SHA-256 digest as `CorrelationLogId`; the original validated identifier remains available in the response, audit ledger, and trace context. This prevents user-controlled log entries while preserving deterministic diagnostic linkage.

Correlation identifiers are diagnostic labels, not authentication credentials, and must not be trusted for authorization.

## Health endpoints

### ASP.NET Core API

| Endpoint | Purpose | Dependency checks |
|---|---|---|
| `GET /health` | Backward-compatible process health | None |
| `GET /health/live` | Liveness/probe that confirms the process can serve HTTP | None |
| `GET /health/ready` | Readiness for traffic | PostgreSQL and FastAPI health |

The readiness endpoint returns `503 Service Unavailable` when a required dependency is unhealthy. Its response includes dependency status and duration, correlation ID, and trace ID, but not credentials or connection strings.

The optional external answer provider is request-time functionality and is not called by readiness probes. Provider timeout and availability are reported through controlled Ask responses, traces, metrics, and audit events rather than causing readiness probes to send billable or data-bearing requests.

### FastAPI service

`GET /health` returns service status, UTC check time, correlation ID, and the active trace ID when one exists.

## OpenTelemetry signals

Both services can run without a telemetry collector. When `OTEL_EXPORTER_OTLP_ENDPOINT` is configured, they export OTLP/HTTP traces and metrics. The ASP.NET Core service also exports structured OpenTelemetry logs.

Service names:

```text
enterprise-document-assistant-api
enterprise-document-assistant-ai-service
```

ASP.NET Core instrumentation includes:

- inbound HTTP spans;
- outgoing `HttpClient` spans, including optional provider requests;
- runtime metrics;
- custom Search, Ask, answer-generation, upload/enqueue, and ingestion-worker spans;
- bounded trace, span, correlation, tenant, document, ingestion-job, provider-name, result-status, and controlled-failure attributes where applicable.

FastAPI instrumentation includes inbound request spans and a custom counter for its indexing boundary.

## Application metrics

The custom meter is `EnterpriseDocumentAssistant.Api`.

| Instrument | Type | Meaning |
|---|---|---|
| `document_assistant.authorization.denied` | Counter | HTTP 401 and 403 responses |
| `document_assistant.uploads.queued` | Counter | Uploads durably queued |
| `document_assistant.search.requests` | Counter | Search requests |
| `document_assistant.search.duration` | Histogram | Search duration in milliseconds |
| `document_assistant.search.results` | Histogram | Visible result count |
| `document_assistant.ask.requests` | Counter | Ask requests |
| `document_assistant.ask.duration` | Histogram | Retrieval plus answer-generation duration |
| `document_assistant.answer_generation.results` | Counter | Answered or insufficient-evidence results by provider/status |
| `document_assistant.answer_generation.failures` | Counter | Controlled provider failures by provider/code/retryability |
| `document_assistant.answer_generation.duration` | Histogram | Generation duration after retrieval |
| `document_assistant.answer_generation.input_tokens` | Histogram | Provider-reported input tokens when available |
| `document_assistant.answer_generation.output_tokens` | Histogram | Provider-reported output tokens when available |
| `document_assistant.ingestion.completed` | Counter | Completed jobs |
| `document_assistant.ingestion.retried` | Counter | Jobs returned to Pending |
| `document_assistant.ingestion.failed` | Counter | Terminal job failures |
| `document_assistant.ingestion.recovered` | Counter | Abandoned jobs recovered |
| `document_assistant.ingestion.duration` | Histogram | Worker processing duration |
| `document_assistant.audit.persisted` | Counter | Application audit events persisted |
| `document_assistant.audit.persistence_failures` | Counter | Supplementary audit persistence failures |

Metrics deliberately avoid user IDs, document IDs, file names, questions, source text, generated answer text, provider response text, API keys, and other unbounded or sensitive values. Provider name, status, controlled failure code, and retryability are bounded dimensions.

## Structured logging

The ASP.NET Core API writes JSON console logs with UTC timestamps, scopes, trace ID, span ID, and the log-safe `CorrelationLogId` digest. Worker scopes include document and ingestion-job identifiers. FastAPI uses structured JSON-style console logging.

The application must not log:

- bearer tokens;
- document text or source chunks;
- search query text;
- question text;
- generated answer text;
- provider API keys;
- provider response bodies;
- PostgreSQL passwords or connection strings;
- externally supplied file content;
- raw externally supplied correlation text.

Provider failures are logged using controlled application codes and retryability. Client responses and audit metadata do not include provider response bodies.

## Durable audit ledger

PostgreSQL table `audit_events` stores append-only tenant-aware audit records:

- occurrence time;
- tenant;
- authenticated actor and role;
- event type and action;
- resource type and identifier;
- outcome;
- correlation and trace identifiers;
- bounded JSON metadata that excludes document/query/question/answer/provider-response content.

Application roles receive only `SELECT` and `INSERT`; they do not receive `UPDATE` or `DELETE`.

Forced PostgreSQL Row-Level Security applies to the audit table:

- `document_app` can insert and read only the active `app.tenant_id`;
- `document_privileged` can insert and read across tenants;
- the API exposes `GET /api/audit/events` only to `Admin` and `PlatformAdmin`;
- `Admin` is restricted to its token tenant;
- `PlatformAdmin` can retrieve cross-tenant events through the explicit privileged path;
- ordinary `User` tokens receive `403 Forbidden`.

## Atomic mutation events

Database triggers write base audit events in the same transaction as:

- document creation;
- document status changes;
- ingestion-job creation;
- ingestion-job status changes.

This ensures the durable state transition and its base audit record commit or roll back together. These trigger records use a safe system/owner fallback when no application correlation context is available.

Application endpoints add correlated semantic events for:

- document listing;
- metadata creation;
- upload and durable enqueue;
- processing-status access;
- semantic search;
- grounded Ask and answer-provider outcomes;
- audit-ledger access.

For Search and Ask, audit metadata stores bounded operational values such as `topK`, result/source count, duration, answer status, provider/model identifiers, grounding state, reason/failure code, retryability, and provider-reported token counts. Query, question, source, generated answer, credential, and provider-response text are never stored.

## Local configuration

The default stack requires no collector and no answer-provider credential:

```text
OTEL_EXPORTER_OTLP_ENDPOINT=
ANSWER_GENERATION_PROVIDER=Deterministic
```

To export to an OTLP/HTTP collector reachable from Docker, set a base endpoint such as:

```text
OTEL_EXPORTER_OTLP_ENDPOINT=http://host.docker.internal:4318
```

The .NET exporter uses the configured endpoint. The Python exporter sends traces to `/v1/traces` and metrics to `/v1/metrics` beneath that base endpoint.

External answer-provider configuration is documented in [RAG_ASK_ENDPOINT.md](RAG_ASK_ENDPOINT.md). Provider credentials must come from trusted secret/configuration infrastructure rather than logs, source control, metrics, or audit details.

## Validation

CI verifies:

- valid and invalid correlation-ID behavior in both services;
- deterministic log-safe hashing of external correlation IDs;
- liveness and dependency-aware readiness;
- audit-table constraints, indexes, forced RLS, policies, and triggers;
- absence of `UPDATE` and `DELETE` grants for application roles;
- tenant-admin isolation and PlatformAdmin visibility;
- failure of an application-role attempt to update audit rows;
- absence of known sensitive search/question/provider values from serialized audit responses;
- controlled insufficient-evidence and provider-failure response contracts;
- provider-protocol tests that use no real credentials;
- strict answer-grounding regression thresholds and a retained machine-readable report;
- .NET and Python unit/integration tests, CodeQL, and dependency review.

## Remaining production work

This foundation does not provide:

- a bundled production collector or telemetry backend;
- dashboards, alert rules, or service-level objectives;
- long-term audit retention, archival, legal hold, or deletion automation;
- tamper-evident hashing or external immutable audit storage;
- sampling policy tuned from production traffic;
- end-to-end load, cardinality, provider-cost, or exporter-failure testing;
- production secret management or identity-provider lifecycle integration;
- an approved provider account, provider contract, data-processing agreement, or factual-accuracy guarantee.
