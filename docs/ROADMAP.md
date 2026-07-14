# Roadmap

This roadmap separates completed capabilities from planned work. A milestone is marked complete only when its implementation, tests, and documentation are present in the repository.

## Completed Foundation

- Docker Compose environment for the API, Web UI, FastAPI service, PostgreSQL, and Redis
- ASP.NET Core health endpoint and Swagger/OpenAPI
- FastAPI health and indexing-boundary endpoints
- local document storage
- PostgreSQL-backed document metadata
- plain-text extraction
- fixed-size chunking with overlap
- deterministic local embeddings
- in-memory semantic index with similarity ranking
- semantic search endpoint
- deterministic source-aware ask endpoint
- sample documents and end-to-end demo script
- .NET integration tests and CI image builds

## Milestone 1 — Persistent Semantic Index

Goal: preserve indexed chunks across process restarts and use PostgreSQL vector similarity when configured.

Tracked by [issue #41](https://github.com/mahdiaghtaee/enterprise-ai-document-assistant/issues/41).

Planned work:

- enable the PostgreSQL `vector` extension;
- add a migration for document chunks and embeddings;
- implement a pgvector-backed `ISemanticIndexStore`;
- keep the in-memory provider for isolated tests;
- select the active provider through configuration;
- add integration tests for persistence, ranking, dimensions, and document isolation;
- document migration and troubleshooting.

## Milestone 2 — Reliable Background Indexing

Goal: remove document processing from the synchronous upload request.

Planned work:

- persist indexing jobs and processing states;
- return a document identifier and status from upload;
- process jobs with a background worker;
- make chunk writes idempotent;
- add bounded retries and a dead-letter path;
- expose processing status and failure details;
- add restart and duplicate-delivery tests.

Redis may be used for coordination only after the job model and persistence requirements are clear.

## Milestone 3 — Identity and Document Authorization

Goal: prevent unauthorized document access.

Planned work:

- authentication;
- role-based authorization;
- workspace or tenant isolation;
- document ownership and access policies;
- authorization checks on upload, list, search, ask, and source retrieval;
- audit events for document access and administrative changes;
- negative security tests.

## Milestone 4 — Provider Integrations

Goal: introduce real embedding and language-model providers without coupling public contracts to a vendor.

Planned work:

- provider interfaces and configuration;
- one local provider and one external provider;
- deterministic fake providers for tests;
- timeout, retry, cancellation, and error mapping;
- cost and token-usage metadata where applicable;
- a retrieval evaluation dataset before provider selection.

Python-specific processing should move to the FastAPI service only when a concrete library or deployment requirement justifies the additional service complexity.

## Milestone 5 — Observability and Operations

Goal: make failures diagnosable and deployments maintainable.

Planned work:

- structured logs with correlation identifiers;
- OpenTelemetry traces across HTTP, database, and background processing;
- metrics for uploads, indexing duration, failures, and retrieval latency;
- readiness and dependency health checks;
- backup and restore documentation;
- retention and deletion workflows;
- deployment profiles with secret management and restricted ports.

## Milestone 6 — Document Format Expansion

Goal: support additional formats safely.

Planned work:

- PDF text extraction;
- optional OCR for scanned documents;
- file-signature validation rather than extension-only checks;
- malware-scanning integration point;
- size, page-count, and extraction-time limits;
- format-specific test fixtures and failure cases.

## Explicitly Deferred

The following work is deferred until persistence, security, and operational foundations are complete:

- multi-tenant billing;
- complex administration dashboards;
- autonomous agents;
- provider-specific optimizations without measured need;
- unsupported claims about production accuracy or scale.
