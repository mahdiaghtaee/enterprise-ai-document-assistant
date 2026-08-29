# Local Development Guide

This guide covers managed tenant provisioning, durable membership authorization, safe TXT/PDF/DOCX ingestion, split API/Worker processing, PostgreSQL Row-Level Security, tamper-evident audit operations, and the optional local observability stack.

## Prerequisites

For Docker Compose, install Docker, Docker Compose v2, Git, and Python 3.11 or later. The .NET 8 SDK is required when running .NET tests or evaluation commands directly on the host.

## Environment setup

Copy the template when changing ports, local credentials, processing limits, audit retention, or optional integrations:

```bash
cp .env.example .env
```

Windows PowerShell:

```powershell
Copy-Item .env.example .env
```

Core settings:

| Variable | Default | Purpose |
|---|---:|---|
| `WEB_UI_HOST_PORT` | `3000` | Web UI host port |
| `API_HOST_PORT` | `5000` | Public ASP.NET Core API host port |
| `AI_SERVICE_HOST_PORT` | `8000` | FastAPI host port |
| `POSTGRES_HOST_PORT` | `5432` | PostgreSQL host port |
| `REDIS_HOST_PORT` | `6379` | Redis host port |
| `APP_DB_PASSWORD` | `document-app-local` | Tenant-RLS public API role password |
| `PLATFORM_DB_PASSWORD` | `document-platform-local` | Narrow platform/lifecycle role password |
| `PRIVILEGED_DB_PASSWORD` | `document-privileged-local` | Independent Worker role password |

Document-processing settings:

| Variable | Default |
|---|---:|
| `DOCUMENT_MAX_PDF_PAGES` | `200` |
| `DOCUMENT_MAX_DOCX_ARCHIVE_ENTRIES` | `2048` |
| `DOCUMENT_MAX_DOCX_EXPANDED_BYTES` | `52428800` |
| `DOCUMENT_MAX_EXTRACTED_CHARACTERS` | `1000000` |
| `DOCUMENT_MAX_DOCX_XML_CHARACTERS` | `5000000` |
| `FILE_THREAT_SCANNING_PROVIDER` | `Disabled` |
| `CLAMAV_PORT` | `3310` |
| `CLAMAV_TIMEOUT` | `00:00:10` |

Audit-retention settings:

| Variable | Default | Purpose |
|---|---:|---|
| `AUDIT_RETENTION_ENABLED` | `false` | Enables Worker-hosted active-to-archive movement |
| `AUDIT_RETENTION_DAYS` | `90` | Active-tier age cutoff example |
| `AUDIT_RETENTION_BATCH_SIZE` | `1000` | Maximum rows moved per database call |
| `AUDIT_RETENTION_MAX_BATCHES` | `10` | Maximum batches per Worker run |
| `AUDIT_RETENTION_INTERVAL` | `1.00:00:00` | Interval between runs |
| `AUDIT_RETENTION_INITIAL_DELAY` | `00:01:00` | Startup delay before first run |

Retention is deliberately disabled by default. The 90-day value is a local example, not a legal or business retention recommendation.

Optional observability settings:

| Variable | Default |
|---|---:|
| `OTEL_COLLECTOR_HOST_PORT` | `4318` |
| `PROMETHEUS_HOST_PORT` | `9090` |
| `GRAFANA_HOST_PORT` | `3001` |
| `ALERTMANAGER_HOST_PORT` | `9093` |
| `GRAFANA_ADMIN_PASSWORD` | `admin-local` |

All committed passwords are local-development values only. Production must use managed secrets, restricted networks, separate service identities, reviewed retention, and production identity/provider configuration.

## Start the default stack

```bash
docker compose up --build
```

Expected services:

- `document-api`: public API in `ApplicationMode=Api`, without the privileged Worker credential;
- `document-worker`: internal-only process in `ApplicationMode=Worker`;
- Web UI;
- FastAPI integration boundary;
- PostgreSQL with pgvector and forced RLS;
- Redis.

The default stack does **not** start ClamAV or a telemetry backend. `FILE_THREAT_SCANNING_PROVIDER=Disabled` and `AUDIT_RETENTION_ENABLED=false` preserve a service-free, non-destructive local default.

The API and Worker share `document-storage`. The API validates/scans before persistence, then atomically enqueues. The Worker performs bounded extraction/indexing and, only when explicitly enabled, bounded audit archival using its privileged database identity.

Fresh PostgreSQL volumes initialize:

- tenant/membership/invitation tables;
- documents, semantic chunks, and ingestion jobs;
- append-only `audit_events`;
- per-tenant audit-chain fields and `audit_chain_heads`;
- tenant-RLS protected `audit_event_archive`;
- document/runtime/platform/privileged roles and forced RLS policies;
- pgvector indexes and ingestion constraints.

## Start the optional observability stack

Use the Compose override explicitly:

