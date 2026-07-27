# Architecture Overview

Enterprise AI Document Assistant is a local-first reference system for durable document ingestion, persistent semantic retrieval, and source-aware answers.

The repository contains ASP.NET Core and FastAPI services. The executable document pipeline runs in an ASP.NET Core hosted worker. FastAPI remains a small HTTP boundary for future Python-specific integrations.

## Current High-Level Flow

```text
User / Client
    |
    v
ASP.NET Core API
    |
    |-- validate and store uploaded files
    |-- atomically persist document metadata and a Pending job
    |-- return 202 Accepted with a processing-status URL
    |
    +--> PostgreSQL + pgvector
    |       - document metadata
    |       - durable ingestion jobs
    |       - persistent chunks and embeddings
    |       - cosine-distance retrieval
    |
    +--> ASP.NET Core hosted worker
    |       - claim jobs with FOR UPDATE SKIP LOCKED
    |       - extract supported text locally
    |       - split text into chunks
    |       - generate deterministic embeddings
    |       - write chunks through ISemanticIndexStore
    |       - retry, recover, complete, or fail jobs
    |
    +--> Redis
    |       - available for future caching or coordination
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

### ASP.NET Core API and Worker

Current responsibilities:

- public REST endpoints and Swagger/OpenAPI;
- upload validation and local file storage;
- atomic PostgreSQL document and initial job persistence;
- `202 Accepted` upload responses with job and status links;
- hosted background job claiming and execution;
- bounded retry scheduling and terminal failure handling;
- abandoned-job recovery after a processing timeout;
- graceful-shutdown return to the queue;
- public processing-status reporting;
- PostgreSQL-backed document metadata;
- plain-text extraction and fixed-size chunking;
- deterministic embedding generation;
- configurable semantic-index provider selection;
- PostgreSQL/pgvector persistence and similarity search in Docker Compose;
- in-memory semantic-index provider for isolated tests and lightweight hosts;
- deterministic source-aware answer construction.

Planned responsibilities:

- authentication and authorization;
- tenant or workspace isolation;
- audit logging;
- structured operational telemetry;
- stable provider contracts for external model services.

### Python FastAPI Service

Current responsibilities:

- service health endpoint;
- indexing-boundary endpoint returning a placeholder queued status.

Potential future responsibilities, only when implemented and tested:

- Python-specific document parsing;
- external or local embedding providers;
- model-provider integration;
- specialized retrieval or reranking.

### PostgreSQL and pgvector

Current responsibilities:

- document metadata persistence;
- durable ingestion jobs and lifecycle state;
- one-active-job-per-document enforcement;
- ordered pending-job claim indexes;
- persistent document chunks;
- eight-dimensional deterministic embeddings;
- HNSW cosine-distance index;
- vector ranking used by search and ask endpoints.

Planned responsibilities:

- users, workspaces, and access-control data;
- audit and retention data.

### Redis

Redis is part of the stack but is not yet used by the application workflow. Potential uses include:

- short-lived caching;
- coordination that cannot be expressed safely through the PostgreSQL job model;
- distributed rate limiting;
- transient operational state.

The current durable queue intentionally remains PostgreSQL-backed until a measured requirement justifies another coordination system.

### Semantic Index Providers

`ISemanticIndexStore` keeps ingestion, search, and ask flows independent of storage-specific types.

Supported implementations:

- `InMemorySemanticIndexStore`: process-local, deterministic, and suitable for isolated tests;
- `PostgresSemanticIndexStore`: transactional upsert, pgvector cosine search, and persistence across API restarts.

Provider selection is configuration-driven. Docker Compose selects `Postgres`; the default when configuration is absent is `InMemory`.

## Request Flow: Upload and Enqueue

```text
Client uploads a supported document
    |
    v
ASP.NET Core validates and saves the file
    |
    v
One PostgreSQL transaction inserts:
    - document metadata
    - initial Pending ingestion job
    |
    +--> failure: rollback database rows and remove the stored file
    |
    v
API returns 202 Accepted with document ID, job ID, and status URL
```

The upload request does not wait for extraction, chunking, embeddings, or semantic-index writes.

## Worker Flow: Claim and Process

```text
Hosted worker polls for available Pending jobs
    |
    v
SELECT candidate FOR UPDATE SKIP LOCKED
    |
    v
Atomically set Processing and increment attempt_count
    |
    v
Load document metadata and stored file
    |
    v
Extract -> Chunk -> Embed -> ISemanticIndexStore.UpsertAsync
    |
    +--> success: Completed + document status indexed
    +--> retryable failure: Pending with delayed available_at
    +--> permanent/exhausted failure: Failed
```

`SKIP LOCKED` allows multiple API instances to claim separate jobs without waiting on one another or processing the same active job concurrently.

## Recovery Flow

The worker periodically identifies `Processing` rows whose `started_at` is older than the configured processing timeout.

- if attempts remain, the job returns to `Pending` with `worker-timeout` details;
- if the attempt limit is exhausted, the job becomes `Failed`;
- graceful shutdown returns the interrupted job to `Pending` and restores the consumed attempt.

## Request Flow: Processing Status

```text
Client requests /api/documents/{documentId}/processing-status
    |
    v
API loads the latest job for the document
    |
    v
Response includes state, attempts, lifecycle timestamps, controlled errors, and terminal flag
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

- Document metadata and the initial job are inserted in one transaction.
- Failed enqueue persistence removes the locally stored file.
- Job claims move the selected row to `Processing` in the same transaction as row selection.
- A partial unique index prevents multiple active jobs for one document.
- Chunk upserts are performed inside a PostgreSQL transaction.
- `(document_id, chunk_index)` provides idempotent replacement semantics across retry execution.
- Chunk rows are deleted when the owning document row is deleted.
- Embedding dimensions and finite numeric values are validated before database access.
- API container restart does not remove job history or pgvector records.
- Removing the PostgreSQL volume removes local metadata, job history, and vector records.

## Design Principles

- describe implemented behavior separately from planned behavior;
- preserve deterministic execution without external AI credentials;
- keep public API contracts independent of pgvector-specific types;
- use configuration for provider and worker settings;
- prefer durable PostgreSQL state before adding distributed coordination components;
- verify persistence and retry behavior through integration tests;
- keep source attribution in search and answer responses.

## Production Gaps

Before handling sensitive business documents, the system requires:

- authentication and role-based authorization;
- tenant or workspace isolation;
- secure storage and malware scanning;
- secret management and restricted network exposure;
- audit events and retention policies;
- structured logging, metrics, and distributed tracing;
- retrieval evaluation and defenses against unauthorized retrieval or prompt injection;
- operational load, failover, and capacity validation.

See [SECURITY.md](../SECURITY.md), [BACKGROUND_INGESTION.md](BACKGROUND_INGESTION.md), [PGVECTOR_SCHEMA.md](PGVECTOR_SCHEMA.md), and [ROADMAP.md](ROADMAP.md).
