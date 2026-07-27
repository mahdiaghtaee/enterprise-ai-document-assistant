# Engineering Case Study

## Enterprise AI Document Assistant

This project demonstrates how durable document retrieval can be integrated into a conventional enterprise backend without depending on a paid model provider or hiding important production gaps.

The current system is local-first and deterministic. It combines durable background ingestion, persistent semantic retrieval, and source-aware answers in a form that can be run, failed, recovered, and tested repeatedly.

## Problem

A useful internal document assistant needs more than a chat endpoint. It needs a dependable lifecycle:

1. accept and validate a document;
2. store the file;
3. atomically persist metadata and durable work;
4. process the document outside the HTTP request;
5. retry transient failures and recover abandoned work;
6. expose processing state to clients;
7. retrieve relevant passages from persistent storage;
8. return an answer with source context.

It also needs explicit contracts, integration tests, health checks, security boundaries, operational documentation, and a reproducible development environment.

## Current Implementation

The repository contains:

- an **ASP.NET Core API** that owns public endpoints, upload validation, local file storage, PostgreSQL metadata, durable job creation, processing-status reporting, search, and deterministic source-aware answers;
- an **ASP.NET Core hosted worker** that claims PostgreSQL jobs and performs plain-text extraction, chunking, deterministic embeddings, retries, recovery, and semantic-index persistence;
- a **Python FastAPI service** that currently exposes health and indexing-boundary endpoints for future Python-specific processing;
- **PostgreSQL with pgvector** for document metadata, job history, chunks, embeddings, and cosine retrieval;
- **Redis** as available infrastructure for future caching or justified coordination requirements;
- a small Web UI, Docker Compose environment, CI, CodeQL, and Dependency Review.

The Python service does not currently perform extraction, embedding, retrieval, or answer generation. Those capabilities run in .NET and are documented as such.

## Why Start with a Deterministic Pipeline?

The implementation avoids external model credentials and provider-specific behavior.

Benefits:

- the complete workflow runs locally;
- tests are repeatable;
- job transitions and retrieval behavior can be inspected directly;
- provider integration remains an explicit later decision;
- CI can validate persistence without paid services.

Trade-off:

- the deterministic embeddings and answer builder demonstrate architecture and data flow, not production model quality.

## Why Use PostgreSQL as the Durable Queue?

The project already depends on PostgreSQL for document metadata and vectors. A constrained job table plus transactional row claiming provides a small, inspectable execution model without introducing another broker before a measured requirement exists.

The worker claims one eligible row with `FOR UPDATE SKIP LOCKED`, changes it to `Processing`, and increments the attempt count in the same transaction. Multiple application instances can therefore claim different jobs without processing the same active job concurrently.

This decision has limits. Advanced scheduling, very high throughput, cross-region coordination, and independent worker fleets may eventually justify a dedicated broker. The current design keeps that trade-off explicit.

## Implemented Workflow

```text
Upload document
    -> validate and save file locally
    -> atomically insert document metadata and Pending job
    -> return 202 Accepted with processing-status URL

Hosted worker
    -> claim next available job
    -> extract supported text
    -> split text into chunks
    -> generate deterministic embeddings
    -> transactionally upsert chunks into PostgreSQL/pgvector
    -> mark Completed

Failure path
    -> record controlled error
    -> retry after delay when transient and attempts remain
    -> otherwise mark Failed
    -> recover abandoned Processing jobs after timeout
```

The semantic-index provider remains configuration-driven: isolated tests may use memory, while Docker Compose uses PostgreSQL.

## Reliability Boundaries

### Atomic enqueue

Document metadata and the initial ingestion job are committed together. If the database operation fails after local storage succeeds, the file is deleted to avoid an untracked storage orphan.

### Idempotent indexing

Semantic records use `(document_id, chunk_index)` replacement semantics. Reprocessing the same document does not append duplicate chunk rows.

### Retry and recovery

Transient failures return to `Pending` after a configurable delay. Attempt counts are bounded. Jobs left in `Processing` beyond the configured lease are recovered or failed when no attempts remain.

### Graceful shutdown

An interrupted in-process job returns to `Pending` without consuming an attempt, so a normal host shutdown does not spend the retry budget.

### Client-visible state

Clients receive `202 Accepted` and poll a stable processing-status endpoint. They can distinguish active, completed, failed, and retry-pending work without relying on logs.

## Important Trade-offs

### Limited extraction

The current implementation supports the documented local plain-text path. PDF parsing, DOCX extraction, OCR, malware scanning, and file-signature validation are not complete.

### PostgreSQL-backed worker

The current model is appropriate for a focused reference implementation and moderate workloads. It is not presented as a universal replacement for a dedicated message broker.

### Development infrastructure

Docker Compose exposes local ports and uses development defaults. Production deployments require secret management, restricted networks, TLS, backup, retention, load testing, and failover validation.

### No access control

Authentication, authorization, document ownership, tenant isolation, and audit logging are not implemented. The project must not be used for confidential or regulated documents in its current state.

## Next Engineering Steps

1. Add authentication, role-based authorization, and document ownership.
2. Add tenant or workspace isolation and negative security tests.
3. Add audit events, correlation identifiers, OpenTelemetry traces, metrics, and structured logs.
4. Build a reproducible retrieval-quality evaluation corpus and baseline.
5. Add a justified provider-backed answer generator while retaining the deterministic local mode.
6. Harden storage, networking, secrets, backup, deletion, and retention.
7. Add safe document formats only with format-specific limits and failure tests.

## Engineering Topics Demonstrated

- ASP.NET Core API and hosted-service design;
- PostgreSQL transactions and constrained lifecycle models;
- safe concurrent claiming with `SKIP LOCKED`;
- bounded retry and abandoned-work recovery;
- persistent pgvector retrieval and idempotent upserts;
- configuration-driven provider selection;
- Docker Compose and service-boundary trade-offs;
- integration testing and reproducible local workflows;
- explicit separation of implemented and planned capabilities;
- security and production-readiness analysis.

## Review Questions

The project provides concrete material for discussing:

- when PostgreSQL is sufficient for durable background work and when a broker is justified;
- how to prevent duplicate concurrent processing across instances;
- how cancellation, retry budgets, and recovery should interact;
- how to preserve stable API contracts while changing storage or model providers;
- why persistence, security, retrieval evaluation, and observability matter as much as model selection;
- how to keep a deterministic test path for systems that may later use external AI providers;
- how to evolve a demonstrable workflow without overstating its readiness.
