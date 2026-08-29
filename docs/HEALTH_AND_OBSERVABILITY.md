# Health, Audit, and Observability

This document describes the implemented operational-diagnostics and audit-operations foundation for the Enterprise AI Document Assistant.

## Operational goals

The system should answer these questions without inspecting private document content:

1. Is each process running and ready?
2. Are required dependencies reachable?
3. Which bounded operation failed and how is it correlated?
4. Which authenticated actor performed a security-relevant operation?
5. Is the tenant audit history structurally intact across active and archived rows?
6. Are reliability and audit controls inside their reviewed local SLO thresholds?

Telemetry and the durable audit ledger remain separate. Telemetry is optimized for aggregation and diagnosis; audit events are tenant-aware security/business records.

## Correlation and trace context

Every ASP.NET Core and FastAPI response includes a validated `X-Correlation-ID`. Missing or invalid values are replaced with a generated identifier. Standard W3C `traceparent` propagation is handled by OpenTelemetry instrumentation.

The ASP.NET Core log scope does not write externally supplied correlation text directly. It stores a deterministic SHA-256-derived `CorrelationLogId`; the original validated identifier remains in the response, audit record, and trace context. Correlation identifiers are diagnostic labels, not authorization credentials.

## Health endpoints

| Endpoint | Purpose | Dependency checks |
|---|---|---|
| `GET /health` | Process status and safe feature-state summary | None |
| `GET /health/live` | Liveness | None |
| `GET /health/ready` | Readiness | PostgreSQL and FastAPI health |

`GET /health` reports the process mode, configured file-threat-scanning provider, and whether audit retention is active in the Worker. It does not disclose scanner endpoints, connection strings, retention cutoffs, or credentials.

The external answer provider is request-time functionality and is not called by readiness probes.

## OpenTelemetry signals

The default repository remains collector-free. When `OpenTelemetry:OtlpEndpoint` / `OTEL_EXPORTER_OTLP_ENDPOINT` is configured, the services export OTLP signals.

Service names include:

```text
enterprise-document-assistant-api
enterprise-document-assistant-worker
enterprise-document-assistant-ai-service
```

ASP.NET Core instrumentation includes inbound HTTP, outgoing HttpClient, runtime metrics, Search, Ask, answer-generation, upload, ingestion, and audit-operation signals. FastAPI includes correlated inbound request instrumentation.

## Application metrics

The custom meter is `EnterpriseDocumentAssistant.Api`.

| Instrument | Type | Meaning |
|---|---|---|
| `document_assistant.authorization.denied` | Counter | Rejected authorization decisions |
| `document_assistant.uploads.queued` | Counter | Uploads durably queued |
| `document_assistant.search.requests` | Counter | Search requests |
| `document_assistant.search.duration` | Histogram | Search duration |
| `document_assistant.search.results` | Histogram | Visible result count |
| `document_assistant.ask.requests` | Counter | Ask requests |
| `document_assistant.ask.duration` | Histogram | Retrieval plus generation duration |
| `document_assistant.answer_generation.results` | Counter | Answer/insufficient-evidence results |
| `document_assistant.answer_generation.failures` | Counter | Controlled provider failures |
| `document_assistant.answer_generation.duration` | Histogram | Provider/local generation duration |
| `document_assistant.answer_generation.input_tokens` | Histogram | Provider-reported input tokens when available |
| `document_assistant.answer_generation.output_tokens` | Histogram | Provider-reported output tokens when available |
| `document_assistant.ingestion.completed` | Counter | Completed ingestion jobs |
| `document_assistant.ingestion.retried` | Counter | Retried ingestion jobs |
| `document_assistant.ingestion.failed` | Counter | Terminal ingestion failures |
| `document_assistant.ingestion.recovered` | Counter | Abandoned jobs recovered |
| `document_assistant.ingestion.duration` | Histogram | Worker processing duration |
| `document_assistant.audit.persisted` | Counter | Application audit events persisted |
| `document_assistant.audit.persistence_failures` | Counter | Supplementary audit persistence failures |
| `document_assistant.audit.integrity_checks` | Counter | Audit-chain verification checks |
| `document_assistant.audit.integrity_failures` | Counter | Failed audit-chain checks |
| `document_assistant.audit.archive_runs` | Counter | Retention/archive worker runs |
| `document_assistant.audit.archived_events` | Counter | Rows moved to the archive tier |
| `document_assistant.audit.archive_failures` | Counter | Failed archive runs |
| `document_assistant.audit.archive_duration` | Histogram | Archive-run duration |

Metric labels deliberately exclude tenant IDs, user IDs, document IDs, file names, correlation IDs, trace IDs, questions, source text, generated answers, tokens, provider bodies, scanner signatures, credentials, and other content-derived/high-cardinality values.

## Durable audit ledger and hash chain

`audit_events` remains the active append-only audit table. New events receive database-generated, per-tenant integrity fields:

- `chain_sequence`;
- `previous_hash`;
- `event_hash`.

The `BEFORE INSERT` trigger serializes same-tenant inserts using a transaction-scoped advisory lock and updates the tenant chain head in the same transaction. The event hash is SHA-256 over the previous hash plus a canonical representation of the bounded audit fields. Callers cannot supply a trusted chain sequence/hash because the trigger overwrites those values.

Existing rows are deterministically backfilled in `(occurred_at, id)` order when the migration is applied.

This is **tamper-evident**, not externally immutable. A database superuser that can rewrite events, hashes, and chain heads remains inside the trust boundary. Stronger non-repudiation requires independently controlled chain-head anchoring or immutable signed storage.

## Audit archive and retention

