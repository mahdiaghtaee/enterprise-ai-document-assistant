# Enterprise AI Document Assistant

A small but realistic starter project for building an internal document assistant. The goal is to let a user register company documents, send them to a separate AI service, and later ask questions with answers grounded in those documents.

The first version is intentionally modest. It focuses on a clean structure, separate services, and a local Docker setup before adding heavier features like authentication, file parsing, embeddings, and RAG.

## Current version

Version 1 includes:

- ASP.NET Core 8 API
- Python FastAPI AI service
- PostgreSQL container for future persistence
- Redis container for future background jobs and caching
- Docker Compose local stack
- A small in-memory document repository
- Health endpoints for both services

## Architecture

```text
Client
  |
  v
ASP.NET Core API
  |---- PostgreSQL     planned persistence
  |---- Redis          planned queue/cache
  |
  v
Python AI Service     planned parsing, chunking and embeddings
```

## Repository layout

```text
src/
  api-dotnet/          Main ASP.NET Core API
  ai-service-python/   FastAPI AI/document service
  worker-dotnet/       Background worker notes for the next version
```

## Run locally

Requirements:

- Docker
- .NET 8 SDK if running the API outside Docker
- Python 3.11+ if running the AI service outside Docker

Start everything:

```bash
docker compose up --build
```

Open these URLs:

```text
ASP.NET Core API: http://localhost:5000/health
Swagger UI:       http://localhost:5000/swagger
AI Service:       http://localhost:8000/health
AI Service docs:  http://localhost:8000/docs
```

## Try the API

Create a document record:

```bash
curl -X POST http://localhost:5000/api/documents \
  -H "Content-Type: application/json" \
  -d '{"fileName":"sample-policy.pdf","contentType":"application/pdf"}'
```

List documents:

```bash
curl http://localhost:5000/api/documents
```

Index a document in the AI service placeholder:

```bash
curl -X POST http://localhost:8000/index \
  -H "Content-Type: application/json" \
  -d '{"file_name":"sample-policy.pdf","content_type":"application/pdf","text":"Initial test document"}'
```

## Notes

The document repository is currently in-memory. That is deliberate for the first version, because the goal is to make the API shape and service boundaries clear before adding database migrations and persistence.

## Next steps

- Add real file upload to the .NET API
- Store document metadata in PostgreSQL
- Add a background worker for indexing
- Add document parsing in the Python service
- Add embeddings with pgvector
- Add a question-answer endpoint with source references
- Add JWT authentication
- Add integration tests and GitHub Actions
