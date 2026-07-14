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
- PostgreSQL
- Redis

Document metadata is persisted in PostgreSQL. Semantic-index records are kept in API memory and are lost when the API process restarts.

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
```

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
3. Check both health endpoints.
4. Upload `samples/contract-policy.txt`.
5. Refresh the document list.
6. Search for `vendor contract approval process`.
7. Ask `Who needs to approve vendor contracts?`.
8. Inspect the returned source chunk.
9. Restart the API container and confirm that metadata remains while the in-memory semantic index is cleared.

## Troubleshooting

### A host port is already in use

Change the relevant value in `.env` and restart the stack.

### PostgreSQL settings changed after the first startup

Initial database settings are applied when the data volume is first created. Recreate only disposable local data when you need the initialization scripts to run again.

### The API cannot reach PostgreSQL

Check container status and logs. The connection string must use the Compose service name `postgres`, not `localhost`.

### The API cannot reach the FastAPI service

Check the `ai-service` logs. The API uses the internal address `http://ai-service:8000`; changing `AI_SERVICE_HOST_PORT` affects host access only.

### File upload fails

Check API logs and verify that the content type and size are supported. Unsupported formats should fail clearly rather than being presented as indexed.

### Search returns no results after an API restart

This is expected because the semantic index is in memory. Re-upload the document or implement the pgvector milestone tracked in issue #41.

### The browser cannot reach the API

Confirm that the API health endpoint responds. When `API_HOST_PORT` changes, the current static Web UI configuration may also require an update.
