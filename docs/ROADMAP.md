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

## Milestone 2 — Reliable Background Indexing (Completed in v0.3.0)

Goal: remove document processing from the synchronous upload request and provide durable execution, retries, recovery, and status reporting.

Tracked by completed [issue #5](https://github.com/mahdiaghtaee/enterprise-ai-document-assistant/issues/5).

Delivered:

- durable `document_ingestion_jobs` storage linked to documents;
- constrained `Pending`, `Processing`, `Completed`, and `Failed` states;
- bounded attempt counts, lifecycle timestamps, retry availability, and controlled failure fields;
- one-active-job-per-document enforcement;
- atomic document and initial job persistence;
- cleanup of locally stored files when enqueue persistence fails;
- ordered transactional claiming with PostgreSQL row locking and `SKIP LOCKED`;
- an ASP.NET Core hosted worker for extraction, chunking, embedding generation, and semantic-index writes;
- idempotent semantic-index upserts across retry execution;
- bounded delayed retries and terminal failure after attempt exhaustion;
- graceful-shutdown return to the queue without consuming an attempt;
- abandoned-processing recovery after a configurable timeout;
- `202 Accepted` upload responses with durable job and status links;
- an authenticated processing-status endpoint with controlled failure details;
- PostgreSQL integration tests for atomic persistence, claiming, completion, retry exhaustion, recovery, and latest status retrieval;
- configuration and operations documentation.

## Milestone 3 — Identity and Document Authorization (Partially Delivered)

Goal: prevent unauthorized document access.

Tracked by [issue #2](https://github.com/mahdiaghtaee/enterprise-ai-document-assistant/issues/2) for the delivered authentication and RBAC boundary, with tenant architecture continuing under [issue #8](https://github.com/mahdiaghtaee/enterprise-ai-document-assistant/issues/8).

Delivered:

- fail-closed JWT bearer authentication;
- validated issuer, audience, signature, expiration, and required subject claim;
- `User` and `Admin` roles;
- authentication requirements on document upload, list, search, ask, and processing status;
- document ownership persisted from the authenticated `sub` claim;
- owner-aware PostgreSQL and in-memory document repositories;
- owner-filtered semantic retrieval and source-aware answers;
- administrator access across document owners;
- idempotent ownership migration and legacy-document backfill;
- local development token helper and authenticated Web UI/demo flow;
- negative tests for anonymous requests, malformed authorization context, and cross-user retrieval;
- authenticated Compose verification across API restart.

Remaining work:

- explicit tenant/workspace model;
- tenant-aware authentication context;
- database-enforced tenant isolation across all tables and queries;
- tenant administration policies;
- audit events for document access and administrative changes;
- external identity-provider integration, key rotation, and token revocation;
- expanded negative tests for every tenant-aware operation.

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
- metrics for uploads, indexing duration, failures, authorization denials, and retrieval latency;
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

The following work is deferred until persistence, tenant security, and operational foundations are complete:

- multi-tenant billing;
- complex administration dashboards;
- autonomous agents;
- provider-specific optimizations without measured need;
- unsupported claims about production accuracy or scale.