```bash
docker compose -f docker-compose.yml -f docker-compose.observability.yml up --build
```

This adds:

- OpenTelemetry Collector;
- Prometheus;
- Grafana;
- Alertmanager.

Applications export OTLP to the Collector. Prometheus scrapes the Collector's Prometheus exporter. Grafana is provisioned from repository files. Prometheus loads repository recording/alert rules and routes alerts to Alertmanager.

The committed Alertmanager receiver is `local-null`; it sends no external notification.

Local URLs:

| Service | URL |
|---|---|
| Web UI | `http://localhost:3000` |
| Swagger UI | `http://localhost:5000/swagger` |
| API health | `http://localhost:5000/health` |
| API readiness | `http://localhost:5000/health/ready` |
| FastAPI health | `http://localhost:8000/health` |
| Collector OTLP/HTTP | `http://localhost:4318` |
| Collector health | `http://localhost:13133` |
| Prometheus | `http://localhost:9090` |
| Grafana | `http://localhost:3001` |
| Alertmanager | `http://localhost:9093` |

Validate version-controlled observability assets without starting containers:

```bash
python scripts/verify_observability_assets.py
```

## Recommended local demo

```bash
python scripts/demo_flow.py
```

Without `JWT_TOKEN`, the demo creates development tokens, provisions `demo-tenant`, creates/accepts an invitation, uploads a sample, waits for the independent Worker, then runs Search and grounded Ask.

Run the document-format smoke flow against a running stack:

```bash
python scripts/document_format_smoke.py
```

It provisions an isolated local tenant, uploads real PDF/DOCX fixtures, waits for Worker completion, verifies retrieval, and confirms a spoofed PDF is rejected before enqueue.

## Safe upload behavior

Supported pairs are exact:

| Extension | Content type |
|---|---|
| `.txt` | `text/plain` |
| `.pdf` | `application/pdf` |
| `.docx` | `application/vnd.openxmlformats-officedocument.wordprocessingml.document` |

The upload path validates authorization, size, extension/MIME agreement, real PDF/DOCX structure, parser/resource limits, and the optional malware verdict before durable enqueue. The Worker re-applies processing limits. Image-only/scanned PDFs return `ocr-required`; OCR is not bundled.

When `FileThreatScanning:Provider=ClamAv` is selected, scanner timeout/unavailability fails closed. The repository does not operate the scanner daemon or signature updates.

## Manual tenant lifecycle

Generate local tokens:

```bash
PLATFORM_TOKEN=$(python scripts/create_dev_token.py --user platform-admin --tenant platform --role PlatformAdmin)
ADMIN_TOKEN=$(python scripts/create_dev_token.py --user tenant-a-admin --tenant tenant-a --role Admin)
USER_TOKEN=$(python scripts/create_dev_token.py --user user-a --tenant tenant-a --role User)
```

Provision tenant:

```bash
curl http://localhost:5000/api/platform/tenants \
  -H "Authorization: Bearer $PLATFORM_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"tenantId":"tenant-a","displayName":"Tenant A","initialAdminUserId":"tenant-a-admin"}'
```

Create invitation:

```bash
curl http://localhost:5000/api/tenant/invitations \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"inviteeUserId":"user-a","role":"User","lifetimeHours":24}'
```

Accept the one-time token as the invited subject:

```bash
curl http://localhost:5000/api/tenant/invitations/accept \
  -H "Authorization: Bearer $USER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"token":"<one-time-token>"}'
```

Removing membership or disabling the tenant makes the next protected request fail without waiting for JWT expiration.

## Audit integrity verification

Tenant Admin verifies its own tenant only:

```bash
curl http://localhost:5000/api/audit/integrity \
  -H "Authorization: Bearer $ADMIN_TOKEN"
```

PlatformAdmin must name a tenant explicitly:

```bash
curl "http://localhost:5000/api/audit/integrity?tenantId=tenant-a" \
  -H "Authorization: Bearer $PLATFORM_TOKEN"
```

The response includes:

- `tenantId`;
- `isValid`;
- `checkedCount`;
- `firstBrokenSequence` when present;
- `headSequence`.

It does not expose event payloads or hashes.

Audit history includes archived rows by default:

```bash
curl "http://localhost:5000/api/audit/events?limit=100&includeArchived=true" \
  -H "Authorization: Bearer $ADMIN_TOKEN"
```

Use `includeArchived=false` for active rows only.

## Enable audit retention deliberately

Before changing `AUDIT_RETENTION_ENABLED=true`:

1. back up PostgreSQL and document storage;
2. review the required active retention period;
3. verify `audit_events`, `audit_event_archive`, and `audit_chain_heads` are included in backup scope;
4. run integrity verification;
5. confirm only the Worker has `ConnectionStrings__PostgresPrivileged`;
6. start with a conservative batch size;
7. monitor archive and integrity metrics;
8. verify the chain after the first archive run.

