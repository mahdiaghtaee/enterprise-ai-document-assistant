# Engineering Case Study

## Enterprise AI Document Assistant

This project explores how document retrieval can be integrated into a conventional backend without depending on a paid model provider or hiding important production gaps.

The current system is intentionally local-first and deterministic. It demonstrates document upload, metadata persistence, extraction, chunking, embeddings, semantic retrieval, and source-aware answers in a form that can be run and tested repeatedly.

## Problem

A useful internal document assistant needs more than a chat endpoint. It needs a repeatable lifecycle:

1. accept and validate a document;
2. store the file and its metadata;
3. extract and split supported text;
4. generate searchable representations;
5. retrieve relevant passages;
6. return an answer with source context.

It also needs persistence, health checks, explicit contracts, tests, operational documentation, and a reproducible development environment.

## Current Implementation

The repository contains:

- an **ASP.NET Core API** that owns the public endpoints, PostgreSQL metadata, local file storage, plain-text extraction, chunking, deterministic embeddings, the in-memory semantic index, and source-aware answer construction;
- a **Python FastAPI service** that currently exposes health and indexing-boundary endpoints for future Python-specific processing;
- **PostgreSQL** for document metadata;
- **Redis** as unused but available infrastructure for future caching or background work;
- a small Web UI and Docker Compose environment.

This distinction is important: the Python service does not yet perform extraction, embedding, retrieval, or answer generation. Those capabilities currently run in .NET.

## Why Start with a Deterministic Pipeline?

The first implementation avoids external model credentials and provider-specific behavior.

Benefits:

- the complete workflow can run locally;
- tests are repeatable;
- reviewers can inspect retrieval behavior directly;
- provider integration remains an explicit later decision.

Trade-off:

- the deterministic embeddings and answer builder demonstrate architecture and data flow, not production model quality.

## Why Keep a FastAPI Boundary?

Python offers useful document and machine-learning libraries. An explicit HTTP boundary allows those capabilities to be introduced later without coupling public API contracts to a particular provider.

The boundary is not free. It adds deployment, networking, failure handling, versioning, and observability concerns. Until Python-specific capabilities justify that cost, the deterministic workflow remains in the .NET API.

## Implemented Workflow

```text
Upload document
    -> save file locally
    -> persist metadata in PostgreSQL
    -> extract supported text in .NET
    -> split text into chunks
    -> generate deterministic embeddings
    -> store chunks in an in-memory index
    -> retrieve ranked chunks
    -> construct an answer with source records
```

The upload flow also calls the FastAPI indexing endpoint to exercise the service boundary. That endpoint currently returns a placeholder queued status.

## Important Trade-offs

### In-memory semantic index

Indexed chunks are not durable across API restarts. The next storage milestone is PostgreSQL with pgvector behind the existing semantic-index abstraction.

### Synchronous processing

The current upload path is suitable for small demonstrations. Production workloads require persisted jobs, background workers, retries, idempotency, processing states, and failure handling.

### Limited extraction

The current implementation supports the documented local text path. PDF parsing, OCR, malware scanning, and format-specific validation are not complete.

### Development infrastructure

Docker Compose exposes local ports and uses development defaults. Production deployments require secret management, restricted networks, TLS, backup, and retention controls.

### No access control

Authentication, authorization, tenant isolation, and audit logging are not implemented. The project must not be used for confidential or regulated documents in its current state.

## Next Engineering Steps

1. Add a pgvector-backed semantic-index provider and persistence tests.
2. Move indexing to background jobs with retries and idempotency.
3. Add authentication, role-based authorization, and workspace isolation.
4. Add a justified provider boundary for external or local models while retaining deterministic test providers.
5. Add OpenTelemetry traces, metrics, structured logs, and audit events.
6. Harden storage, networking, secrets, backup, and retention.
7. Build a retrieval evaluation set before selecting production embedding or language models.

## Engineering Topics Demonstrated

- ASP.NET Core API design and dependency injection
- PostgreSQL metadata persistence
- deterministic retrieval and semantic-index abstractions
- Docker Compose and service-boundary trade-offs
- integration testing and reproducible local workflows
- explicit separation of implemented and planned capabilities
- security and production-readiness analysis

## Review Questions

The project provides concrete material for discussing:

- when a separate service is justified and when a modular application is simpler;
- how to preserve stable API contracts while changing providers;
- why persistence, security, retrieval evaluation, and observability matter as much as model selection;
- how to keep a deterministic test path for systems that may later use external AI providers;
- how to evolve a demonstrable workflow without overstating its readiness.
