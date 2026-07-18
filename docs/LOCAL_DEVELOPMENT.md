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
- PostgreSQL with the pgvector extension available
- Redis

Fresh PostgreSQL volumes enable the `vector` extension and create the `document_chunks` table with a fixed `vector(8)` embedding column and an HNSW cosine-distance index. The dimension matches the current deterministic embedding generator. The application still uses the in-memory semantic-index provider until the PostgreSQL provider is implemented.

Document metadata is persisted in PostgreSQL. Semantic-index records used by the current API are kept in API memory and are lost when the API process restarts.

## Default Local URLs

| Service | URL |
|---|---|
| Web UI | `http://localhost:3000` |
| Swagger UI | `http://localhost:5000/swagger` |
| ASP.NET Core health | `http://localhost:5000/health` |
| FastAPI health | `http://localhost:8000/health` |
| PostgreSQL | `localhost:5432` |
| Redis | `localhost:6379` |

Use the configured port when a `*_HOST_PORT` value changes.

## Verify the Services

```bash
curl http://localhost:5000/health
curl http://localhost:8000/health
docker compose exec -T postgres psql -U documents -d documents -c "SELECT extversion FROM pg_extension WHERE extname = 'vector';"
docker compose exec -T postgres psql -U documents -d documents -c "\d+ document_chunks"
```

More details are in [PGVECTOR_SCHEMA.md](PGVECTOR_SCHEMA.md).

## Current Processing Boundary

The ASP.NET Core API currently performs upload validation, local storage, metadata persistence, plain-text extraction, chunking, deterministic embedding generation, semantic search, and deterministic source-aware answer construction.

The FastAPI service currently exposes health and placeholder indexing-boundary endpoints. It does not yet perform extraction, embeddings, retrieval, or answer generation.

## Run the Demo

```bash
python scripts/demo_flow.py
```

## Run the .NET Tests

```bash
dotnet test tests/api-dotnet/EnterpriseDocumentAssistant.Api.Tests.csproj --configuration Release
```

The automated tests do not require an external AI provider.

## Manual Verification

1. Start the stack.
2. Open the Web UI.
3. Check both health endpoints and the pgvector schema commands above.
4. Upload `samples/contract-policy.txt`.
5. Refresh the document list.
6. Search for `vendor contract approval process`.
7. Ask `Who needs to approve vendor contracts?`.
8. Inspect the returned source chunk.
9. Restart the API container and confirm that metadata and the database schema remain while the active in-memory semantic index is cleared.

## Troubleshooting

### A host port is already in use

Change the relevant value in `.env` and restart the stack.

### PostgreSQL settings or initialization scripts changed after the first startup

PostgreSQL initialization scripts run only when the data volume is first created. Switching to the pgvector image or changing the vector dimension does not automatically update an existing volume.

For disposable local data:

```bash
docker compose down --volumes
docker compose up --build
```

Do not remove a volume that contains data you need. Back it up and apply a reviewed schema migration instead.

### The API cannot reach PostgreSQL

Check container status and logs. The connection string must use the Compose service name `postgres`, not `localhost`.

### The API cannot reach the FastAPI service

Check the `ai-service` logs. The API uses the internal address `http://ai-service:8000`; changing `AI_SERVICE_HOST_PORT` affects host access only.

### File upload fails

Check API logs and verify that the content type and size are supported. Unsupported formats should fail clearly rather than being presented as indexed.

### Search returns no results after an API restart

This remains expected until the PostgreSQL-backed `ISemanticIndexStore` provider is implemented. The database schema exists, but the active application provider is still in memory.

### The browser cannot reach the API

Confirm that the API health endpoint responds. When `API_HOST_PORT` changes, the current static Web UI configuration may also require an update.
