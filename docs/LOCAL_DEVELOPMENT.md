# Local Development Guide

This guide covers local setup, verification, and troubleshooting for the current implementation.

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

## Start the Stack

```bash
docker compose up --build
```

Expected services:

- Web UI
- ASP.NET Core API
- Python FastAPI service
- PostgreSQL with pgvector
- Redis

The Compose API uses the `Postgres` semantic-index provider. Fresh PostgreSQL volumes enable the `vector` extension and create `document_chunks` with a fixed `vector(8)` column and an HNSW cosine-distance index.

Document metadata and semantic-index records are persisted in PostgreSQL. Restarting only the API container does not remove indexed chunks.

## Default Local URLs

| Service | URL |
|---|---|
| Web UI | `http://localhost:3000` |
| Swagger UI | `http://localhost:5000/swagger` |
| ASP.NET Core health | `http://localhost:5000/health` |
| FastAPI health | `http://localhost:8000/health` |
| PostgreSQL | `localhost:5432` |
| Redis | `localhost:6379` |

## Verify the Services

```bash
curl http://localhost:5000/health
curl http://localhost:8000/health
docker compose exec -T postgres psql -U documents -d documents -c "SELECT extversion FROM pg_extension WHERE extname = 'vector';"
docker compose exec -T postgres psql -U documents -d documents -c "\d+ document_chunks"
```

More details are in [PGVECTOR_SCHEMA.md](PGVECTOR_SCHEMA.md).

## Provider Selection

The semantic-index provider is selected through configuration:

```text
SemanticIndex__Provider=Postgres
```

Supported values are `InMemory` and `Postgres`. When no value is configured, the application defaults to `InMemory`; Docker Compose explicitly selects `Postgres`.

## Current Processing Boundary

The ASP.NET Core API performs upload validation, local storage, metadata persistence, plain-text extraction, chunking, deterministic embedding generation, pgvector persistence and retrieval, and deterministic source-aware answer construction.

The FastAPI service exposes health and placeholder indexing-boundary endpoints. It does not yet perform extraction, embeddings, retrieval, or answer generation.

## Run the Demo

```bash
python scripts/demo_flow.py
```

## Run the .NET Tests

```bash
dotnet test tests/api-dotnet/EnterpriseDocumentAssistant.Api.Tests.csproj --configuration Release
```

## Manual Persistence Verification

1. Start the stack.
2. Upload `samples/contract-policy.txt`.
3. Search for `vendor contract approval process`.
4. Restart the API only:

   ```bash
   docker compose restart document-api
   ```

5. Wait for `http://localhost:5000/health`.
6. Repeat the search and confirm the same document remains available.
7. Inspect stored rows:

   ```bash
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

Do not remove a volume containing required data. Back it up and apply a reviewed migration instead.

### Search returns no results

Confirm that `SemanticIndex__Provider` is `Postgres` in the running API container, the upload response includes an embedding summary, and `document_chunks` contains rows.

### The API cannot reach PostgreSQL

The container connection string must use the Compose service name `postgres`, not `localhost`.

### The API cannot reach FastAPI

The internal address is `http://ai-service:8000`; changing the host port does not change this service-to-service URL.

### File upload fails

Check API logs and verify that the content type and size are supported.

### The browser cannot reach the API

Confirm that the API health endpoint responds. When `API_HOST_PORT` changes, the current static Web UI configuration may also require an update.
