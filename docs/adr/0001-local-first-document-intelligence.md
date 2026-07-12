# ADR 0001: Use a Local-First Document Intelligence Pipeline

- Status: Accepted
- Date: 2026-07-12

## Context

The project needs an end-to-end document workflow that other developers can run, inspect, and test without creating accounts with external AI providers.

Using a hosted embedding or language-model API in the first version would make the demo more realistic in one area, but it would also introduce credentials, cost, network dependencies, nondeterministic test behavior, and provider-specific code before the application boundaries were stable.

The project also needs to show how an ASP.NET Core business API can work with Python-based document-processing components without making either service responsible for every concern.

## Decision

The initial version will use:

- deterministic local embeddings;
- an in-memory semantic index;
- deterministic answer construction with source references;
- ASP.NET Core for public API and document metadata concerns;
- Python FastAPI for document processing and retrieval concerns;
- Docker Compose for a reproducible local environment.

External model providers and persistent vector stores will be added behind explicit interfaces rather than being embedded directly into controllers or public API contracts.

## Consequences

### Positive

- The complete workflow is runnable without paid services.
- Integration tests remain deterministic.
- New contributors can understand the data flow before configuring providers.
- Service responsibilities are visible and independently replaceable.
- The project has a stable baseline for comparing future provider implementations.

### Negative

- Retrieval quality is limited by the simple local embedding strategy.
- The in-memory index is not durable across restarts.
- The answer path does not demonstrate production language-model behavior.
- Performance characteristics do not represent a production vector database.

## Follow-up Decisions

Future ADRs should cover:

- selection of the first persistent vector store;
- background indexing and retry semantics;
- authentication and tenant isolation;
- the external or local LLM provider abstraction;
- observability and audit-event design.
