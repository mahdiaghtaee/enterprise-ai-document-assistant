# Local Development Guide

This guide covers authenticated local setup, durable ingestion verification, ownership isolation, and troubleshooting.

## Prerequisites

For Docker Compose, install Docker, Docker Compose v2, Git, and Python 3.11 or later. The .NET 8 SDK is required only when running .NET tests directly on the host.

## Environment setup

Docker Compose includes development defaults, so the stack can start without `.env`. Copy the example file when changing host ports or PostgreSQL development credentials.

Linux or macOS:

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
| `ASPNETCORE_ENVIRONMENT` | `Development` | Loads explicit local JWT configuration |
| `POSTGRES_DB` | `documents` | Local database name |
| `POSTGRES_USER` | `documents` | Local database user |
| `POSTGRES_PASSWORD` | `documents` | Local database password |

These values and the JWT settings in `appsettings.Development.json` are local-development values only.

A non-development deployment must supply its own:

- `Jwt__Issuer`;
- `Jwt__Audience`;
- `Jwt__SigningKey` with at least 32 UTF-8 bytes.

Missing JWT configuration prevents API startup.

The hosted worker also accepts:

| Variable | Default | Purpose |
|---|---:|---|
| `IngestionWorker__PollInterval` | `00:00:02` | Delay between empty-queue polls |
| `IngestionWorker__RetryDelay` | `00:00:15` | Delay before retrying a transient failure |
| `IngestionWorker__ProcessingTimeout` | `00:10:00` | Maximum processing lease before recovery |
| `IngestionWorker__RecoveryInterval` | `00:01:00` | Interval between abandoned-job recovery scans |

## Start the stack

```bash
docker compose up --build
```

Expected services:

- authenticated ASP.NET Core document API and hosted ingestion worker;
- Web UI;
- Python FastAPI service;
- PostgreSQL with pgvector;
- Redis.

Fresh PostgreSQL volumes initialize:

- `documents`, including required `owner_id`;
- `document_ingestion_jobs`;
- `document_chunks`;
- owner/date, active-job, claim-order, history, and HNSW indexes.

Restarting only the API container does not remove ownership, metadata, job history, or indexed chunks.

## Local URLs

| Service | URL |
|---|---|
| Web UI | `http://localhost:3000` |
| Swagger UI | `http://localhost:5000/swagger` |
| ASP.NET Core health | `http://localhost:5000/health` |
| FastAPI health | `http://localhost:8000/health` |
| PostgreSQL | `localhost:5432` |
| Redis | `localhost:6379` |

## Create a development token

Ordinary user:

```bash
python scripts/create_dev_token.py --user demo-user --role User
```

Administrator used only for local authorization verification:

```bash
python scripts/create_dev_token.py --user demo-admin --role Admin
```

Paste the token into Swagger's **Authorize** dialog or the Web UI authentication panel. The helper uses only the Python standard library and is not an identity provider.

Verify the principal:

```bash
TOKEN=$(python scripts/create_dev_token.py --user demo-user --role User)
curl http://localhost:5000/api/auth/me -H "Authorization: Bearer $TOKEN"
```

The public health endpoint does not require a token:

```bash
curl http://localhost:5000/health
curl http://localhost:8000/health
```

## Verify the schema

```bash
docker compose exec -T postgres psql -U documents -d documents -c "\d+ documents"
docker compose exec -T postgres psql -U documents -d documents -c "\d+ document_ingestion_jobs"
docker compose exec -T postgres psql -U documents -d documents -c "\d+ document_chunks"
docker compose exec -T postgres psql -U documents -d documents -c "SELECT extversion FROM pg_extension WHERE extname = 'vector';"
```

More details are in [AUTHENTICATION_AND_AUTHORIZATION.md](AUTHENTICATION_AND_AUTHORIZATION.md), [BACKGROUND_INGESTION.md](BACKGROUND_INGESTION.md), and [PGVECTOR_SCHEMA.md](PGVECTOR_SCHEMA.md).

