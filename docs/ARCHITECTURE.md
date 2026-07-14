# Architecture Overview

Enterprise AI Document Assistant is a local-first reference system for document ingestion, deterministic semantic retrieval, and source-aware answers.

The repository contains two application services, but the current executable document pipeline runs in the ASP.NET Core API. The FastAPI service is presently a small HTTP boundary for future Python-specific document or model integrations.

## Current High-Level Flow

```text
User / Client
    |
    v
ASP.NET Core API
    |
    |-- validate and store uploaded files
    |-- persist document metadata in PostgreSQL
    |-- extract supported text locally
    |-- split text into chunks
    |-- generate deterministic embeddings
    |-- index and search chunks in memory
    |-- build deterministic source-aware answers
    |
    +--> PostgreSQL
    |       - document metadata
    |
    +--> Redis
    |       - available for future caching and job coordination
    |
    +--> FastAPI service
            - health endpoint
            - indexing-boundary endpoint
            - future Python-specific processing
```

## Why Keep a Python Service Boundary?

Python has a broad ecosystem for document parsing, embeddings, machine learning, and model providers. Keeping an explicit HTTP boundary makes it possible to introduce those capabilities later without changing the public API contract.

The current implementation deliberately avoids pretending that the split is already complete. Extraction, chunking, deterministic embeddings, retrieval, and answer construction currently run in .NET. Moving a responsibility to Python should be justified by a concrete library, deployment, scaling, or ownership requirement.

## Components

### ASP.NET Core API

Current responsibilities:

- public REST endpoints and Swagger/OpenAPI
- upload validation and local file storage
- PostgreSQL-backed document metadata
- plain-text extraction
- fixed-size chunking
- deterministic embedding generation
- in-memory semantic indexing and similarity search
- deterministic source-aware answer construction
- calls to the FastAPI indexing boundary

Planned responsibilities:

- authentication and authorization
- tenant or workspace isolation
- audit logging
- background job orchestration
- stable provider contracts for vector stores and model services

### Python FastAPI Service

Current responsibilities:

- service health endpoint
- indexing-boundary endpoint returning a placeholder queued status

Planned responsibilities, only when implemented and tested:

- Python-specific document parsing
- external or local embedding providers
- model-provider integration
- specialized retrieval or reranking

### PostgreSQL

Current responsibility:

- document metadata persistence

Planned responsibilities:

- document processing state
- persistent document chunks
- pgvector-backed embeddings
- users, workspaces, and access-control data
- conversation and audit data when those features are introduced

### Redis

Redis is part of the local stack but is not yet used by the application workflow. Potential uses include:

- short-lived caching
- background indexing coordination
- distributed locks or idempotency records
- transient processing state

### Semantic Index

The current `ISemanticIndexStore` implementation keeps records in memory. This makes tests and the local demo deterministic, but records do not survive an API restart.

The next storage implementation is planned around PostgreSQL with pgvector while preserving the public API contracts and the in-memory provider for isolated tests.

## Request Flow: Upload

```text
Client uploads a supported document
    |
    v
ASP.NET Core validates and saves the file
    |
    v
Document metadata is inserted into PostgreSQL
    |
    v
The API extracts supported text and creates chunks
    |
    v
The API generates deterministic embeddings
    |
    v
Chunks are written to the in-memory semantic index
    |
    +--> FastAPI indexing boundary is called
    |
    v
Upload response returns metadata and processing summaries
```

The FastAPI call currently confirms the service boundary; it does not perform the local extraction or indexing work.

## Request Flow: Search

```text
Client submits a query
    |
    v
ASP.NET Core validates the request
    |
    v
The deterministic embedding generator embeds the query
    |
    v
The in-memory semantic index calculates similarity scores
    |
    v
The API returns ranked chunks with source metadata
```

## Request Flow: Ask

```text
Client submits a question
    |
    v
The API embeds the question and retrieves relevant chunks
    |
    v
A deterministic answer is assembled from the best source context
    |
    v
The API returns the answer and source records
```

This endpoint demonstrates retrieval and source attribution. It is not a production language-model implementation.

## Design Principles

- describe implemented behavior separately from planned behavior
- preserve deterministic local execution for tests
- keep public API contracts independent of provider-specific types
- introduce distributed components only when a concrete requirement justifies them
- make security and durability limitations visible
- keep source attribution in search and answer responses

## Production Gaps

Before handling sensitive business documents, the system requires:

- authentication and role-based authorization
- tenant or workspace isolation
- persistent vector storage
- asynchronous indexing with retries and idempotency
- secure storage and malware scanning
- secret management and restricted network exposure
- audit events and retention policies
- structured logging, metrics, and distributed tracing
- retrieval evaluation and defenses against unauthorized retrieval or prompt injection

See [SECURITY.md](../SECURITY.md) and [ROADMAP.md](ROADMAP.md) for the current plan.
