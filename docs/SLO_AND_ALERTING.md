# SLO and Alerting Foundation

## Purpose

This document defines the first version-controlled operational objectives for the reference deployment. The targets are engineering guardrails for local and pre-production validation. They are not production availability commitments and must be recalibrated from representative traffic, dependency behavior, maintenance windows, and business requirements before production use.

The canonical executable rules live in `infra/observability/alerts.yml`. The canonical incident procedures live in `docs/runbooks/AUDIT_OPERATIONS.md`.

## Measurement boundary

The optional observability stack receives OTLP telemetry from the ASP.NET Core API, independent worker, and FastAPI boundary. Prometheus scrapes the OpenTelemetry Collector Prometheus exporter. Grafana reads Prometheus. Prometheus routes firing alerts to a local Alertmanager receiver that deliberately has no external notification integration.

Metric dimensions must remain bounded. Do not add tenant IDs, user IDs, document IDs, file names, correlation IDs, trace IDs, search text, questions, generated answers, source text, invitation tokens, scanner signatures, provider bodies, API keys, or credentials as metric labels.

## Initial objectives

| Objective | Local target | Measurement | Alert |
|---|---:|---|---|
| API availability | >= 99.9% non-5xx | `1 - document_assistant:api_5xx_ratio5m` | `ApiErrorBudgetBurn` |
| API server p95 | <= 750 ms | `document_assistant:api_latency_p95_seconds5m` | `ApiLatencySloViolation` |
| Audit persistence | zero known failures | `document_assistant_audit_persistence_failures_total` | `AuditPersistenceFailure` |
| Audit integrity | zero failed verifications | `document_assistant_audit_integrity_failures_total` | `AuditIntegrityFailure` |
| Audit retention | zero failed archive runs | `document_assistant_audit_archive_failures_total` | `AuditArchiveFailure` |
| Ingestion terminal failures | fewer than 3 per 15 minutes | `document_assistant_ingestion_failed_total` | `IngestionTerminalFailureSpike` |
| Telemetry path | collector exporter continuously scrapeable | `up{job="otel-collector"}` | `TelemetryPipelineUnavailable` |

Health probes are included in the generic ASP.NET Core server-duration metric in this foundation. A production SLO should normally define an explicit request population and exclude probes, synthetic tests, and intentionally rejected traffic where appropriate.

## Availability error budget

A 99.9% availability objective permits a 0.1% server-error budget over the chosen reporting window. The current alert intentionally uses a short five-minute 5xx ratio and a ten-minute `for` period as an early operational guard, not a complete multi-window burn-rate implementation.

Before production:

1. choose a reporting window, such as 28 or 30 days;
2. define which routes and status outcomes belong to the SLI population;
3. add long-window and short-window burn-rate recording rules;
4. exclude planned maintenance only if the service contract allows it;
5. validate that retry storms and dependency failures are represented correctly;
6. assign an alert owner and paging/notification destination.

## Latency objective

The local p95 threshold is 750 ms for ASP.NET Core server duration. It is intentionally broad enough for development containers and is not a claim about production performance. Search and Ask also expose dedicated application histograms that should be used for endpoint-specific SLOs after representative load tests exist.

## Audit objectives

Audit persistence and audit-chain integrity are security controls rather than ordinary performance indicators. A single observed persistence or integrity failure is actionable. The application metrics record only the occurrence and result category; they do not include audit payloads or tenant identifiers.

The hash chain detects altered, missing, or reordered audit data when the chain head and event hashes are not recomputed consistently. It is not an external immutable ledger: a database superuser who can rewrite events, hashes, and chain heads can defeat this control. Production environments that require stronger non-repudiation should periodically anchor chain heads in independently controlled immutable storage or a signing service.

## Audit retention objective

`AuditRetention:Enabled` defaults to `false`. When enabled on the privileged worker, old active audit rows are moved in bounded batches to `audit_event_archive`. The archive retains the original event ID, chain sequence, previous hash, and event hash, so verification spans both tiers.

This is active-tier retention and archival, not legal deletion. Archive purge, legal hold, export, subject-access handling, and jurisdiction-specific retention periods remain deployment responsibilities.

## Alert delivery

The repository Alertmanager configuration uses `local-null`; no external notification is sent. A production deployment must configure a reviewed receiver such as PagerDuty, Opsgenie, email, Slack, or another supported channel, using secret management rather than committed credentials.

Before enabling paging, test every rule with synthetic non-sensitive signals and verify ownership, escalation, deduplication, silence procedures, and recovery notifications.

## Review cadence

Review these objectives after any of the following:

- a provider, embedding model, or retrieval architecture change;
- a significant ingestion/parser change;
- tenant scale or traffic shape changes;
- a PostgreSQL, collector, Prometheus, or Grafana upgrade;
- an incident that reveals a missing or noisy signal;
- a material change to retention, privacy, or compliance obligations.
