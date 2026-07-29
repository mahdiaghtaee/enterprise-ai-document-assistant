# Health, Audit, and Observability

This document describes the implemented operational-diagnostics foundation for the Enterprise AI Document Assistant.

## Operational goals

The system should answer four questions without inspecting private document content:

1. Is each process running?
2. Are required dependencies reachable?
3. Which request, trace, tenant, document, or ingestion job failed?
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

Missing or invalid values are replaced with a generated 32-character identifier. The identifier is placed in structured log scopes and OpenTelemetry activity tags. Outgoing ASP.NET Core requests propagate the same header. Standard W3C `traceparent` propagation is handled by OpenTelemetry HTTP instrumentation.

Correlation identifiers are diagnostic labels, not authentication credentials, and must not be trusted for authorization.

## Health endpoints

### ASP.NET Core API

| Endpoint | Purpose | Dependency checks |
|---|---|---|
| `GET /health` | Backward-compatible process health | None |
| `GET /health/live` | Liveness/probe that confirms the process can serve HTTP | None |
| `GET /health/ready` | Readiness for traffic | PostgreSQL and FastAPI health |

The readiness endpoint returns `503 Service Unavailable` when a required dependency is unhealthy. Its response includes dependency status and duration, correlation ID, and trace ID, but not credentials or connection strings.

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
- outgoing `HttpClient` spans;
- runtime metrics;
- custom Search, Ask, upload/enqueue, and ingestion-worker spans;
- trace, span, correlation, tenant, document, and ingestion-job attributes where applicable.

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
| `document_assistant.ask.duration` | Histogram | Ask retrieval duration |
| `document_assistant.ingestion.completed` | Counter | Completed jobs |
| `document_assistant.ingestion.retried` | Counter | Jobs returned to Pending |
| `document_assistant.ingestion.failed` | Counter | Terminal job failures |
| `document_assistant.ingestion.recovered` | Counter | Abandoned jobs recovered |
| `document_assistant.ingestion.duration` | Histogram | Worker processing duration |
| `document_assistant.audit.persisted` | Counter | Application audit events persisted |
| `document_assistant.audit.persistence_failures` | Counter | Supplementary audit persistence failures |

Metrics deliberately avoid user IDs, document IDs, file names, queries, and other unbounded high-cardinality values.

## Structured logging

The ASP.NET Core API writes JSON console logs with UTC timestamps, scopes, trace ID, span ID, and correlation ID. Worker scopes include document and ingestion-job identifiers. FastAPI uses structured JSON-style console logging.

The application must not log:

- bearer tokens;
- document text or source chunks;
- search query text;
- question text;
- PostgreSQL passwords or connection strings;
- externally supplied file content.

## Durable audit ledger

PostgreSQL table `audit_events` stores append-only tenant-aware audit records:

- occurrence time;
- tenant;
- authenticated actor and role;
- event type and action;
- resource type and identifier;
- outcome;
- correlation and trace identifiers;
- bounded JSON metadata that excludes document/query/question content.

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
- grounded Ask;
- audit-ledger access.

For Search and Ask, audit metadata stores only `topK`, result/source count, and duration. The query and question are never stored.

## Local configuration

The default stack requires no collector:

```text
OTEL_EXPORTER_OTLP_ENDPOINT=
```

To export to an OTLP/HTTP collector reachable from Docker, set a base endpoint such as:

```text
OTEL_EXPORTER_OTLP_ENDPOINT=http://host.docker.internal:4318
```

The .NET exporter uses the configured endpoint. The Python exporter sends traces to `/v1/traces` and metrics to `/v1/metrics` beneath that base endpoint.

## Validation

CI verifies:

- valid and invalid correlation-ID behavior in both services;
- liveness and dependency-aware readiness;
- audit-table constraints, indexes, forced RLS, policies, and triggers;
- absence of `UPDATE` and `DELETE` grants for application roles;
- tenant-admin isolation and PlatformAdmin visibility;
- failure of an application-role attempt to update audit rows;
- absence of a known sensitive search string from the serialized audit response;
- .NET and Python unit/integration tests, CodeQL, and dependency review.

## Remaining production work

This foundation does not provide:

- a bundled production collector or telemetry backend;
- dashboards, alert rules, or service-level objectives;
- long-term audit retention, archival, legal hold, or deletion automation;
- tamper-evident hashing or external immutable audit storage;
- sampling policy tuned from production traffic;
- end-to-end load, cardinality, or exporter-failure testing;
- production secret management or identity-provider lifecycle integration.
