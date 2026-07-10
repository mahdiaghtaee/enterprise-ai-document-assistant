# Local Development Guide

This guide helps reviewers and potential freelance clients run the project locally.

## Prerequisites

Install these tools before running the project:

- Docker
- Docker Compose
- .NET SDK
- Python 3.11 or later

## Environment Setup

Create a local environment file:

```bash
cp .env.example .env
```

For local demo usage, the default values in `.env.example` are enough to explain the expected configuration. For production usage, all passwords, storage paths, API URLs, and security settings must be reviewed.

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

| Service | URL |
|---|---|
| Web UI | `http://localhost:3000` |
| ASP.NET Core API | `http://localhost:5000` |
| Swagger UI | `http://localhost:5000/swagger` |
| Python AI service | `http://localhost:8000` |
| PostgreSQL | `localhost:5432` |
| Redis | `localhost:6379` |

## Validate the API

```bash
curl http://localhost:5000/health
```

Open Swagger:

```text
http://localhost:5000/swagger
```

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

## Run the .NET tests

```bash
dotnet test tests/api-dotnet/EnterpriseDocumentAssistant.Api.Tests.csproj
```

The integration tests use the in-memory document repository so they remain isolated from the local PostgreSQL container.

## Suggested Manual Demo

1. Start the services with Docker Compose.
2. Open the Web UI at `http://localhost:3000`.
3. Check the API health status.
4. Upload `samples/contract-policy.txt`.
5. Refresh the document list.
6. Search for `vendor contract approval process`.
7. Ask `Who needs to approve vendor contracts?`.
8. Select a returned source and inspect the exact chunk.
9. Restart the API container and confirm that the document metadata remains in the list.

## Troubleshooting

### Port already in use

If port `3000`, `5000`, `8000`, `5432`, or `6379` is already in use, stop the conflicting service or adjust the Docker Compose port mapping.

### Existing PostgreSQL volume does not contain the table

PostgreSQL initialization scripts only run when the database volume is first created. For local development, recreate the volume:

```bash
docker compose down -v
docker compose up --build
```

This deletes local database data.

### AI service not reachable

Check that the API uses the same AI service URL as the Docker Compose network name.

### File upload fails

Check the maximum upload size and document storage path in `.env`.

### Browser cannot reach the API

The Web UI calls the API at `http://localhost:5000`. Make sure Docker Compose is running and the API health endpoint responds before using upload, search, or ask.

## Client Review Checklist

A reviewer should be able to answer:

- What business problem does this project solve?
- What services are included in the architecture?
- How does the document upload flow work?
- Where is document metadata persisted?
- How do semantic search and RAG-style answers work?
- What parts are production-ready and what parts are planned?