## Provider selection

Docker Compose selects:

```text
SemanticIndex__Provider=Postgres
```

Supported values are `InMemory` and `Postgres`. Without configuration, isolated application tests use `InMemory`.

## Processing and authorization boundary

The upload request:

1. validates the JWT and document-access policy;
2. derives ownership from `sub`;
3. validates and stores the file;
4. atomically persists document metadata with its owner and the initial job;
5. returns `202 Accepted`.

The hosted worker performs extraction, chunking, deterministic embeddings, semantic-index persistence, retry, completion, failure, and recovery. Ownership is carried from document metadata into index records.

Document list, processing status, Search, Ask, and source text are filtered by owner. An `Admin` token bypasses the owner filter; it does not bypass authentication.

## Run the demo

```bash
python scripts/demo_flow.py
```

When `JWT_TOKEN` is absent, the script creates a short-lived local `User` token. Override the subject with `DEMO_USER_ID` or supply a separately issued token through `JWT_TOKEN`.

The script uploads a sample, polls status, runs owner-filtered search, and asks a grounded question.

## Run tests

```bash
dotnet test tests/api-dotnet/EnterpriseDocumentAssistant.Api.Tests.csproj --configuration Release
```

PostgreSQL lifecycle tests run when `POSTGRES_TEST_CONNECTION_STRING` is configured. CI additionally verifies anonymous rejection, cross-user isolation, administrator visibility, persisted ownership, and retrieval after API restart.

## Manual isolation verification

1. Generate tokens for `user-a`, `user-b`, and an `Admin`.
2. Upload `samples/contract-policy.txt` with the `user-a` token.
3. Poll the returned status URL with `user-a` until `Completed`.
4. Search with `user-a`; the document must be returned.
5. Repeat Search with `user-b`; no chunk from `user-a` may be returned.
6. Search with the `Admin` token; the document may be returned.
7. Restart only the API:

   ```bash
   docker compose restart document-api
   ```

8. Repeat all three searches and confirm the same access boundary.
9. Inspect ownership:

   ```bash
   docker compose exec -T postgres psql -U documents -d documents -c "SELECT id, file_name, owner_id, status FROM documents ORDER BY created_at DESC;"
   ```

## Existing PostgreSQL volumes

PostgreSQL entrypoint scripts run only for a fresh data volume. Existing volumes must not be assumed to have `owner_id`.

For required data:

1. create and verify a backup;
2. review `infra/postgres/init/zzzz-document-ownership.sql`;
3. apply it manually with `ON_ERROR_STOP`;
4. confirm all rows have nonblank owners;
5. deploy the authenticated API.

For disposable local data only:

```bash
docker compose down --volumes
docker compose up --build
```

## Troubleshooting

### `401 Unauthorized`

Confirm the header uses `Authorization: Bearer <token>`. Verify signature key, issuer, audience, expiration, and API environment.

### `403 Forbidden`

The token may be valid but missing `sub` or an approved `User`/`Admin` role.

### A foreign status URL returns `404`

This is intentional for ordinary users. The API does not reveal whether another owner's document exists.

### Search returns no results

Confirm the current subject owns the document, the job reached `Completed`, the configured provider is `Postgres`, and `document_chunks` contains rows. Test with an `Admin` token only when diagnosing ownership.

### A document remains `Pending` or becomes `Failed`

Inspect API logs and the ingestion table:

```bash
docker compose logs document-api
docker compose exec -T postgres psql -U documents -d documents -c "SELECT * FROM document_ingestion_jobs ORDER BY id DESC;"
```

### PostgreSQL initialization changed after first startup

Initialization scripts do not rerun against an existing volume. Apply reviewed migrations after backup rather than deleting required data.

### A host port is already in use

Change the relevant `.env` value and restart the stack.

### The browser cannot reach the API

Confirm the health endpoint responds. When `API_HOST_PORT` changes, update the Web UI API base URL stored in browser local storage.
