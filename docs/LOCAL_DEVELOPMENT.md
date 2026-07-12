# Local Development Guide

This guide helps contributors and reviewers run the complete project locally and understand which settings are safe to customize.

## Prerequisites

For the Docker Compose workflow, install:

- Docker Desktop or Docker Engine
- Docker Compose
- Git

The .NET SDK and Python 3.11 or later are only required when running tests or scripts directly on the host machine.

## Environment Setup

Docker Compose includes development defaults, so the stack can start without a local `.env` file. Copy the example file when you need to change host ports or PostgreSQL development credentials.

Linux or macOS:

```bash
cp .env.example .env
```

Windows PowerShell:

```powershell
Copy-Item .env.example .env
```

The available settings are:

| Variable | Default | Purpose |
|---|---:|---|
| `WEB_UI_HOST_PORT` | `3000` | Web UI port exposed on the host |
| `API_HOST_PORT` | `5000` | ASP.NET Core API port exposed on the host |
| `AI_SERVICE_HOST_PORT` | `8000` | FastAPI service port exposed on the host |
| `POSTGRES_HOST_PORT` | `5432` | PostgreSQL port exposed on the host |
| `REDIS_HOST_PORT` | `6379` | Redis port exposed on the host |
| `ASPNETCORE_ENVIRONMENT` | `Development` | ASP.NET Core runtime environment |
| `POSTGRES_DB` | `documents` | Local PostgreSQL database name |
| `POSTGRES_USER` | `documents` | Local PostgreSQL user |
| `POSTGRES_PASSWORD` | `documents` | Local PostgreSQL password |

These values are intended only for local development. Do not reuse the example password or expose the database and Redis ports in a public deployment.

## Run with Docker Compose

```bash
docker compose up --build
```

Expected services:

- Web UI
- ASP.NET Core API
- Python FastAPI AI service
- PostgreSQL
- Redis

PostgreSQL initializes the `documents` table from:

```text
infra/postgres/init/001_documents.sql
```

Uploaded document metadata is stored in PostgreSQL and remains available after the API container restarts.

## Local URLs

With the default `.env.example` values:

| Service | URL |
|---|---|
| Web UI | `http://localhost:3000` |
| ASP.NET Core API | `http://localhost:5000` |
| Swagger UI | `http://localhost:5000/swagger` |
| Python AI service | `http://localhost:8000` |
| PostgreSQL | `localhost:5432` |
| Redis | `localhost:6379` |

When a `*_HOST_PORT` value changes, use the corresponding configured port instead.

## Validate the API

```bash
curl http://localhost:5000/health
```

Open Swagger:

```text
http://localhost:5000/swagger
```

Replace `5000` when `API_HOST_PORT` has been customized.

## Validate the Web UI

Open:

```text
http://localhost:3000
```

Use the UI to:

1. Check API health.
2. Upload a sample text file.
3. Refresh the persistent document list.
4. Run semantic search.
5. Ask a grounded question.
6. Open a returned source in the source viewer.

## Run the .NET Tests

```bash
dotnet test tests/api-dotnet/EnterpriseDocumentAssistant.Api.Tests.csproj
```

The integration tests use the in-memory document repository so they remain isolated from the local PostgreSQL container.

## Suggested Manual Demo

1. Start the services with Docker Compose.
2. Open the Web UI.
3. Check the API health status.
4. Upload `samples/contract-policy.txt`.
5. Refresh the document list.
6. Search for `vendor contract approval process`.
7. Ask `Who needs to approve vendor contracts?`.
8. Select a returned source and inspect the exact chunk.
9. Restart the API container and confirm that the document metadata remains in the list.

## Troubleshooting

### A host port is already in use

Change the relevant value in `.env`. For example:

```dotenv
API_HOST_PORT=5050
POSTGRES_HOST_PORT=55432
```

Then restart the stack:

```bash
docker compose down
docker compose up --build
```

### PostgreSQL settings changed after the first startup

PostgreSQL initialization scripts and initial credentials are applied when the data volume is first created. Changing `POSTGRES_DB`, `POSTGRES_USER`, or `POSTGRES_PASSWORD` does not rewrite an existing volume.

For disposable local data, recreate the volume:

```bash
docker compose down -v
docker compose up --build
```

This command permanently deletes the local PostgreSQL data stored in the Compose volume.

### AI service is not reachable

Check container status and logs:

```bash
docker compose ps
docker compose logs ai-service
```

The API communicates with the AI service through the internal Compose address `http://ai-service:8000`; changing `AI_SERVICE_HOST_PORT` only changes host access.

### File upload fails

Check the API logs and verify that the selected file type and size are supported:

```bash
docker compose logs document-api
```

### Browser cannot reach the API

Confirm that the API health endpoint responds and that the Web UI is using the expected default API host port. When `API_HOST_PORT` is changed, the current static Web UI configuration may also need to be updated.

## Reviewer Checklist

A reviewer should be able to answer:

- What business problem does this project solve?
- What services are included in the architecture?
- How does the document upload flow work?
- Where is document metadata persisted?
- How do semantic search and RAG-style answers work?
- Which local settings are configurable?
- What parts are implemented and what production controls remain planned?
