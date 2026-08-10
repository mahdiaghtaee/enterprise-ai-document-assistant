# Local Development Guide

This guide covers managed tenant provisioning, durable membership authorization, safe TXT/PDF/DOCX ingestion, split API/worker processing, PostgreSQL Row-Level Security, and isolation verification.

## Prerequisites

For Docker Compose, install Docker, Docker Compose v2, Git, and Python 3.11 or later. The .NET 8 SDK is required only when running .NET tests or evaluation commands directly on the host.

## Environment setup

Copy the local environment template when changing ports, development credentials, document-processing limits, or optional integrations:

```bash
cp .env.example .env
```

Windows PowerShell:

```powershell
Copy-Item .env.example .env
```

Core local settings:

| Variable | Default | Purpose |
|---|---:|---|
| `WEB_UI_HOST_PORT` | `3000` | Web UI host port |
| `API_HOST_PORT` | `5000` | Public ASP.NET Core API host port |
| `AI_SERVICE_HOST_PORT` | `8000` | FastAPI host port |
| `POSTGRES_HOST_PORT` | `5432` | PostgreSQL host port |
| `REDIS_HOST_PORT` | `6379` | Redis host port |
| `ASPNETCORE_ENVIRONMENT` | `Development` | Loads local JWT configuration |
| `POSTGRES_DB` | `documents` | Local database name |
| `POSTGRES_USER` | `documents` | Initialization administrator |
| `POSTGRES_PASSWORD` | `documents` | Initialization administrator password |
| `APP_DB_PASSWORD` | `document-app-local` | Tenant-RLS public API role password |
| `PLATFORM_DB_PASSWORD` | `document-platform-local` | Narrow lifecycle/cross-tenant read role password |
| `PRIVILEGED_DB_PASSWORD` | `document-privileged-local` | Independent ingestion-worker role password |

Safe document-processing settings:

| Variable | Default | Purpose |
|---|---:|---|
| `DOCUMENT_MAX_PDF_PAGES` | `200` | Maximum PDF pages accepted/processed |
| `DOCUMENT_MAX_DOCX_ARCHIVE_ENTRIES` | `2048` | Maximum DOCX ZIP entries |
| `DOCUMENT_MAX_DOCX_EXPANDED_BYTES` | `52428800` | Maximum total uncompressed DOCX bytes |
| `DOCUMENT_MAX_EXTRACTED_CHARACTERS` | `1000000` | Maximum normalized extracted text characters |
| `DOCUMENT_MAX_DOCX_XML_CHARACTERS` | `5000000` | Maximum XML characters parsed per inspected/extracted DOCX part |
| `FILE_THREAT_SCANNING_PROVIDER` | `Disabled` | `Disabled` or fail-closed `ClamAv` |
| `CLAMAV_HOST` | `clamav` | clamd host when enabled |
| `CLAMAV_PORT` | `3310` | clamd TCP port |
| `CLAMAV_TIMEOUT` | `00:00:10` | Scanner request timeout |
| `CLAMAV_CHUNK_SIZE_BYTES` | `65536` | INSTREAM chunk size |

These are local-development values only. Production must use managed secrets, separate service identities, restricted networks, its own JWT issuer/audience/signing configuration, and an operated malware-scanning service if scanning is required.

## Start the stack

```bash
docker compose up --build
```

Expected services:

- `document-api`: public API in `ApplicationMode=Api`, without the privileged worker credential;
- `document-worker`: internal-only ingestion process in `ApplicationMode=Worker`;
- Web UI;
- FastAPI integration boundary;
- PostgreSQL with pgvector and forced RLS;
- Redis.

The reference stack does **not** start ClamAV. The default `FILE_THREAT_SCANNING_PROVIDER=Disabled` requires no external scanning service. `/health` reports `fileThreatScanningProvider` so this state is explicit.

The API and Worker share the named `document-storage` volume. The API validates and optionally scans uploads before persistence, then writes pending jobs. The Worker reads accepted files and performs bounded TXT/PDF/DOCX extraction and indexing.

Fresh PostgreSQL volumes initialize:

- `tenants`, `tenant_memberships`, and `tenant_invitations`;
- `documents` with required `owner_id` and `tenant_id`;
- `document_chunks` with tenant identity and pgvector embeddings;
- `document_ingestion_jobs` with tenant identity;
- append-only `audit_events`;
- roles `document_app`, `document_platform`, and `document_privileged`;
- forced lifecycle, document, job, chunk, and audit RLS policies;
- invitation-token, membership, ownership, active-job, claim-order, and vector indexes.

## Local URLs

| Service | URL |
|---|---|
| Web UI | `http://localhost:3000` |
| Swagger UI | `http://localhost:5000/swagger` |
| ASP.NET Core health | `http://localhost:5000/health` |
| ASP.NET Core readiness | `http://localhost:5000/health/ready` |
| FastAPI health | `http://localhost:8000/health` |
| PostgreSQL | `localhost:5432` |
| Redis | `localhost:6379` |
| Worker | No published host port |

## Recommended local demo

```bash
python scripts/demo_flow.py
```

Without `JWT_TOKEN`, the script:

1. creates development PlatformAdmin, tenant Admin, and User tokens;
2. provisions `demo-tenant` when absent;
3. revokes an abandoned pending invitation for `demo-user` if necessary;
4. creates and accepts a new one-time invitation;
5. uploads the sample file;
6. waits for the independent Worker;
7. runs Search and grounded Ask.

Run the dedicated document-format smoke flow against a running stack:

```bash
python scripts/document_format_smoke.py
```

It provisions an isolated local tenant, accepts a membership invitation, uploads real PDF and DOCX fixtures, waits for Worker completion, verifies both files through semantic retrieval, and confirms a spoofed PDF is rejected before enqueue.

Optional identity overrides for the main demo:

```text
DEMO_USER_ID
DEMO_ADMIN_USER_ID
DEMO_TENANT_ID
DEMO_ROLE
```

Set `JWT_TOKEN` only for an already provisioned external subject. The token helper is not an identity provider.

## Safe document upload behavior

Supported pairs are exact:

| Extension | Content type |
|---|---|
| `.txt` | `text/plain` |
| `.pdf` | `application/pdf` |
| `.docx` | `application/vnd.openxmlformats-officedocument.wordprocessingml.document` |

The upload flow is:

1. validate JWT and durable tenant membership;
2. enforce 10 MB upload size, supported extension, supported MIME, and extension/MIME agreement;
3. inspect actual document structure:
   - TXT initial bytes must be UTF-8 and non-binary;
   - PDF must have `%PDF-`, parse successfully with PdfPig, and stay under the page limit;
   - DOCX must be a bounded ZIP/OOXML package containing the expected Word main document part and safe XML;
4. if configured, stream the upload to ClamAV before local storage;
5. persist the accepted file to the shared volume;
6. atomically create document metadata and the pending ingestion job;
7. return `202 Accepted`;
8. Worker re-applies extraction limits and writes semantic chunks only after successful extraction.

Scanned/image-only PDFs return a terminal `ocr-required` processing error. OCR is not silently attempted or bundled.

## Optional ClamAV testing

To use a trusted clamd endpoint, set:

```text
FILE_THREAT_SCANNING_PROVIDER=ClamAv
CLAMAV_HOST=<reachable-clamd-host>
CLAMAV_PORT=3310
CLAMAV_TIMEOUT=00:00:10
```

When `ClamAv` is selected, the API fails closed:

- clean scanner verdict -> upload may continue;
- threat verdict -> HTTP `400` with `malware-detected`;
- timeout/unreachable/unexpected scanner result -> HTTP `503` with `malware-scanner-unavailable`.

Do not enable `ClamAv` merely by changing the setting if no scanner is actually operated. The repository does not manage signature updates, scanner availability, network isolation, or scanner monitoring.

## Manual lifecycle workflow

Generate development tokens:

```bash
PLATFORM_TOKEN=$(python scripts/create_dev_token.py --user platform-admin --tenant platform --role PlatformAdmin)
ADMIN_TOKEN=$(python scripts/create_dev_token.py --user tenant-a-admin --tenant tenant-a --role Admin)
USER_TOKEN=$(python scripts/create_dev_token.py --user user-a --tenant tenant-a --role User)
```

Provision a tenant and initial Admin:

```bash
curl http://localhost:5000/api/platform/tenants \
  -H "Authorization: Bearer $PLATFORM_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"tenantId":"tenant-a","displayName":"Tenant A","initialAdminUserId":"tenant-a-admin"}'
```

Create an invitation:

```bash
curl http://localhost:5000/api/tenant/invitations \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"inviteeUserId":"user-a","role":"User","lifetimeHours":24}'
```

Copy the one-time `token` from the response, then accept it as the invited subject:

```bash
curl http://localhost:5000/api/tenant/invitations/accept \
  -H "Authorization: Bearer $USER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"token":"<one-time-token>"}'
```

Verify the principal and durable state:

```bash
curl http://localhost:5000/api/auth/me \
  -H "Authorization: Bearer $USER_TOKEN"
```

The response includes `tenantManaged`, `tenantActive`, `membershipActive`, and `membershipRole`.

A signed User/Admin token receives `403` before the tenant/membership exists. Removing the membership or disabling the tenant makes the next protected request return `403`.

## Verify PostgreSQL security

```bash
docker compose exec -T postgres psql -U documents -d documents -c "\d+ tenants"
docker compose exec -T postgres psql -U documents -d documents -c "\d+ tenant_memberships"
docker compose exec -T postgres psql -U documents -d documents -c "\d+ tenant_invitations"
docker compose exec -T postgres psql -U documents -d documents -c "SELECT relname, relrowsecurity, relforcerowsecurity FROM pg_class WHERE relname IN ('tenants','tenant_memberships','tenant_invitations','documents','document_chunks','document_ingestion_jobs','audit_events');"
docker compose exec -T postgres psql -U documents -d documents -c "SELECT rolname, rolsuper, rolbypassrls FROM pg_roles WHERE rolname IN ('document_app','document_platform','document_privileged');"
docker compose exec -T postgres psql -U documents -d documents -c "SELECT tablename, policyname, roles FROM pg_policies ORDER BY tablename, policyname;"
```

Expected:

- lifecycle and tenant-data tables have RLS enabled and forced;
- all three application roles are non-superuser and do not have `BYPASSRLS`;
- `document_app` policies depend on transaction-local `app.tenant_id`;
- `document_platform` has lifecycle/cross-tenant read policies but no ingestion mutation grants;
- `document_privileged` supports worker mutation paths.

Verify process credentials:

```bash
docker compose exec -T document-api env | grep ConnectionStrings__Postgres
docker compose exec -T document-worker env | grep ConnectionStrings__Postgres
```

`document-api` must not expose `ConnectionStrings__PostgresPrivileged`. `document-worker` must have it.

## Processing and authorization boundary

The upload request:

1. validates JWT signature, issuer, audience, lifetime, `sub`, `tenant_id`, and role;
2. checks active tenant and durable membership;
3. rejects stale elevated JWT roles that do not match durable membership;
4. derives owner and tenant identity from claims;
5. validates extension/MIME, document signature/package, format safety limits, and optional malware verdict;
6. stores the accepted file on the shared volume;
7. opens a tenant-scoped PostgreSQL transaction;
8. atomically persists document metadata and the initial job;
9. returns `202 Accepted`.

The independent Worker uses the privileged connection, loads persisted tenant/owner state, reads the shared file, re-applies bounded extraction rules, and preserves both identities while writing semantic chunks.

- `User`: active membership plus owner and tenant filters;
- `Admin`: active durable Admin plus tenant filter only;
- `PlatformAdmin`: narrow platform cross-tenant path.

## Run tests

```bash
dotnet test tests/api-dotnet/EnterpriseDocumentAssistant.Api.Tests.csproj --configuration Release
```

PostgreSQL integration tests run when `POSTGRES_TEST_CONNECTION_STRING` is configured. CI verifies:

- direct lifecycle/document RLS behavior;
- invitation digest-only storage and one-time acceptance;
- final-Admin protection;
- cross-tenant write rejection;
- API lifecycle and authorization behavior;
- extension/MIME and real PDF/DOCX inspection boundaries;
- bounded TXT/PDF/DOCX extraction and `ocr-required` behavior;
- optional ClamAV protocol behavior without a real scanner service;
- independent Worker PDF/DOCX processing through the dedicated Compose smoke workflow;
- API/Worker restart persistence;
- credential separation.

## Existing PostgreSQL volumes

The safe document-format milestone adds no database migration. Entrypoint scripts still run only for a fresh volume, so existing databases must not be assumed to have current tenant lifecycle/RLS schema.

For required data:

1. create and verify a backup of PostgreSQL and stored document files;
2. review `zzzz-document-ownership.sql`, `zzzzz-tenant-isolation.sql`, `zzzzzz-audit-observability.sql`, and `zzzzzzz-tenant-lifecycle.sql`;
3. set strong distinct `APP_DB_PASSWORD`, `PLATFORM_DB_PASSWORD`, and `PRIVILEGED_DB_PASSWORD` values;
4. apply scripts with `ON_ERROR_STOP` using an administrator connection;
5. review generated legacy tenant/member mappings and ensure every tenant has an active Admin;
6. verify constraints, grants, roles, policies, invitation indexes, and runtime behavior;
7. deploy separate API and Worker identities;
8. review and configure document-processing safety limits;
9. serve traffic only after negative lifecycle, cross-tenant, and document-format tests pass.

For disposable local data only:

```bash
docker compose down --volumes
docker compose up --build
```

## Troubleshooting

### `401 Unauthorized`

Verify bearer header, signature key, issuer, audience, expiration, and API environment.

### `403 Forbidden`

Check required JWT claims, active tenant, active membership, durable role, and invitation acceptance.

### Upload returns `400 invalid-file-signature` or `invalid-docx-package`

The bytes do not match the declared format or the DOCX package is structurally unsafe/malformed. Renaming a file or changing its MIME header does not bypass inspection.

### PDF processing returns `ocr-required`

The PDF has no extractable text. OCR is intentionally not bundled. Use a text-bearing PDF or add a separately reviewed OCR pipeline.

### Upload returns `503 malware-scanner-unavailable`

`FILE_THREAT_SCANNING_PROVIDER=ClamAv` is enabled but clamd did not produce a trusted clean result. Verify scanner health, host/port, firewall/network rules, timeout, and signature-update operations. Do not switch to permissive behavior merely to bypass an outage.

### Invitation returns `409`

The invitation may already be accepted/revoked/expired, or another non-expired pending invitation exists for the subject. List/revoke the pending invitation and create a new one.

### Final Admin cannot be removed

This is intentional. Promote or invite another Admin first, then remove/downgrade the original Admin.

### A foreign document returns `404`

This is intentional outside the caller's authorized owner or tenant scope.

### Search returns no results

Confirm the document reached `Completed`, the token tenant matches the document, and the durable membership has owner or Admin scope.

### Worker does not process the job

Check:

```bash
docker compose ps document-worker
docker compose logs document-worker
```

Confirm both API and Worker mount `document-storage`, and only Worker has the privileged database connection.

### Runtime database reads return no rows

Confirm the query runs inside a transaction after setting `app.tenant_id`. Missing session context fails closed by design.

### Cross-tenant write fails with an RLS error

This is expected. The runtime role cannot insert or update a row whose `tenant_id` differs from the transaction context.

### PostgreSQL initialization changed after first startup

Initialization scripts do not rerun against existing volumes. Apply reviewed migrations after backup rather than deleting required data.

See [Safe Document Extraction](TEXT_EXTRACTION_PIPELINE.md), [Managed Tenant Lifecycle](TENANT_LIFECYCLE.md), [Authentication and Authorization](AUTHENTICATION_AND_AUTHORIZATION.md), and [Tenant Isolation](TENANT_ISOLATION.md) for the complete security model.
