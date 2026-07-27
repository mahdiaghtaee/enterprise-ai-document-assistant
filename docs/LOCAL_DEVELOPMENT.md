# Local Development Guide

This guide covers local setup, durable ingestion verification, and troubleshooting for the current implementation.

## Prerequisites

For the Docker Compose workflow, install Docker, Docker Compose v2, and Git. The .NET 8 SDK and Python 3.11 or later are required only when running tests or scripts directly on the host.

## Environment Setup

Docker Compose includes development defaults, so the stack can start without `.env`. Copy the example file when you need to change host ports or PostgreSQL development credentials.

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
| `ASPNETCORE_ENVIRONMENT` | `Development` | ASP.NET Core environment |
| `POSTGRES_DB` | `documents` | Local database name |
| `POSTGRES_USER` | `documents` | Local database user |
| `POSTGRES_PASSWORD` | `documents` | Local database password |

These values are for local development only.

The hosted worker also accepts standard ASP.NET Core environment-variable configuration:

| Variable | Default | Purpose |
|---|---:|---|
| `IngestionWorker__PollInterval` | `00:00:02` | Delay between empty-queue polls |
| `IngestionWorker__RetryDelay` | `00:00:15` | Delay before retrying a transient failure |
| `IngestionWorker__ProcessingTimeout` | `00:10:00` | Maximum processing lease before recovery |
| `IngestionWorker__RecoveryInterval` | `00:01:00` | Interval between abandoned-job recovery scans |

## Start the Stack

```bash
docker compose up --build
```

Expected services:

- Web UI;
- ASP.NET Core API and hosted ingestion worker;
- Python FastAPI service;
- PostgreSQL with pgvector;
- Redis.

Fresh PostgreSQL volumes enable the `vector` extension and initialize:

- `documents` for metadata;
- `document_ingestion_jobs` for durable lifecycle state;
- `document_chunks` for persistent vector records;
- the active-job, claim-order, history, and HNSW indexes.

Restarting only the API container does not remove document metadata, job history, or indexed chunks.

## Default Local URLs

| Service | URL |
|---|---|
| Web UI | `http://localhost:3000` |
| Swagger UI | `http://localhost:5000/swagger` |
| ASP.NET Core health | `http://localhost:5000/health` |
| FastAPI health | `http://localhost:8000/health` |
| PostgreSQL | `localhost:5432` |
| Redis | `localhost:6379` |

## Verify the Services and Schema

```bash
curl http://localhost:5000/health
curl http://localhost:8000/health
docker compose exec -T postgres psql -U documents -d documents -c "SELECT extversion FROM pg_extension WHERE extname = 'vector';"
docker compose exec -T postgres psql -U documents -d documents -c "\d+ document_ingestion_jobs"
docker compose exec -T postgres psql -U documents -d documents -c "\d+ document_chunks"
```

More details are in [BACKGROUND_INGESTION.md](BACKGROUND_INGESTION.md) and [PGVECTOR_SCHEMA.md](PGVECTOR_SCHEMA.md).

## Provider Selection

The semantic-index provider is selected through configuration:

```text
SemanticIndex__Provider=Postgres
```

Supported values are `InMemory` and `Postgres`. When no value is configured, the application defaults to `InMemory`; Docker Compose explicitly selects `Postgres`.

## Current Processing Boundary

The upload request performs validation, local file storage, and atomic document/job persistence. It returns `202 Accepted` without waiting for extraction or indexing.

The ASP.NET Core hosted worker performs:

- job claiming;
- plain-text extraction;
- fixed-size chunking;
- deterministic embedding generation;
- semantic-index persistence;
- retry, completion, terminal failure, and recovery transitions.

The FastAPI service exposes health and placeholder indexing-boundary endpoints. It does not yet perform extraction, embeddings, retrieval, or answer generation.

## Run the Demo

```bash
python scripts/demo_flow.py
```

The script uploads a sample file, polls the processing-status endpoint until completion, runs search, and asks a grounded question.

## Run the .NET Tests

```bash
dotnet test tests/api-dotnet/EnterpriseDocumentAssistant.Api.Tests.csproj --configuration Release
```

PostgreSQL lifecycle tests run when `POSTGRES_TEST_CONNECTION_STRING` is configured.

## Manual Durable-Ingestion Verification

1. Start the stack.
2. Upload `samples/contract-policy.txt`.
3. Read `processingStatusUrl` from the `202 Accepted` response.
4. Poll until the job reaches `Completed`.
5. Search for `vendor contract approval process`.
6. Restart the API only:

   ```bash
   docker compose restart document-api
   ```

7. Wait for `http://localhost:5000/health`.
8. Repeat the search and confirm the same document remains available.
9. Inspect persisted state:

   ```bash
   docker compose exec -T postgres psql -U documents -d documents -c "SELECT id, document_id, status, attempt_count, last_error_code FROM document_ingestion_jobs ORDER BY id;"
   docker compose exec -T postgres psql -U documents -d documents -c "SELECT document_id, chunk_index FROM document_chunks ORDER BY document_id, chunk_index;"
   ```

## Troubleshooting

### A host port is already in use

Change the relevant value in `.env` and restart the stack.

### PostgreSQL initialization changed after first startup

Initialization scripts run only when the data volume is first created.

For disposable local data:

```bash
docker compose down --volumes
docker compose up --build
```

Do not remove a volume containing required data. Back it up and apply the idempotent SQL scripts or a reviewed migration instead.

### A document remains Pending

Check API logs for worker-loop database errors. Verify the API connection string, inspect `available_at`, and confirm the job has attempts remaining.

```bash
docker compose logs document-api
docker compose exec -T postgres psql -U documents -d documents -c "SELECT * FROM document_ingestion_jobs ORDER BY id DESC;"
```

### A document reaches Failed

Read `last_error_code` and `last_error_summary` from the processing-status endpoint. Unsupported or empty document content is terminal; transient infrastructure errors are retried until the attempt limit is reached.

### Search returns no results

Confirm the document job reached `Completed`, `SemanticIndex__Provider` is `Postgres`, and `document_chunks` contains rows.

### The API cannot reach PostgreSQL

The container connection string must use the Compose service name `postgres`, not `localhost`.

### The API cannot reach FastAPI

The internal address is `http://ai-service:8000`; changing the host port does not change this service-to-service URL.

### File upload fails

Check API logs and verify that the content type and size are supported. If database enqueue fails after storage, the API removes the newly stored file.

### The browser cannot reach the API

Confirm that the API health endpoint responds. When `API_HOST_PORT` changes, update the Web UI API base URL stored in browser local storage.