`audit_event_archive` stores archived audit events with the original event ID, chain sequence, previous hash, event hash, and a separate archive timestamp. Forced tenant RLS applies to archived reads.

Application/platform/worker roles do not receive direct `UPDATE`, `DELETE`, or `TRUNCATE` access to active or archived audit tables. The only application retention path is the bounded `archive_audit_events(cutoff, batch_size)` `SECURITY DEFINER` function, and execute permission is granted only to the privileged Worker role.

`AuditRetentionWorker` is registered only when the process runs Worker responsibilities and has `PostgresPrivileged`. It is **disabled by default**. When enabled it:

1. computes a retention cutoff from `RetentionDays`;
2. moves rows in configured batches;
3. stops at `MaxBatchesPerRun` or a short batch;
4. records bounded success/failure metrics;
5. never grants or uses direct application-table deletion privileges.

Default settings:

```text
AUDIT_RETENTION_ENABLED=false
AUDIT_RETENTION_DAYS=90
AUDIT_RETENTION_BATCH_SIZE=1000
AUDIT_RETENTION_MAX_BATCHES=10
AUDIT_RETENTION_INTERVAL=1.00:00:00
AUDIT_RETENTION_INITIAL_DELAY=00:01:00
```

Archival is not legal deletion. Archive purge, legal hold, subject-access/export, jurisdiction-specific retention, and immutable backup policy remain deployment responsibilities.

## Integrity verification API

`GET /api/audit/integrity` requires the existing Admin authorization policy.

Tenant Admin:

```http
GET /api/audit/integrity
Authorization: Bearer <tenant-admin-token>
```

The Tenant Admin is always scoped to its authenticated tenant. Supplying a different `tenantId` is rejected.

PlatformAdmin:

```http
GET /api/audit/integrity?tenantId=<target-tenant>
Authorization: Bearer <platform-admin-token>
```

PlatformAdmin must name the tenant explicitly. The response contains only:

- tenant ID;
- `isValid`;
- `checkedCount`;
- `firstBrokenSequence` when present;
- `headSequence`.

It does not return audit event details or hashes. Verification spans archive and active rows in chain order. The verification action itself is audited with bounded result metadata.

`GET /api/audit/events` includes archived rows by default; `includeArchived=false` limits the query to the active table.

## Optional local operational stack

The normal command remains unchanged:

```bash
docker compose up --build
```

To run the versioned local observability backend:

```bash
docker compose -f docker-compose.yml -f docker-compose.observability.yml up --build
```

The override starts:

- OpenTelemetry Collector;
- Prometheus;
- Grafana;
- Alertmanager.

It redirects application OTLP export to the Collector. Prometheus scrapes the Collector Prometheus exporter; Grafana uses a provisioned Prometheus datasource and version-controlled dashboard; Prometheus loads version-controlled recording/alert rules and routes alerts to Alertmanager.

The committed Alertmanager receiver is `local-null`. It sends nothing externally. Production notification receivers require explicit secret-managed configuration.

Default local endpoints:

| Service | URL |
|---|---|
| Collector OTLP/HTTP | `http://localhost:4318` |
| Collector health | `http://localhost:13133` |
| Prometheus | `http://localhost:9090` |
| Grafana | `http://localhost:3001` |
| Alertmanager | `http://localhost:9093` |

Grafana admin credentials in `.env.example` are local-development values only.

## SLOs, alerts, and runbooks

The executable Prometheus rules are in `infra/observability/alerts.yml`. Initial local objectives and their limitations are documented in [SLO_AND_ALERTING.md](SLO_AND_ALERTING.md). Incident procedures and backup/restore verification are in [runbooks/AUDIT_OPERATIONS.md](runbooks/AUDIT_OPERATIONS.md).

Current alert names:

- `AuditPersistenceFailure`;
- `AuditIntegrityFailure`;
- `AuditArchiveFailure`;
- `IngestionTerminalFailureSpike`;
- `ApiErrorBudgetBurn`;
- `ApiLatencySloViolation`;
- `TelemetryPipelineUnavailable`.

The repository's availability and latency targets are development/pre-production guardrails, not production commitments.

## Structured logging boundary

Application logs must not include bearer/invitation tokens, document/source content, search or question text, generated answers, provider/scanner response bodies, API keys, database passwords/full connection strings, uploaded bytes, or raw externally supplied correlation text.

Audit-retention logs include only counts and cutoff timestamps. Audit-integrity metrics include only validity/result counts; tenant identifiers are not metric labels.

## CI validation

CI now verifies:

- existing correlation, health, tenant, RLS, retrieval, answer, document-format, and audit behavior;
- audit chain generation under concurrent same-tenant inserts;
- tamper detection after privileged payload mutation;
- cross-tenant verifier denial for tenant runtime roles;
- archive continuity across active + archived tiers;
- absence of direct audit mutation privileges;
- bounded retention worker batching/failure/cancellation behavior;
- base and observability Compose configurations;
- optional stack startup and Collector health;
- OTLP ASP.NET metrics arriving in Prometheus;
- Prometheus recording/alert rule loading;
- Grafana datasource/dashboard provisioning;
- version-controlled runbook links and pinned observability image tags.

## Remaining production work

This foundation still does not provide:

- external immutable audit anchoring or signed checkpoints;
- jurisdiction-specific archive purge/legal-hold automation;
- production notification integrations or paging ownership;
- a long-term production metrics/traces/logs backend or HA observability topology;
- production-calibrated multi-window error-budget burn rules;
- load-derived sampling and cardinality budgets;
- proven RPO/RTO values (the runbook defines exercises but repeated evidence is required);
- centralized secret management or production identity-provider lifecycle integration.
