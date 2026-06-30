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

- ASP.NET Core API
- Python FastAPI AI service
- PostgreSQL
- Redis

## Validate the API

After startup, check the health endpoint:

```bash
curl http://localhost:8080/health
```

Then open Swagger/OpenAPI in the browser if the API exposes it in the development environment.

## Suggested Manual Demo

1. Start the services with Docker Compose.
2. Open the API documentation.
3. Check the health endpoint.
4. Upload a sample document.
5. Confirm that metadata is stored.
6. Ask a business question using the planned RAG flow.
7. Review returned sources or expected source references.

## Troubleshooting

### Port already in use

If port `8080`, `8000`, `5432`, or `6379` is already in use, stop the conflicting service or adjust the Docker Compose port mapping.

### Database startup delay

PostgreSQL may need a few seconds before accepting connections. Restart the API service if it starts before the database is ready.

### AI service not reachable

Check that the API uses the same AI service URL as the Docker Compose network name.

### File upload fails

Check the maximum upload size and document storage path in `.env`.

## Client Review Checklist

A reviewer should be able to answer:

- What business problem does this project solve?
- What services are included in the architecture?
- How does the document upload flow work?
- How would semantic search and RAG be added?
- What parts are production-ready and what parts are planned?
