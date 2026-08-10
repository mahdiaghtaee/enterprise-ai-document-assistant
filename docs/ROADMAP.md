# Roadmap

This roadmap separates completed capabilities from planned work. A milestone is marked complete only when its implementation, tests, and documentation are present in the repository.

## Completed Foundation

- split Docker Compose environment for the public API, privileged worker, Web UI, FastAPI service, PostgreSQL, and Redis
- ASP.NET Core and FastAPI health endpoints
- local shared document storage
- PostgreSQL-backed document metadata
- bounded TXT, text-bearing PDF, and DOCX extraction with safe upload gates
- deterministic local embeddings
- semantic search and grounded Ask endpoints
- managed tenant lifecycle and durable membership enforcement
- sample documents and end-to-end demo script
- .NET, Python, PostgreSQL, container, document-format, CodeQL, and Dependency Review checks

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

## Milestone 3 — Identity, Ownership, and Database Tenant Isolation (Completed)

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
- forced PostgreSQL Row-Level Security for tenant data tables;
- transaction-local tenant session context for runtime operations;
- composite tenant/document foreign keys;
- background-processing preservation of tenant and owner identity;
- local tenant-aware token helper, Swagger, Web UI, and demo flow;
- negative API and database tests for cross-user and cross-tenant access;
- migration and security documentation.

Remaining identity-provider work:

- external identity-provider and directory synchronization;
- domain verification and organization ownership proof;
- managed signing-key rotation and token/session revocation;
- SCIM or equivalent enterprise provisioning integration;
- device, conditional-access, and break-glass controls.

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
- correlated application audit for listing, upload, status, Search, Ask, lifecycle changes, and audit access;
- explicit exclusion of document text, queries, questions, invitation tokens, bearer tokens, and file content from audit metadata;
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

## Milestone 6 — Managed Tenant Lifecycle and Worker Trust Boundary (Completed Foundation)

Goal: move from token-claim demonstrations to managed organization lifecycle and independently deployed privileged processing.

Tracked by issue #81.

Delivered:

- durable `tenants`, `tenant_memberships`, and `tenant_invitations` storage;
- PlatformAdmin provisioning, deactivation, and reactivation APIs;
- atomic tenant and initial Admin creation;
- active tenant and active durable membership checks on protected requests;
- durable Admin-role enforcement that rejects stale elevated JWT claims;
- member listing, role changes, and removal;
- transactional protection against removing or downgrading the final active Admin;
- one-time, expiration-aware, revocable invitations bound to the authenticated subject;
- storage of SHA-256 invitation-token digests rather than plaintext;
- forced RLS and direct cross-tenant database rejection tests for lifecycle tables;
- bounded lifecycle audit records that exclude invitation secrets;
- separate `document_app`, `document_platform`, and `document_privileged` database roles;
- `ApplicationMode=Api`, `Worker`, and compatibility `Combined` modes;
- an independent Compose worker with no published host port and no privileged credential in the public API container;
- shared named-volume document storage between enqueue and processing services;
- API, authorization, PostgreSQL, Compose, restart, and negative lifecycle tests;
- a self-bootstrapping local demo and migration/security documentation.

Current lifecycle boundary:

- local invitations target a stable subject identifier rather than an email address;
- invitation delivery and recipient proofing are external responsibilities;
- removed membership blocks this application immediately but does not revoke an identity-provider session;
- the platform and worker roles are separated at the database credential and process level, but the reference stack remains a single Compose deployment.

Remaining tenant-management work:

- external IdP/SCIM synchronization;
- trusted email or enterprise invitation delivery;
- domain ownership verification;
- per-tenant quotas and usage governance;
- retention, export, deletion, and legal-hold workflows;
- organization transfer and recovery procedures;
- centralized secrets, managed service identities, and production network policy;
- approval and notification workflows for elevated-role changes.

## Milestone 7 — Safe Document Format Expansion (Completed Foundation)

Goal: support additional formats without treating extension/MIME metadata as a trust boundary or allowing unbounded parser work.

Tracked by issue #84.

Delivered:

- strict extension/content-type agreement for `.txt`, `.pdf`, and `.docx`;
- actual `%PDF-` signature checking plus PdfPig parse validation before durable enqueue;
- DOCX ZIP/OOXML validation for required parts and WordprocessingML main-document declaration;
- path-traversal rejection for DOCX archive entries;
- configurable DOCX entry-count, expanded-byte, and XML-character limits;
- configurable PDF page-count and extracted-character limits;
- strict UTF-8 plain-text validation and extraction;
- bounded PDF text extraction with PdfPig content-order extraction;
- bounded DOCX WordprocessingML text extraction with DTD processing disabled and no XML resolver;
- explicit `ocr-required` result for scanned/image-only PDFs rather than silent empty indexing;
- optional fail-closed ClamAV `INSTREAM` scanner integration point;
- local `Disabled` malware-scanning default that requires no external service and is visible through health output;
- scanner failure/threat rejection before document storage and atomic job creation;
- controlled scanner error codes without raw scanner response/signature leakage;
- unit tests for signatures, package validation, extraction, limits, cancellation, and ClamAV protocol outcomes;
- dedicated Compose workflow that uploads real PDF and DOCX fixtures through the managed tenant API, waits for the independent worker, verifies retrieval, and rejects a spoofed PDF;
- updated configuration and extraction/security documentation.

Current format boundary:

- OCR execution is not bundled; scanned/image-only PDFs stop with `ocr-required`;
- PDF reading order remains dependent on source layout and PdfPig extraction heuristics;
- rich PDF layout/table reconstruction is not implemented;
- password-protected PDF/DOCX workflows are not supported;
- the reference stack does not deploy ClamAV by default; production scanner deployment, signature updates, isolation, monitoring, and network policy are operational responsibilities.

Remaining document-processing work:

- optional sandboxed OCR path with reviewed language packs and resource limits;
- richer layout/table extraction where measured product requirements justify it;
- production malware-scanner deployment/runbook and signature-update monitoring;
- content-disarm/reconstruction or sandboxed document rendering where required;
- file-at-rest encryption and object-storage lifecycle policies;
- representative document-format corpus covering large, multilingual, malformed, and adversarial files.

## Explicitly Deferred

The following work remains deferred until secret management, retention, and operational foundations are complete:

- multi-tenant billing;
- complex administration dashboards;
- autonomous agents;
- provider-specific optimizations without measured need;
- unsupported claims about production accuracy, confidentiality, or scale.
