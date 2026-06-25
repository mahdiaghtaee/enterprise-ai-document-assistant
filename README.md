# Enterprise AI Document Assistant

A small but realistic starter project for building an internal document assistant. The goal is to let a user upload company documents, send them to a separate AI service, and later ask questions with answers grounded in those documents.

The project starts small, but it is structured like a real backend system: a .NET API handles application workflows, while a Python FastAPI service owns AI/document processing concerns.

## Current version

Version 1 includes:

- ASP.NET Core 8 API
- Python FastAPI AI service
- Docker Compose local stack
- PostgreSQL container for future persistence
- Redis container for future background jobs and caching
- Local document upload workflow
- In-memory document metadata repository
- Service-to-service call from .NET API to Python AI service
- Health endpoints for both services

## Architecture

```text
Client
  |
  v
ASP.NET Core API
  |---- Local file storage
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
docs/
  resume-notes.md      Notes for explaining the project in interviews
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

Upload a document:

```bash
curl -X POST http://localhost:5000/api/documents/upload \
  -F "file=@sample-policy.pdf"
```

Create a metadata-only document record:

```bash
curl -X POST http://localhost:5000/api/documents \
  -H "Content-Type: application/json" \
  -d '{"fileName":"sample-policy.pdf","contentType":"application/pdf"}'
```

List documents:

```bash
curl http://localhost:5000/api/documents
```

Index a document directly in the AI service placeholder:

```bash
curl -X POST http://localhost:8000/index \
  -H "Content-Type: application/json" \
  -d '{"file_name":"sample-policy.pdf","content_type":"application/pdf","text":"Initial test document"}'
```

## Notes

The document repository is currently in-memory. That is deliberate for the first version, because the goal is to make the API shape and service boundaries clear before adding database migrations and persistence.

The upload endpoint stores files locally and then asks the AI service to queue the document for indexing. The AI service currently returns a placeholder response. The next version should replace this with parsing, chunking, embeddings and source-aware question answering.

## Next steps

- Store document metadata in PostgreSQL
- Add a background worker for indexing
- Add document parsing in the Python service
- Add embeddings with pgvector
- Add a question-answer endpoint with source references
- Add JWT authentication
- Add integration tests and GitHub Actions
