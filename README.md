# Enterprise AI Document Assistant

A small but realistic starter project for building an internal document assistant. The goal is to let a user upload company documents, index their content, and ask questions with answers grounded in the uploaded files.

This repository is intentionally kept simple at the first step. The structure is ready for a production-style system, but the implementation starts with a clean API, a separate AI service, and Docker-based local development.

## What this project will include

- ASP.NET Core API for the main backend
- Python FastAPI service for document processing and AI calls
- PostgreSQL for application data
- Redis for background processing and caching
- Docker Compose for local development
- A worker service for document indexing tasks
- Tests and CI in later iterations

## Architecture

```text
Client
  |
  v
ASP.NET Core API
  |---- PostgreSQL
  |---- Redis
  |
  v
Python AI Service
  |
  v
Document parsing / embeddings / question answering
```

## Repository layout

```text
src/
  api-dotnet/          Main ASP.NET Core API
  ai-service-python/   FastAPI AI/document service
  worker-dotnet/       Background worker skeleton
tests/
  api-tests/           API test project placeholder
deploy/
  docker/              Deployment-related files
```

## Local development

Requirements:

- Docker
- .NET 8 SDK
- Python 3.11+

Run the local stack:

```bash
docker compose up --build
```

API endpoints after startup:

```text
ASP.NET Core API: http://localhost:5000
AI Service:       http://localhost:8000
PostgreSQL:       localhost:5432
Redis:            localhost:6379
```

## Current status

Initial scaffold. The next step is implementing document upload in the .NET API and forwarding extracted content to the Python service.

## Planned next steps

- Add document upload endpoint
- Store document metadata in PostgreSQL
- Add document parsing in Python service
- Add embedding storage with pgvector
- Add question-answer endpoint with source references
- Add authentication
- Add integration tests
