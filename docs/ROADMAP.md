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

## Milestone 1 — Persistent Semantic Index (Completed in v0.2.0)

Goal: preserve indexed chunks across process restarts and use PostgreSQL vector similarity when configured.

Tracked by completed [issue #41](https://github.com/mahdiaghtaee/enterprise-ai-document-assistant/issues/41).

Delivered:

- PostgreSQL `vector` extension initialization;
- persistent `document_chunks` storage with fixed `vector(8)` embeddings;
- an HNSW cosine-distance index;
- a PostgreSQL-backed `ISemanticIndexStore` with transactional, idempotent upserts;
- the in-memory provider for isolated tests;
- configuration-driven provider selection;
- provider validation for dimensions, finite values, defaults, and unsupported settings;
- Docker Compose verification that retrieval survives an API-container restart;
- migration, local setup, and troubleshooting documentation.

## Milestone 2 — Reliable Background Indexing (In progress)

Goal: remove document processing from the synchronous upload request.

Tracked by [issue #5](https://github.com/mahdiaghtaee/enterprise-ai-document-assistant/issues/5).

Foundation delivered in v0.2.0:

- durable `document_ingestion_jobs` storage linked to documents;
- constrained `Pending`, `Processing`, `Completed`, and `Failed` states;
- bounded attempt counts, lifecycle timestamps, retry availability, and controlled failure fields;
- one-active-job-per-document enforcement;
- an ordered partial index for pending-job claiming;
- ASP.NET Core state models and focused tests;
- Docker Compose assertions for defaults, constraints, and claim indexes.

Remaining work:

- add a PostgreSQL ingestion-job repository;
- persist a document and its initial job atomically;
- return a document identifier and `Pending` state from upload without waiting for processing;
- claim jobs through a hosted background worker using row locking and `SKIP LOCKED`;
- move extraction, chunking, embedding generation, and semantic-index writes into the worker;
- preserve idempotent chunk writes across retries and duplicate delivery;
- schedule bounded retries and record terminal failure safely;
- expose processing status and controlled failure details;
- add successful-processing, restart-recovery, retry, terminal-failure, and duplicate-delivery integration tests.

Redis may be used for coordination only after the PostgreSQL job model and recovery behavior are proven.

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
