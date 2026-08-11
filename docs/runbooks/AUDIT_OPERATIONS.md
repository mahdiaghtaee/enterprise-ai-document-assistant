# Audit and Reliability Operations Runbook

## Scope

This runbook covers the first operational alerts and audit-retention controls shipped with the reference stack. Commands assume the Docker Compose development topology. Adapt identities, secret handling, storage, backup tooling, and escalation paths before production use.

Never paste document text, questions, answers, bearer tokens, invitation tokens, provider responses, scanner signatures, database passwords, or raw audit `details` into tickets or chat systems.

## Initial triage

For every alert:

1. record the alert name, start time, service, deployment version, and correlation/trace identifiers only when already available in bounded telemetry;
2. check `/health/live` and `/health/ready` for the API and Worker deployment state;
3. inspect Grafana `Enterprise Document Assistant - Operations` for the matching time window;
4. preserve database and application logs according to the incident policy;
5. avoid deleting, rewriting, or manually repairing audit rows before evidence has been captured;
6. escalate security-significant integrity failures separately from ordinary reliability failures.

## Audit persistence failure

Alert: `AuditPersistenceFailure`

Signal: `document_assistant_audit_persistence_failures_total`

1. Check PostgreSQL readiness and connection exhaustion.
2. Confirm `document_app`, `document_platform`, and `document_privileged` still have `INSERT` but not `UPDATE`/`DELETE` on `audit_events`.
3. Check database constraints, disk capacity, transaction errors, and recent migrations.
4. Correlate application logs using trace/correlation metadata without copying event payloads.
5. If audit writes are failing during security-sensitive mutations, consider stopping the affected write path until the audit dependency is restored.
6. After recovery, run audit integrity verification for affected tenants and document any known audit gap.

Do not silently backfill synthetic events to hide a persistence gap.

## Audit integrity failure

Alert: `AuditIntegrityFailure`

Signal: `document_assistant_audit_integrity_failures_total`

Treat this as a security-significant event until disproven.

1. Stop retention/archive maintenance for the affected environment.
2. Preserve a database backup/snapshot and relevant database logs before attempting repair.
3. Run `verify_audit_chain_scoped` using the tenant-scoped Admin path or the controlled PlatformAdmin/operator path.
4. Record only `is_valid`, `checked_count`, `first_broken_sequence`, and `head_sequence`; do not export event payloads to the incident channel.
5. Compare the affected sequence with database maintenance, restore, migration, and privileged-access records.
6. Determine whether the failure is caused by missing rows, payload mutation, hash/head mutation, an incomplete restore, or software defect.
7. Do not recalculate hashes in place until evidence is preserved and the incident owner approves remediation.
8. If a database superuser is suspected, treat the local hash chain as potentially compromised because a superuser can rewrite rows and chain heads together.

A production deployment with non-repudiation requirements should compare against independently anchored chain-head values.

## Audit retention failure

Alert: `AuditArchiveFailure`

Signal: `document_assistant_audit_archive_failures_total`

1. Confirm `AuditRetention:Enabled` is intentionally enabled.
2. Check the Worker has `ConnectionStrings:PostgresPrivileged`; the public API must not have it.
3. Verify `archive_audit_events(timestamptz, integer)` exists and only the privileged Worker role has execute permission.
4. Check archive-table disk growth, locks, transaction failures, and retention-option bounds.
5. Verify the chain before retrying archival.
6. Retry with a smaller batch only after identifying lock or resource pressure; never grant direct application `DELETE` privileges as a workaround.
7. Verify the chain again after archival and confirm archived rows remain visible through authorized audit queries.

Retention is disabled by default. A failure while disabled indicates configuration drift or an unexpected caller.

## Ingestion terminal failure spike

Alert: `IngestionTerminalFailureSpike`

Signal: `document_assistant_ingestion_failed_total`

1. Group failures by controlled error code, not document name or document content.
2. Check parser limits, storage availability, PostgreSQL/pgvector health, and embedding generation.
3. For `ocr-required`, do not treat image-only documents as parser failures; OCR is intentionally not bundled.
4. For malware-scanner failures, use the document-format security runbook/configuration and preserve fail-closed behavior.
5. Check whether one format/provider/deployment release caused the increase.
6. Avoid increasing parser limits until memory/CPU impact is measured and reviewed.

## API error budget burn

Alert: `ApiErrorBudgetBurn`

Recording rule: `document_assistant:api_5xx_ratio5m`

1. Confirm the Prometheus target is healthy so a telemetry outage is not being mistaken for an application outage.
2. Identify which service/dependency is returning or causing 5xx responses.
3. Check PostgreSQL, AI-service readiness, worker backlog, and external answer provider status when enabled.
4. Correlate with deployments and migrations.
5. Roll back or disable the affected optional provider only when the deterministic/local path remains safe and behavior is understood.
6. Track the consumed error budget over the reporting window chosen for the deployment.

The repository threshold is a development guard and must be recalibrated for production traffic.

## API latency SLO violation

Alert: `ApiLatencySloViolation`

Recording rule: `document_assistant:api_latency_p95_seconds5m`

1. Compare API duration with Search, Ask, provider-generation, and ingestion metrics.
2. Separate application latency from collector/Prometheus scrape delay.
3. Check PostgreSQL query latency, connection saturation, provider timeout behavior, and host CPU/memory pressure.
4. Confirm the workload is representative before changing the 750 ms local threshold.
5. Prefer profiling and query/provider optimization over raising timeouts globally.

## Telemetry pipeline unavailable

Alert: `TelemetryPipelineUnavailable`

Signal: `up{job="otel-collector"}`

1. Check `otel-collector` container/process health and port `13133`.
2. Check Prometheus target status for `otel-collector:9464`.
3. Validate Collector configuration and exporter startup logs.
4. Confirm application OTLP endpoints resolve to the Collector from their deployment network.
5. Telemetry failure must not weaken authentication, tenant RLS, document processing, or audit persistence.
6. Restore telemetry and confirm expected metric names reappear before closing the incident.

## Backup and restore verification

Run a restore exercise on a non-production copy at a defined cadence.

1. Back up PostgreSQL and stored document objects/volume using deployment-approved tooling.
2. Record backup identifiers, timestamps, encryption state, and retention policy without exposing credentials.
3. Restore into an isolated database and storage location.
4. Verify tenant, membership, document, ingestion, chunk, active audit, archived audit, and chain-head row counts.
5. Run audit-chain verification for every restored tenant.
6. Confirm the restored application can read authorized active and archived audit history while cross-tenant access remains denied.
7. Confirm `document_app` and `document_platform` still cannot directly update/delete/truncate audit tables.
8. Confirm the Worker alone can invoke bounded archival.
9. Run representative document processing, Search, Ask, and health checks.
10. Record recovery-point and recovery-time observations. Do not claim an RPO/RTO until repeated exercises demonstrate them.

A restore that loses only `audit_chain_heads` is still an integrity incident; chain heads are part of the verification state and must be backed up with audit rows.

## Post-incident review

For material incidents, capture:

- trigger and detection path;
- affected service/tenant scope using identifiers only where policy permits;
- whether the audit chain remained valid;
- whether any audit events were unavailable or lost;
- recovery actions and evidence preserved;
- missing metrics, alerts, or runbook steps;
- threshold/noise adjustments with a reviewed reason;
- follow-up owner and issue.