Do not grant direct `DELETE` on audit tables to make retention work. The Worker uses `archive_audit_events(cutoff,batch_size)`, which moves rows transactionally through the constrained database function.

Archival is not legal deletion. Archive purge/legal hold/export/deletion require a separate policy and implementation.

## Verify PostgreSQL security

```bash
docker compose exec -T postgres psql -U documents -d documents -c "SELECT relname, relrowsecurity, relforcerowsecurity FROM pg_class WHERE relname IN ('tenants','tenant_memberships','tenant_invitations','documents','document_chunks','document_ingestion_jobs','audit_events','audit_event_archive');"
docker compose exec -T postgres psql -U documents -d documents -c "SELECT rolname, rolsuper, rolbypassrls FROM pg_roles WHERE rolname IN ('document_app','document_platform','document_privileged');"
docker compose exec -T postgres psql -U documents -d documents -c "SELECT has_table_privilege('document_app','audit_events','DELETE'), has_table_privilege('document_privileged','audit_event_archive','DELETE');"
docker compose exec -T postgres psql -U documents -d documents -c "SELECT has_function_privilege('document_privileged','archive_audit_events(timestamp with time zone,integer)','EXECUTE');"
```

Expected:

- tenant tables and archive have forced RLS;
- application roles are non-superuser/non-`BYPASSRLS`;
- application/platform/Worker roles cannot directly mutate active/archive audit rows;
- only the privileged Worker role can execute the archive function.

Verify process credentials:

```bash
docker compose exec -T document-api env | grep ConnectionStrings__Postgres
docker compose exec -T document-worker env | grep ConnectionStrings__Postgres
```

The API must not expose `ConnectionStrings__PostgresPrivileged`; the Worker must have it.

## Run tests

```bash
dotnet test tests/api-dotnet/EnterpriseDocumentAssistant.Api.Tests.csproj --configuration Release
```

PostgreSQL integration tests run when `POSTGRES_TEST_CONNECTION_STRING` is configured. CI covers lifecycle/RLS, audit-chain concurrency/tamper/archive behavior, retention Worker boundaries, document formats, retrieval, grounded answers, and optional observability provisioning.

## Existing PostgreSQL volumes

Entrypoint scripts run only for a fresh volume. Existing databases do not automatically receive later audit/lifecycle migrations.

For required data:

1. back up PostgreSQL and stored files;
2. review the existing ownership/tenant/audit/lifecycle scripts;
3. review and apply `zzzzzzzz-audit-operations.sql`;
4. apply `zzzzzzzzz-audit-verification-access.sql`;
5. verify `pgcrypto`, chain fields, chain heads, archive table/RLS, function grants, and direct mutation denial;
6. run integrity verification for each tenant;
7. verify all tenant/lifecycle/RLS constraints and role grants;
8. deploy separate API and Worker identities;
9. serve traffic only after negative access and integrity tests pass.

For disposable local data only:

```bash
docker compose down --volumes
docker compose up --build
```

## Troubleshooting

### `401 Unauthorized`

Verify bearer header, signing key, issuer, audience, expiration, and environment.

### `403 Forbidden`

Verify tenant status, durable membership, durable Admin role, and token claims. Tenant Admin cannot verify another tenant's audit chain.

### Audit integrity returns invalid

Treat as security-significant until disproven. Stop retention maintenance, preserve a database snapshot, and follow [the audit operations runbook](runbooks/AUDIT_OPERATIONS.md#audit-integrity-failure). Do not recalculate hashes in place before evidence is preserved.

### Retention fails

Confirm retention is intentionally enabled, Worker has the privileged connection, the archive function exists, and no direct DELETE grant was added. Follow [the retention runbook](runbooks/AUDIT_OPERATIONS.md#audit-retention-failure).

### Prometheus has no application metrics

With the observability override running:

```bash
curl http://localhost:13133/
curl http://localhost:9090/api/v1/targets
```

Confirm application containers use `http://otel-collector:4318` and Prometheus can scrape `otel-collector:9464`.

### Grafana dashboard is missing

Check Grafana logs and repository provisioning files under `infra/observability/grafana/`. The expected dashboard UID is `enterprise-document-assistant-operations` and the Prometheus datasource UID is `prometheus`.

### Worker does not process jobs

Check:

```bash
docker compose ps document-worker
docker compose logs document-worker
```

Confirm API and Worker share `document-storage` and only Worker has the privileged database connection.

### PostgreSQL initialization changed after first startup

Initialization scripts do not rerun against existing volumes. Apply reviewed migrations after backup rather than deleting required data.

See [Health, Audit, and Observability](HEALTH_AND_OBSERVABILITY.md), [SLO and Alerting](SLO_AND_ALERTING.md), [Audit Operations Runbook](runbooks/AUDIT_OPERATIONS.md), [Managed Tenant Lifecycle](TENANT_LIFECYCLE.md), and [Tenant Isolation](TENANT_ISOLATION.md).
