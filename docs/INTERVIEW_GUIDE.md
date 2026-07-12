# Interview Guide

## One-minute Explanation

Enterprise AI Document Assistant is a local-first reference project for integrating document retrieval into an enterprise backend. ASP.NET Core owns the public API, orchestration, and document metadata. A Python FastAPI service owns extraction, chunking, deterministic embeddings, semantic retrieval, and source-aware answer construction. PostgreSQL stores document metadata, Redis is included for future caching and background workflows, and Docker Compose runs the complete development environment.

## Why Not Build Everything in Python?

The project is meant to resemble a business system, not only an AI experiment. ASP.NET Core is used for long-lived API contracts, database workflows, validation, and future authentication and authorization. Python remains focused on document and retrieval components. The split makes each side replaceable, but it also introduces network, deployment, and observability concerns that a modular monolith would avoid.

## Why Deterministic Embeddings?

They keep the project runnable without provider credentials or cost and make test behavior repeatable. They are not presented as production-quality embeddings. A real deployment would add a provider abstraction and compare retrieval quality, latency, privacy, and cost before selecting a model.

## Main Limitation

The semantic index is currently in memory. It is suitable for demonstrating the flow but not durable. The planned next step is a vector-store abstraction with PostgreSQL and pgvector.

## Production Changes

Before using the system with business documents, I would add:

- authentication and role-based authorization;
- tenant and workspace isolation;
- secure object storage and malware scanning;
- background indexing with retries and idempotency;
- pgvector or another persistent search backend;
- audit events and document-access logs;
- OpenTelemetry traces, metrics, and structured logs;
- secret management, TLS, network restrictions, backup, and retention policies;
- prompt-injection and unauthorized-retrieval defenses.

## Questions I Expect

### Why services instead of one application?

The split demonstrates a realistic boundary between business APIs and Python AI tooling. For a smaller product team, I would also consider a modular monolith and separate the services only when deployment, scaling, ownership, or library requirements justify it.

### How would you make indexing reliable?

I would persist an indexing job, process it asynchronously, use idempotent chunk writes, store processing states, apply bounded retries, and send repeated failures to a dead-letter path. The upload request should return a document ID and processing status rather than waiting for large files.

### How would you evaluate retrieval?

I would create a representative question-and-document set and measure retrieval relevance, source coverage, ranking quality, latency, and failure cases. Provider decisions should be based on that evaluation rather than model popularity alone.
