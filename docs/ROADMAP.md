# Roadmap

This roadmap separates completed capabilities from planned work. A milestone is marked complete only when its implementation, tests, and documentation are present in the repository.

## Completed Foundation

- Docker Compose environment for the API, Web UI, FastAPI service, PostgreSQL, and Redis
- ASP.NET Core health endpoint and Swagger/OpenAPI
- FastAPI health and indexing-boundary endpoints
- local document storage
- PostgreSQL-backed document metadata
- plain-text extraction and fixed-size chunking
- deterministic local embeddings
- semantic search and deterministic source-aware Ask endpoints
- sample documents and end-to-end demo script
- .NET, Python, PostgreSQL, container, CodeQL, and Dependency Review checks

## Milestone 1 — Persistent Semantic Index (Completed in v0.2.0)

Goal: preserve indexed chunks across process restarts and use PostgreSQL vector similarity when configured.

Delivered:

- PostgreSQL `vector` extension initialization;
- persistent `document_chunks` storage with fixed `vector(8)` embeddings;
- HNSW cosine-distance indexing;
- PostgreSQL and in-memory `ISemanticIndexStore` implementations;
- configuration-driven provider selection;
- provider validation and persistence checks across API restart;
- migration, setup, and troubleshooting documentation.

## Milestone 2 — Reliable Background Indexing (Completed in v0.3.0)

Goal: remove processing from the synchronous upload request and provide durable execution, retries, recovery, and status reporting.

Delivered:

- durable ingestion-job storage and constrained lifecycle states;
- atomic document and initial-job persistence;
- ordered transactional claiming with `FOR UPDATE SKIP LOCKED`;
- hosted background extraction, chunking, embedding, and index persistence;
- bounded retries, graceful-shutdown requeue, and abandoned-work recovery;
- `202 Accepted` upload responses and authenticated processing status;
- PostgreSQL lifecycle and runtime persistence tests.

## Milestone 3 — Identity, Ownership, and Tenant Isolation (Completed)

Goal: prevent unauthorized access across users and organizations.

Tracked by completed issue #2 for authentication and document authorization and issue #8 for tenant architecture.

Delivered:

- fail-closed JWT bearer authentication;
- required issuer, audience, signature, expiration, `sub`, `tenant_id`, and role validation;
- tenant-scoped `User` and `Admin` roles;
- explicit cross-tenant `PlatformAdmin` role;
- immutable document ownership and tenant identity derived from JWT claims;
- owner-aware document repositories and retrieval;
- tenant identity persisted on documents, semantic chunks, and ingestion jobs;
- separate non-superuser runtime and privileged PostgreSQL roles;
- forced PostgreSQL Row-Level Security for all tenant data tables;
- transaction-local tenant session context for runtime operations;
- composite tenant/document foreign keys;
- background-processing preservation of tenant and owner identity;
- local tenant-aware token helper, Swagger, Web UI, and demo flow;
- negative API tests for anonymous, missing-claim, cross-user, and cross-tenant access;
- direct PostgreSQL tests for cross-tenant reads, writes, and missing tenant context;
- Compose verification of tenant isolation and persistence across API restart;
- migration and security documentation.

Remaining identity and tenant lifecycle work:

- tenant provisioning and deactivation;
- memberships, invitations, domain verification, and role lifecycle;
- external identity-provider synchronization;
- managed key rotation and token revocation;
- per-tenant quotas, retention, export, and deletion workflows;
- separation of the privileged worker/platform path into an independent trust boundary.

## Milestone 4 — Auditability and Observability

Goal: make security-sensitive activity and failures traceable and operationally diagnosable.

Planned work:

- durable audit events for authentication, document access, upload, status, search, Ask, and administrative actions;
- correlation identifiers propagated across HTTP and background processing;
- structured logs with tenant-safe fields and no bearer tokens or document content;
- OpenTelemetry traces across HTTP, PostgreSQL, and ingestion jobs;
- metrics for uploads, processing duration, retries, failures, authorization denials, and retrieval latency;
- readiness and dependency health checks;
- audit retention, export, and tamper-resistance guidance;
- backup, restore, retention, and deletion operations.

## Milestone 5 — Provider Integrations and Retrieval Evaluation

Goal: introduce real embedding and language-model providers without coupling public contracts to a vendor or reducing testability.

Planned work:

- provider interfaces and configuration;
- one local provider and one external provider;
- deterministic fake providers for tests;
- timeout, retry, cancellation, and error mapping;
- cost and token-usage metadata where applicable;
- a representative retrieval evaluation dataset and repeatable quality metrics;
- provider data-handling and tenant-isolation review.

Python-specific processing should move to FastAPI only when a concrete library or deployment requirement justifies the additional service complexity.

## Milestone 6 — Document Format Expansion

Goal: support additional formats safely.

Planned work:

- PDF and DOCX text extraction;
- optional OCR for scanned documents;
- file-signature validation rather than extension-only checks;
- malware-scanning integration point;
- size, page-count, and extraction-time limits;
- format-specific fixtures and controlled failure cases.

## Explicitly Deferred

The following work is deferred until auditability, secret management, identity lifecycle, and operational foundations are complete:

- multi-tenant billing;
- complex administration dashboards;
- autonomous agents;
- provider-specific optimizations without measured need;
- unsupported claims about production accuracy, confidentiality, or scale.
