# Local Development Guide

This guide covers managed tenant provisioning, durable membership authorization, split API/worker processing, PostgreSQL Row-Level Security, and isolation verification.

## Prerequisites

For Docker Compose, install Docker, Docker Compose v2, Git, and Python 3.11 or later. The .NET 8 SDK is required only when running .NET tests or evaluation commands directly on the host.

## Environment setup

Copy the local environment template when changing ports or development credentials:

```bash
cp .env.example .env
```

Windows PowerShell:

```powershell
Copy-Item .env.example .env
```

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

These are local-development values only. Production must use managed secrets, separate service identities, restricted networks, and its own JWT issuer/audience/signing configuration.

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

The API and Worker share the named `document-storage` volume. The API writes uploaded files and pending jobs; the Worker reads those files and performs extraction/indexing.

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

Optional identity overrides:

```text
DEMO_USER_ID
DEMO_ADMIN_USER_ID
DEMO_TENANT_ID
DEMO_ROLE
```

Set `JWT_TOKEN` only for an already provisioned external subject. The token helper is not an identity provider.

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
5. validates and stores the file on the shared volume;
6. opens a tenant-scoped PostgreSQL transaction;
7. atomically persists document metadata and the initial job;
8. returns `202 Accepted`.

The independent Worker uses the privileged connection, loads persisted tenant/owner state, reads the shared file, and preserves both identities while writing semantic chunks.

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
- independent Worker processing and shared storage;
- API/Worker restart persistence;
- credential separation.

## Manual tenant-isolation verification

1. Provision `tenant-a` and `tenant-b` with different initial Admins.
2. Invite and accept `user-a` in `tenant-a` and `user-b` in `tenant-b`.
3. Upload a sample with `user-a` and wait for `Completed`.
4. Search with `user-a`; the document must appear.
5. Search with another ordinary member in `tenant-a`; the document must not appear.
6. Search with the durable `tenant-a` Admin; the document may appear.
7. Search with the `tenant-b` Admin; the document must not appear.
8. Search with PlatformAdmin; the document may appear through the platform read path.
9. Remove a member and verify immediate `403`.
10. Disable a tenant and verify all non-platform members receive `403`.
11. Restart both API and Worker and repeat persistence checks.
12. Inspect stored state:

```bash
docker compose exec -T postgres psql -U documents -d documents -c "SELECT tenant_id, display_name, status FROM tenants ORDER BY tenant_id;"
docker compose exec -T postgres psql -U documents -d documents -c "SELECT tenant_id, user_id, role, status FROM tenant_memberships ORDER BY tenant_id, user_id;"
docker compose exec -T postgres psql -U documents -d documents -c "SELECT id, tenant_id, invitee_user_id, role, status, length(token_hash) FROM tenant_invitations ORDER BY created_at;"
docker compose exec -T postgres psql -U documents -d documents -c "SELECT id, file_name, tenant_id, owner_id, status FROM documents ORDER BY created_at DESC;"
docker compose exec -T postgres psql -U documents -d documents -c "SELECT document_id, tenant_id, chunk_index FROM document_chunks ORDER BY document_id, chunk_index;"
docker compose exec -T postgres psql -U documents -d documents -c "SELECT document_id, tenant_id, status FROM document_ingestion_jobs ORDER BY id;"
```

## Existing PostgreSQL volumes

Entrypoint scripts run only for a fresh volume. Existing databases must not be assumed to have lifecycle tables, platform roles, or current RLS policies.

For required data:

1. create and verify a backup of PostgreSQL and stored document files;
2. review `zzzz-document-ownership.sql`, `zzzzz-tenant-isolation.sql`, `zzzzzz-audit-observability.sql`, and `zzzzzzz-tenant-lifecycle.sql`;
3. set strong distinct `APP_DB_PASSWORD`, `PLATFORM_DB_PASSWORD`, and `PRIVILEGED_DB_PASSWORD` values;
4. apply scripts with `ON_ERROR_STOP` using an administrator connection;
5. review generated legacy tenant/member mappings and ensure every tenant has an active Admin;
6. verify constraints, grants, roles, policies, invitation indexes, and runtime behavior;
7. deploy separate API and Worker identities;
8. serve traffic only after negative lifecycle and cross-tenant tests pass.

For disposable local data only:

```bash
docker compose down --volumes
docker compose up --build
```

## Troubleshooting

### `401 Unauthorized`

Verify bearer header, signature key, issuer, audience, expiration, and API environment.

### `403 Forbidden`

Check all of the following:

- required JWT claims and supported role;
- tenant exists and is `Active`;
- membership exists and is `Active`;
- JWT Admin claim matches a durable Admin membership;
- invitation was accepted by the matching subject.

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

See [Managed Tenant Lifecycle](TENANT_LIFECYCLE.md), [Authentication and Authorization](AUTHENTICATION_AND_AUTHORIZATION.md), and [Tenant Isolation](TENANT_ISOLATION.md) for the complete security model.
