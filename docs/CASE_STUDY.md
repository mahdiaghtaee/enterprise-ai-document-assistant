# Engineering Case Study

## Enterprise AI Document Assistant

I built this project to explore a practical question: how should a document assistant fit into an existing enterprise backend instead of living as an isolated AI demo?

The result is a small multi-service system that separates business-facing API concerns from document-processing and retrieval concerns. The project is intentionally local-first, so it can be run and reviewed without a paid AI provider.

## Problem

A useful internal document assistant needs more than a chat endpoint. It needs a repeatable document lifecycle:

1. accept a document,
2. validate and register it,
3. extract and split its text,
4. generate searchable representations,
5. retrieve relevant passages,
6. return an answer with source context.

It also needs operational building blocks such as persistence, health checks, service boundaries, testable contracts, and a development environment that another engineer can reproduce.

## My Approach

I divided the system into two application services:

- **ASP.NET Core API** for public endpoints, document metadata, orchestration, and future enterprise concerns such as authentication and audit logging;
- **Python FastAPI service** for extraction, chunking, deterministic embeddings, semantic retrieval, and answer construction.

PostgreSQL stores document metadata. Redis is included as infrastructure for future caching and background indexing. Docker Compose starts the complete local environment, including the Web UI.

## Why I Chose This Architecture

### Keep enterprise workflows in .NET

ASP.NET Core is the part of the stack I would use for long-lived business APIs, access control, integrations, validation, and database-backed workflows. Keeping these concerns in the API avoids coupling business logic to a particular AI library.

### Keep document intelligence replaceable

Python has the broader ecosystem for document and machine-learning tooling. Isolating that work behind a service boundary makes it possible to replace extraction, embedding, retrieval, or model providers without redesigning the public API.

### Start without an external model

The first implementation uses deterministic local embeddings and a deterministic answer path. This was a deliberate trade-off:

- the complete workflow can be run offline;
- tests do not depend on a paid provider;
- reviewers can inspect the retrieval path directly;
- future provider integration remains an explicit engineering step rather than hidden setup.

This version demonstrates architecture and data flow. It does not claim to provide production-grade language-model quality.

## Implemented Workflow

```text
Upload document
    -> store document metadata
    -> extract text
    -> split text into chunks
    -> generate deterministic embeddings
    -> index chunks in memory
    -> retrieve relevant chunks
    -> build an answer with source references
```

The repository currently includes:

- an ASP.NET Core REST API;
- a Python FastAPI processing service;
- PostgreSQL-backed document metadata;
- an in-memory semantic index;
- a simple Web UI;
- Docker Compose infrastructure;
- health endpoints and Swagger documentation;
- integration tests and an end-to-end demo script;
- sample policy and contract documents.

## Important Trade-offs

### In-memory vector index

The current semantic index is intentionally in memory. This keeps the first version understandable, but indexed chunks are not durable across service restarts. The next planned step is a provider abstraction with PostgreSQL and pgvector persistence.

### Synchronous processing

The current upload flow is suitable for a demonstration-sized workload. Large files and production traffic should move extraction and indexing to background workers with explicit processing states, retries, and failure handling.

### Development credentials

Docker Compose uses local development credentials. They are not intended for deployment. A production environment would use secrets management, restricted network exposure, TLS, and separate service identities.

### No authentication yet

Authentication, authorization, tenant isolation, and audit logging are part of the production roadmap. Until those controls exist, the project should be treated as a development reference rather than a system for sensitive documents.

## What I Would Build Next

1. Introduce a vector-store interface and a pgvector implementation.
2. Move document indexing to a background worker with retryable jobs.
3. Add authentication, role-based access control, and workspace isolation.
4. Introduce an LLM-provider interface while retaining the local deterministic provider for tests.
5. Add OpenTelemetry traces across the API, AI service, PostgreSQL, and background jobs.
6. Add audit events for upload, indexing, search, and document access.
7. Add deployment profiles with secret management and restricted service ports.

## Skills Demonstrated

- C# and ASP.NET Core API design
- Python FastAPI service integration
- PostgreSQL persistence
- Docker Compose and multi-service development
- Retrieval and semantic-search workflow design
- API contract and service-boundary design
- Integration testing and reproducible demos
- Technical documentation and architecture trade-off analysis

## Discussion Points for an Interview

This project gives me concrete examples for discussing:

- when to split a system into services and when not to;
- how to keep AI providers behind stable application contracts;
- why retrieval quality, persistence, security, and observability matter as much as model selection;
- how to create a local test path for systems that may later depend on external AI providers;
- how I would evolve a working prototype into a production system without rewriting the entire application.
