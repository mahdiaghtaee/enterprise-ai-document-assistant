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

## Local URLs

Docker Compose publishes the services to these host ports:

| Service | URL |
|---|---|
| Web UI | `http://localhost:3000` |
| ASP.NET Core API | `http://localhost:5000` |
| Swagger UI | `http://localhost:5000/swagger` |
| Python AI service | `http://localhost:8000` |
| PostgreSQL | `localhost:5432` |
| Redis | `localhost:6379` |

## Validate the API

After startup, check the health endpoint:

```bash
curl http://localhost:5000/health
```

Then open Swagger/OpenAPI in the browser:

```text
http://localhost:5000/swagger
```

## Validate the Web UI

Open the local UI:

```text
http://localhost:3000
```

Use the UI to:

1. Check API health.
2. Upload a sample text file.
3. Run semantic search.
4. Ask a grounded question and review the returned source chunks.

## Suggested Manual Demo

1. Start the services with Docker Compose.
2. Open the Web UI at `http://localhost:3000`.
3. Check the API health status from the UI.
4. Upload `samples/contract-policy.txt` or `samples/hr-policy.txt`.
5. Search for a business topic such as `vendor contract approval process`.
6. Ask `Who needs to approve vendor contracts?`.
7. Review the answer and source chunks returned by the API.
8. Open Swagger at `http://localhost:5000/swagger` to show the backend endpoints.

## Troubleshooting

### Port already in use

If port `3000`, `5000`, `8000`, `5432`, or `6379` is already in use, stop the conflicting service or adjust the Docker Compose port mapping.

### Database startup delay

PostgreSQL may need a few seconds before accepting connections. Restart the API service if it starts before the database is ready.

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
- How do semantic search and RAG-style answers work?
- What parts are production-ready and what parts are planned?
