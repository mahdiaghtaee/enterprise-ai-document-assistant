# Architecture Overview

Enterprise AI Document Assistant is a local-first reference system for document ingestion, persistent semantic retrieval, and source-aware answers.

The repository contains ASP.NET Core and FastAPI services. The executable document pipeline runs in ASP.NET Core. FastAPI remains a small HTTP boundary for future Python-specific integrations.

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
    |-- write and search chunks through ISemanticIndexStore
    |-- build deterministic source-aware answers
    |
    +--> PostgreSQL + pgvector
    |       - document metadata
    |       - persistent chunks and embeddings
    |       - cosine-distance retrieval
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

Python has a broad ecosystem for document parsing, embeddings, machine learning, and model providers. The HTTP boundary allows those capabilities to be added later without changing the public API contract.

The current implementation does not pretend that this split is already complete. Extraction, chunking, deterministic embeddings, retrieval, and answer construction currently run in .NET.

## Components

### ASP.NET Core API

Current responsibilities:

- public REST endpoints and Swagger/OpenAPI
- upload validation and local file storage
- PostgreSQL-backed document metadata
- plain-text extraction and fixed-size chunking
- deterministic embedding generation
- configurable semantic-index provider selection
- PostgreSQL/pgvector persistence and similarity search in Docker Compose
- in-memory semantic-index provider for isolated tests and lightweight hosts
- deterministic source-aware answer construction
- calls to the FastAPI indexing boundary

Planned responsibilities:

- durable processing states and background job orchestration
- authentication and authorization
- tenant or workspace isolation
- audit logging
- stable provider contracts for external model services

### Python FastAPI Service

Current responsibilities:

- service health endpoint
- indexing-boundary endpoint returning a placeholder queued status

Potential future responsibilities, only when implemented and tested:

- Python-specific document parsing
- external or local embedding providers
- model-provider integration
- specialized retrieval or reranking

### PostgreSQL and pgvector

Current responsibilities:

- document metadata persistence
- persistent document chunks
- eight-dimensional deterministic embeddings
- HNSW cosine-distance index
- vector ranking used by search and ask endpoints

Planned responsibilities:

- document processing states and job records
- users, workspaces, and access-control data
- audit and retention data

### Redis

Redis is part of the stack but is not yet used by the application workflow. Potential uses include:

- short-lived caching
- background indexing coordination
- distributed locks or idempotency records
- transient processing state

### Semantic Index Providers

`ISemanticIndexStore` keeps the public upload, search, and ask flows independent of storage-specific types.

Supported implementations:

- `InMemorySemanticIndexStore`: process-local, deterministic, and suitable for isolated tests;
- `PostgresSemanticIndexStore`: transactional upsert, pgvector cosine search, and persistence across API restarts.

Provider selection is configuration-driven. Docker Compose selects `Postgres`; the default when configuration is absent is `InMemory`.

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
The API extracts text, creates chunks, and generates embeddings
    |
    v
ISemanticIndexStore writes the chunk records
    |
    +--> Compose: PostgreSQL transaction + pgvector column
    +--> Tests/default: in-memory records
    |
    +--> FastAPI indexing boundary is called
    |
    v
Upload response returns metadata and processing summaries
```

## Request Flow: Search

```text
Client submits a query
    |
    v
ASP.NET Core validates and embeds the query
    |
    v
ISemanticIndexStore performs similarity search
    |
    +--> Postgres provider: pgvector cosine distance
    +--> In-memory provider: deterministic cosine calculation
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
A deterministic answer is assembled from source context
    |
    v
The API returns the answer and source records
```

This endpoint demonstrates retrieval and source attribution. It is not a production language-model implementation.

## Persistence and Failure Boundaries

- Chunk upserts are performed inside a PostgreSQL transaction.
- `(document_id, chunk_index)` provides idempotent replacement semantics.
- Chunk rows are deleted when the owning document row is deleted.
- Embedding dimensions and finite numeric values are validated before database access.
- API container restart does not remove pgvector records.
- Removing the PostgreSQL volume removes local metadata and vector records.

## Design Principles

- describe implemented behavior separately from planned behavior
- preserve deterministic execution without external AI credentials
- keep public API contracts independent of pgvector-specific types
- use configuration for provider selection
- verify persistence through an actual container restart
- introduce distributed components only for concrete requirements
- keep source attribution in search and answer responses

## Production Gaps

Before handling sensitive business documents, the system requires:

- authentication and role-based authorization
- tenant or workspace isolation
- asynchronous indexing with retries and idempotency
- secure storage and malware scanning
- secret management and restricted network exposure
- audit events and retention policies
- structured logging, metrics, and distributed tracing
- retrieval evaluation and defenses against unauthorized retrieval or prompt injection

See [SECURITY.md](../SECURITY.md), [PGVECTOR_SCHEMA.md](PGVECTOR_SCHEMA.md), and [ROADMAP.md](ROADMAP.md).
