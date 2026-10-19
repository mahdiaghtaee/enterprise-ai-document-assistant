# Implemented Capabilities and Deferred Work

This file keeps the detailed scope inventory out of the README while preserving the exact engineering boundary of the completed v0.5.x reference milestone.

## Current Scope

Implemented:

- ASP.NET Core REST API with Swagger/OpenAPI
- fail-closed JWT validation for issuer, audience, signature, lifetime, `sub`, `tenant_id`, and role
- durable `tenants`, `tenant_memberships`, and `tenant_invitations` storage
- PlatformAdmin tenant provisioning, deactivation, and reactivation
- atomic tenant and initial Admin creation
- tenant-scoped `User` and `Admin` roles plus explicit cross-tenant `PlatformAdmin`
- active tenant and active durable membership checks on protected requests
- durable Admin enforcement that rejects stale elevated JWT claims
- member listing, role changes, removal, and final-Admin protection
- one-time invitation creation, listing, revocation, expiry, and authenticated acceptance
- SHA-256 invitation-token digests with plaintext returned only once
- immutable document owner and tenant identity derived from JWT claims
- forced PostgreSQL Row-Level Security on tenants, memberships, invitations, documents, semantic chunks, ingestion jobs, active audit events, and archived audit events
- separate non-superuser `document_app`, `document_platform`, and `document_privileged` roles
- a public API container without the privileged worker credential
- an independent ingestion-worker container with no published host port
- shared named-volume document storage between enqueue and processing
- explicit `Api`, `Worker`, and compatibility `Combined` hosting modes
- PostgreSQL-backed metadata and atomic document/job enqueue
- durable `Pending`, `Processing`, `Completed`, and `Failed` job states
- transactional job claiming with PostgreSQL `FOR UPDATE SKIP LOCKED`
- extension/MIME agreement plus actual PDF and DOCX package inspection before durable enqueue
- bounded strict UTF-8, PDF, and DOCX text extraction in the independent worker
- PDF page limits, DOCX archive/XML limits, total extracted-character limits, and cancellation-aware processing
- explicit `ocr-required` handling for image-only/scanned PDFs rather than empty indexing
- optional fail-closed ClamAV `INSTREAM` malware-scanning boundary with a service-free disabled local default
- background extraction, chunking, embedding, and semantic-index persistence
- bounded retries, graceful shutdown, and abandoned-job recovery
- authenticated processing-status, semantic search, and grounded Ask endpoints
- deterministic local extractive answer generation as the default
- optional OpenAI-compatible Chat Completions answer generation
- bounded question, source-count, and context-character provider inputs
- untrusted-source prompt boundaries and mandatory `[S#]` citations
- explicit missing, low-confidence, conflicting, and provider-declined evidence outcomes
- controlled timeout, rate-limit, provider-authentication, malformed-response, and ungrounded-response errors
- source metadata preserved independently from provider-generated text and failures
- validated `X-Correlation-ID` generation, echo, log-safe diagnostic linkage, and service propagation
- OpenTelemetry tracing and metrics for ASP.NET Core, HttpClient, runtime, Search, Ask, provider generation, upload, worker processing, and audit operations
- FastAPI OpenTelemetry request instrumentation and correlation propagation
- optional OTLP/HTTP export while retaining collector-free local execution
- structured JSON console logging with trace, span, correlation digest, document, tenant, and job context
- liveness and dependency-aware readiness endpoints
- append-only PostgreSQL audit ledger with forced tenant RLS
- database-generated per-tenant audit sequence, previous hash, and SHA-256 event hash
- transactionally serialized same-tenant audit-chain heads to prevent concurrent forks
- deterministic backfill of existing audit history
- tenant-RLS protected `audit_event_archive` that preserves original event IDs and chain fields
- integrity verification spanning active and archived audit rows
- `GET /api/audit/integrity` with own-tenant Admin scope and explicit-tenant PlatformAdmin scope
- bounded privileged audit archival without direct application-table DELETE privileges
- Worker-hosted audit-retention automation with a safe disabled default and configurable age/batch/cadence limits
- correlated application audit for document, answer, tenant, membership, invitation, integrity, and audit-read operations
- tenant-admin audit visibility and explicit PlatformAdmin cross-tenant visibility
- an opt-in OpenTelemetry Collector, Prometheus, Grafana, and Alertmanager Compose override
- version-controlled Prometheus recording/alert rules, Grafana datasource/dashboard provisioning, initial SLO guardrails, and incident/restore runbooks
- deterministic eight-dimensional local embeddings and configurable in-memory or PostgreSQL/pgvector semantic index
- versioned tenant-safe retrieval corpus and relevance judgments
- repeatable Precision@K, Recall@K, MRR, empty-query, and local latency evaluation
- versioned grounded-answer cases for citation, insufficient evidence, and provider-failure gates
- reviewed English, Persian, and mixed-language quality cases with per-language/category metrics and deterministic bootstrap intervals
- CI-enforced segmented regression thresholds so Persian or mixed-language regressions cannot be hidden by aggregate scores
- machine-readable regression baselines, non-zero failure exit codes, and retained CI artifacts
- Docker Compose verification, operational-observability integration, .NET/Python tests, coverage floors, direct PostgreSQL RLS/audit tests, CodeQL, and Dependency Review

Not implemented yet:

- trusted email/SMS invitation delivery or domain verification
- external identity-provider or SCIM synchronization
- managed signing-key rotation, identity-provider session revocation, or device controls
- per-tenant quotas, organization-level retention/export/deletion/legal-hold workflows, or recovery automation
- production HA telemetry backend/storage, secret-managed external paging receivers, or production-calibrated multi-window SLO burn rules
- independently anchored/signed audit chain heads or external immutable audit storage
- jurisdiction-specific archive purge, legal hold, subject-access, and deletion automation
- proven RPO/RTO values from repeated restore exercises
- encrypted document storage and centralized secret management
- approved production provider account or provider-specific factual-accuracy validation
- production-scale or statistically representative multilingual retrieval/answer evaluation beyond the committed reviewed synthetic suite
- OCR execution, rich PDF layout/table reconstruction, password-protected-document workflows, or a bundled production malware engine

Managed membership checks, database isolation, file-format gates, grounding gates, tamper-evident audit chains, and bounded operational telemetry reduce disclosure and accountability risks, but this reference project must not be used for confidential or regulated documents until identity-provider lifecycle, encryption, secret management, legal retention, invitation delivery, malware operations, external-provider governance, immutable-audit requirements, and production operational review are completed. See [SECURITY.md](SECURITY.md).

## Maintenance note

The repository is in maintenance mode. The list above documents the completed reference scope and deferred production work; it is not an active feature roadmap.
