# Roadmap

This roadmap separates completed capabilities from planned work. A milestone is marked complete only when its implementation, tests, and documentation are present in the repository.

## Completed Foundation

- Docker Compose environment for the API, Web UI, FastAPI service, PostgreSQL, and Redis
- ASP.NET Core and FastAPI health endpoints
- local document storage
- PostgreSQL-backed document metadata
- plain-text extraction and fixed-size chunking
- deterministic local embeddings
- semantic search and grounded Ask endpoints
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
- negative API and database tests for cross-user and cross-tenant access;
- migration and security documentation.

Remaining identity and tenant lifecycle work:

- tenant provisioning and deactivation;
- memberships, invitations, domain verification, and role lifecycle;
- external identity-provider synchronization;
- managed key rotation and token revocation;
- per-tenant quotas, retention, export, and deletion workflows;
- separation of the privileged worker/platform path into an independent trust boundary.

## Milestone 4 — Auditability and Observability (Completed Foundation)

Goal: make security-sensitive activity and failures traceable and operationally diagnosable.

Tracked by completed issue #33.

Delivered:

- validated `X-Correlation-ID` generation, response echo, log-safe diagnostic linkage, and service propagation;
- W3C trace-context propagation through OpenTelemetry HTTP instrumentation;
- structured JSON console logging with trace, span, correlation digest, tenant, document, and job context;
- OpenTelemetry tracing for ASP.NET Core, HttpClient, FastAPI, Search, Ask, upload, and background ingestion;
- metrics for authorization denials, uploads, retrieval, processing duration, retries, failures, and recovery;
- optional OTLP/HTTP export without a mandatory local collector;
- liveness and dependency-aware readiness endpoints;
- append-only PostgreSQL `audit_events` storage;
- forced tenant RLS and Admin/PlatformAdmin audit-read policies;
- atomic database-trigger audit for document and ingestion-job creation/status changes;
- correlated application audit for listing, upload, status, Search, Ask, and audit access;
- explicit exclusion of document text, queries, questions, bearer tokens, and file content from audit metadata;
- .NET, Python, PostgreSQL, and Compose verification of correlation, audit isolation, append-only privileges, and readiness.

Remaining operational work:

- production collector and telemetry backend selection;
- dashboards, alert rules, service-level objectives, and on-call runbooks;
- audit retention, archival, legal hold, export, and deletion automation;
- tamper-evident hashing or external immutable audit storage;
- load-based trace sampling and metric-cardinality review;
- backup and restore exercises for audit and document data.

## Milestone 5 — Retrieval Evaluation and Grounded Answer Providers (Completed Foundation)

Goal: measure retrieval and grounding behavior before broader provider adoption, without coupling public contracts to a vendor or reducing testability.

Tracked by completed issue #3 for retrieval evaluation and issue #4 for grounded answer generation.

Delivered retrieval evaluation:

- a versioned tenant-safe synthetic corpus and explicit document/chunk relevance judgments;
- exact, ambiguous, vocabulary-mismatch, and empty-query categories;
- a repeatable .NET evaluation command using the existing deterministic embedding and semantic-index abstractions;
- Precision@K, Recall@K, mean reciprocal rank, empty-query accuracy, and local latency metrics;
- machine-readable reports, observed baseline, reviewed regression thresholds, and non-zero failure exit codes;
- a read-only CI workflow with retained artifacts;
- unchanged public Search contracts.

Delivered grounded answer generation:

- `IAnswerGenerator` and `IGroundedAnswerService` abstractions;
- deterministic local extractive generation as the default;
- an optional OpenAI-compatible Chat Completions implementation;
- fail-closed provider configuration for endpoint, credential, model, timeout, and output limits;
- source-count, context-character, and question-length boundaries;
- source content treated as untrusted prompt data;
- mandatory request-local `[S#]` citations for accepted provider answers;
- controlled rejection of uncited or out-of-range citations;
- explicit insufficient-evidence results for missing, low-confidence, conflicting, or provider-declined evidence;
- controlled timeout, network, rate-limit, credential, malformed-response, and empty-response handling;
- source metadata preserved independently from generated text and provider failures;
- token-usage, duration, status, and failure metrics without question, source, answer, response-body, or credential content;
- a versioned eight-case answer-quality dataset with strict grounding-gate thresholds and retained CI reports;
- unit, HTTP-protocol, endpoint, configuration, and regression tests that require no real provider credentials.

Current quality boundary:

- the retrieval corpus remains small and synthetic;
- the local deterministic answer is extractive rather than generative;
- the external-provider path proves protocol, grounding gates, and controlled failures, not model factual accuracy;
- provider activation transfers authorized question/context data outside the deployment and requires a separate privacy, residency, retention, cost, and contractual review.

Remaining provider and evaluation work:

- a larger representative and reviewed corpus;
- multilingual, duplicate, long-document, adversarial, and category-specific evaluation;
- confidence intervals and category-level regression thresholds;
- human-reviewed answer support, completeness, and citation-correctness judgments;
- one approved external-provider comparison run using non-sensitive evaluation data;
- optional provider-specific tokenization and cost estimation;
- embedding-provider abstraction beyond the deterministic model;
- PostgreSQL, local-provider, and approved external-provider comparison reports;
- centralized secret management and managed key rotation before production provider activation.

Python-specific processing should move to FastAPI only when a concrete library or deployment requirement justifies the additional service complexity.

## Milestone 6 — Tenant Lifecycle and Trust-boundary Separation

Goal: move from token-claim demonstrations to managed organization lifecycle and independently deployable privileged processing.

Planned work:

- tenant provisioning and deactivation;
- membership, invitation, role-change, and removal workflows;
- external identity-provider synchronization;
- independent deployment identity for the privileged ingestion worker;
- per-tenant quotas, retention, export, and deletion workflows;
- key rotation and token-revocation integration.

## Milestone 7 — Document Format Expansion

Goal: support additional formats safely.

Planned work:

- PDF and DOCX text extraction;
- optional OCR for scanned documents;
- file-signature validation rather than extension-only checks;
- malware-scanning integration point;
- size, page-count, and extraction-time limits;
- format-specific fixtures and controlled failure cases.

## Explicitly Deferred

The following work remains deferred until secret management, identity lifecycle, retention, and operational foundations are complete:

- multi-tenant billing;
- complex administration dashboards;
- autonomous agents;
- provider-specific optimizations without measured need;
- unsupported claims about production accuracy, confidentiality, or scale.
