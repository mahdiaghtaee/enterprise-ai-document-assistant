# Local Development Guide

This guide covers tenant-aware authentication, durable ingestion, PostgreSQL Row-Level Security, and isolation verification.

## Prerequisites

For Docker Compose, install Docker, Docker Compose v2, Git, and Python 3.11 or later. The .NET 8 SDK is required only when running .NET tests directly on the host.

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
| `API_HOST_PORT` | `5000` | ASP.NET Core API host port |
| `AI_SERVICE_HOST_PORT` | `8000` | FastAPI host port |
| `POSTGRES_HOST_PORT` | `5432` | PostgreSQL host port |
| `REDIS_HOST_PORT` | `6379` | Redis host port |
| `ASPNETCORE_ENVIRONMENT` | `Development` | Loads local JWT configuration |
| `POSTGRES_DB` | `documents` | Local database name |
| `POSTGRES_USER` | `documents` | Initialization administrator |
| `POSTGRES_PASSWORD` | `documents` | Initialization administrator password |
| `APP_DB_PASSWORD` | `document-app-local` | RLS-restricted API role password |
| `PRIVILEGED_DB_PASSWORD` | `document-privileged-local` | Worker/platform role password |

These are local-development values only. A deployment must supply managed secrets and its own JWT issuer, audience, and signing configuration.

## Start the stack

```bash
docker compose up --build
```

Expected services:

- tenant-aware ASP.NET Core API and hosted ingestion worker;
- Web UI;
- FastAPI integration boundary;
- PostgreSQL with pgvector and forced RLS;
- Redis.

Fresh PostgreSQL volumes initialize:

- `documents` with required `owner_id` and `tenant_id`;
- `document_chunks` with tenant identity and pgvector embeddings;
- `document_ingestion_jobs` with tenant identity;
- runtime roles `document_app` and `document_privileged`;
- tenant and privileged RLS policies;
- ownership, tenant, active-job, claim-order, and vector indexes.

## Local URLs

| Service | URL |
|---|---|
| Web UI | `http://localhost:3000` |
| Swagger UI | `http://localhost:5000/swagger` |
| ASP.NET Core health | `http://localhost:5000/health` |
| FastAPI health | `http://localhost:8000/health` |
| PostgreSQL | `localhost:5432` |
| Redis | `localhost:6379` |

## Create development tokens

User token:

```bash
python scripts/create_dev_token.py --user user-a --tenant tenant-a --role User
```

Tenant administrator:

```bash
python scripts/create_dev_token.py --user tenant-a-admin --tenant tenant-a --role Admin
```

Platform administrator used only for explicit local cross-tenant tests:

```bash
python scripts/create_dev_token.py --user platform-admin --tenant platform --role PlatformAdmin
```

Paste a token into Swagger or the Web UI. The helper is not an identity provider.

Verify the principal:

```bash
TOKEN=$(python scripts/create_dev_token.py --user user-a --tenant tenant-a --role User)
curl http://localhost:5000/api/auth/me -H "Authorization: Bearer $TOKEN"
```

The response includes `userId`, `tenantId`, roles, tenant-wide owner access, and cross-tenant access state.

## Verify PostgreSQL security

```bash
docker compose exec -T postgres psql -U documents -d documents -c "\d+ documents"
docker compose exec -T postgres psql -U documents -d documents -c "\d+ document_chunks"
docker compose exec -T postgres psql -U documents -d documents -c "\d+ document_ingestion_jobs"
docker compose exec -T postgres psql -U documents -d documents -c "SELECT relname, relrowsecurity, relforcerowsecurity FROM pg_class WHERE relname IN ('documents','document_chunks','document_ingestion_jobs');"
docker compose exec -T postgres psql -U documents -d documents -c "SELECT rolname, rolsuper, rolbypassrls FROM pg_roles WHERE rolname IN ('document_app','document_privileged');"
docker compose exec -T postgres psql -U documents -d documents -c "SELECT tablename, policyname, roles FROM pg_policies WHERE tablename IN ('documents','document_chunks','document_ingestion_jobs') ORDER BY tablename, policyname;"
```

Expected:

- all three tables have RLS enabled and forced;
- both runtime roles are non-superuser and do not have `BYPASSRLS`;
- each table has a tenant policy and a privileged policy.

## Processing and authorization boundary

The upload request:

1. validates JWT signature, issuer, audience, lifetime, `sub`, `tenant_id`, and role;
2. derives owner and tenant identity from claims;
3. validates and stores the file;
4. opens a tenant-scoped PostgreSQL transaction;
5. atomically persists document metadata and the initial job;
6. returns `202 Accepted`.

The hosted worker uses the privileged connection, loads the persisted tenant and owner, and preserves both while writing semantic chunks. Runtime listing, status, Search, and Ask set a transaction-local `app.tenant_id` before querying RLS-protected tables.

- `User`: owner and tenant filters;
- `Admin`: tenant filter only;
- `PlatformAdmin`: privileged cross-tenant path.

## Run the demo

```bash
python scripts/demo_flow.py
```

Without `JWT_TOKEN`, the script creates a short-lived local token. Override identity with:

```text
DEMO_USER_ID
DEMO_TENANT_ID
DEMO_ROLE
```

## Run tests

```bash
dotnet test tests/api-dotnet/EnterpriseDocumentAssistant.Api.Tests.csproj --configuration Release
```

PostgreSQL integration tests run when `POSTGRES_TEST_CONNECTION_STRING` is configured. CI verifies direct RLS behavior, API cross-tenant isolation, schema policies, and persistence across restart.

## Manual tenant-isolation verification

1. Generate `user-a` and `tenant-a-admin` tokens for `tenant-a`.
2. Generate `user-b` and `tenant-b-admin` tokens for `tenant-b`.
3. Upload a sample with `user-a` and wait for `Completed`.
4. Search with `user-a`; the document must appear.
5. Search with another ordinary user in `tenant-a`; the document must not appear.
6. Search with `tenant-a-admin`; the document may appear.
7. Search with `tenant-b-admin`; the document must not appear.
8. Search with `PlatformAdmin`; the document may appear.
9. Restart only the API and repeat the checks.
10. Inspect stored tenant identity:

```bash
docker compose exec -T postgres psql -U documents -d documents -c "SELECT id, file_name, tenant_id, owner_id, status FROM documents ORDER BY created_at DESC;"
docker compose exec -T postgres psql -U documents -d documents -c "SELECT document_id, tenant_id, chunk_index FROM document_chunks ORDER BY document_id, chunk_index;"
docker compose exec -T postgres psql -U documents -d documents -c "SELECT document_id, tenant_id, status FROM document_ingestion_jobs ORDER BY id;"
```

## Existing PostgreSQL volumes

Entrypoint scripts run only for a fresh volume. Existing databases must not be assumed to have tenant columns, roles, or RLS policies.

For required data:

1. create and verify a backup;
2. review `zzzz-document-ownership.sql` and `zzzzz-tenant-isolation.sql`;
3. set strong `APP_DB_PASSWORD` and `PRIVILEGED_DB_PASSWORD` environment variables;
4. apply scripts with `ON_ERROR_STOP`;
5. replace `legacy-tenant` with reviewed real tenant mappings;
6. verify constraints, roles, policies, and runtime behavior;
7. deploy the API only after negative cross-tenant tests pass.

For disposable local data only:

```bash
docker compose down --volumes
docker compose up --build
```

## Troubleshooting

### `401 Unauthorized`

Verify the bearer header, signature key, issuer, audience, expiration, and API environment.

### `403 Forbidden`

The token may be missing `sub`, `tenant_id`, or a supported role.

### A foreign document returns `404`

This is intentional outside the caller's authorized owner or tenant scope.

### Search returns no results

Confirm the document reached `Completed`, the token tenant matches the document, and the user has owner access or the appropriate tenant/platform administrator role.

### Runtime database reads return no rows

Confirm the query runs inside a transaction after setting `app.tenant_id`. Missing session context fails closed by design.

### Cross-tenant write fails with an RLS error

This is expected. The runtime role cannot insert or update a row whose `tenant_id` differs from the transaction context.

### PostgreSQL initialization changed after first startup

Initialization scripts do not rerun against existing volumes. Apply reviewed migrations after backup rather than deleting required data.

See [Authentication and Authorization](AUTHENTICATION_AND_AUTHORIZATION.md) and [Tenant Isolation](TENANT_ISOLATION.md) for the complete security model.
