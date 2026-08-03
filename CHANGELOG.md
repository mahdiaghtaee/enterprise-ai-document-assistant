# Changelog

All notable changes to this project are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses semantic versioning for tagged releases.

## Unreleased

### Added

- Managed `tenants`, `tenant_memberships`, and `tenant_invitations` storage protected by forced PostgreSQL Row-Level Security.
- PlatformAdmin APIs for tenant provisioning, deactivation, and reactivation.
- Atomic creation of a tenant and its initial Admin membership.
- Durable active-tenant and active-membership authorization for document, audit, and tenant-administration operations.
- Durable Admin enforcement that rejects stale elevated JWT claims when the persisted membership is `User`.
- Member listing, role changes, removal, and transactional protection against removing or downgrading the final active tenant Admin.
- One-time, expiry-aware, revocable invitation tokens bound to the authenticated JWT subject and tenant.
- Cryptographically random invitation secrets with SHA-256 digest-only persistence and plaintext returned once.
- A narrow `document_platform` PostgreSQL role for lifecycle management, cross-tenant reads, and audit insertion.
- `ApplicationMode=Api`, `ApplicationMode=Worker`, and compatibility `ApplicationMode=Combined` hosting modes.
- An independent Docker Compose ingestion worker with no published host port and shared named-volume document storage.
- Lifecycle audit events for provisioning, tenant status, membership role/removal, and invitation creation/acceptance/revocation.
- Direct PostgreSQL lifecycle tests for digest-only invitation storage, one-time acceptance, final-Admin protection, forced RLS, and cross-tenant write rejection.
- Compose verification for provisioning, invitation acceptance, stale-role denial, member removal, tenant deactivation, independent worker processing, credential separation, and restart persistence.
- A self-provisioning managed-tenant demo flow.
- Fail-closed JWT bearer authentication with required subject, tenant, and role validation.
- Immutable document ownership and tenant identity derived from authenticated JWT claims.
- Forced PostgreSQL Row-Level Security for documents, chunks, ingestion jobs, and audit events.
- Validated `X-Correlation-ID` generation, response echo, log scoping, and service propagation.
- Structured JSON console logging with trace, span, correlation, document, tenant, and ingestion-job context.
- OpenTelemetry tracing and metrics for ASP.NET Core HTTP, HttpClient, FastAPI, Search, Ask, provider generation, upload, and background ingestion.
- Optional OTLP/HTTP export without requiring a collector for local development.
- ASP.NET Core liveness and dependency-aware readiness endpoints.
- Append-only PostgreSQL `audit_events` storage with tenant RLS and Admin/PlatformAdmin read policies.
- Atomic database-trigger audit for document and ingestion-job creation and status changes.
- Correlated application audit for document, answer, lifecycle, and audit operations.
- A versioned tenant-safe retrieval corpus, provider-free evaluation command, machine-readable metrics, and regression thresholds.
- `IAnswerGenerator` and `IGroundedAnswerService` abstractions with deterministic local and optional OpenAI-compatible implementations.
- Evidence-strength, conflict, context-size, timeout, output-token, and citation validation gates.
- Controlled insufficient-evidence and provider-failure responses that preserve independent source metadata.
- A credential-free eight-case grounded-answer evaluation baseline and retained CI report.

### Changed

- JWT claims authenticate the requested subject and tenant, while durable tenant/membership state is authoritative for non-platform authorization.
- Membership removal, Admin downgrade, and tenant deactivation affect the next protected request without waiting for JWT expiration.
- The public API no longer receives `ConnectionStrings:PostgresPrivileged`; only the independent Worker receives the full ingestion credential.
- PlatformAdmin cross-tenant reads and lifecycle mutations use the narrower `document_platform` database role.
- Docker Compose runs separate `document-api` and `document-worker` services sharing a named document-storage volume.
- Processing-status reads use tenant-RLS or platform-read paths rather than the worker repository credential.
- The local demo provisions a tenant and accepts a one-time invitation before document operations.
- `Admin` remains tenant scoped; only `PlatformAdmin` uses the explicit cross-tenant path.
- Search, Ask, and lifecycle audit metadata stores bounded operational values while excluding query, question, source, answer, invitation-secret, response-body, and credential content.
- Ask keeps its original response fields while adding answer status, provider, model, grounding, and reason metadata.
- Documentation now treats managed tenant lifecycle, split-worker trust boundaries, tenant isolation, audit/observability, retrieval evaluation, and grounded-answer providers as delivered foundations.

### Migration notes

- Fresh databases apply `infra/postgres/init/zzzz-document-ownership.sql`, `infra/postgres/init/zzzzz-tenant-isolation.sql`, `infra/postgres/init/zzzzzz-audit-observability.sql`, and `infra/postgres/init/zzzzzzz-tenant-lifecycle.sql` automatically.
- Existing PostgreSQL volumes require reviewed manual application after verified database and stored-file backups because entrypoint scripts do not rerun.
- Existing tenant/owner data is mapped to explicit lifecycle records; every generated mapping and active Admin assignment must be reviewed before serving traffic.
- Deployments must provision and rotate distinct `document_app`, `document_platform`, and `document_privileged` credentials.
- The API and Worker should be deployed as separate identities; the privileged worker connection must not be copied into the public API environment.
- Application roles require `SELECT` and `INSERT`, but not `UPDATE` or `DELETE`, on `audit_events`.
- Existing deployments remain on deterministic answer generation unless `ANSWER_GENERATION_PROVIDER=OpenAiCompatible` is selected explicitly.
- External-provider deployments must supply endpoint, API key, and model through trusted configuration and review provider data-handling terms before activation.

### Known limitations

- Trusted invitation email/SMS delivery, domain verification, and recipient identity proofing are not implemented.
- External IdP/SCIM synchronization, managed signing-key rotation, identity-provider session revocation, and device controls remain absent.
- Per-tenant quotas, retention, export, deletion, legal hold, and organization recovery workflows are not implemented.
- A production telemetry backend, dashboards, alerts, SLOs, audit retention, and tamper-evident archival are not bundled.
- Encrypted document storage, malware scanning, and centralized secret management remain absent.
- The repository signing key, PlatformAdmin tokens, and token helper are for local development only.
- The retrieval and answer datasets are small and synthetic; they detect controlled regressions but do not establish production factual accuracy.
- The optional provider path verifies protocol and grounding controls without a production provider account or factual-accuracy claim.
- The project remains unsuitable for confidential or regulated documents without additional identity, invitation-delivery, encryption, retention, secret-management, and operational controls.

## 0.3.0 - 2026-07-27

### Added

- Atomic document metadata and initial ingestion-job creation in one PostgreSQL transaction.
- An ASP.NET Core hosted worker that claims durable jobs with `FOR UPDATE SKIP LOCKED`.
- Background text extraction, chunking, deterministic embedding generation, and semantic-index persistence.
- Bounded delayed retries with terminal failure after attempt exhaustion.
- Recovery for abandoned `Processing` jobs after a configurable timeout.
- Graceful-shutdown handling that returns interrupted work to the queue without consuming an attempt.
- `GET /api/documents/{documentId}/processing-status` for lifecycle, attempt, timestamp, and controlled-error reporting.
- PostgreSQL integration tests for claiming, completion, retry exhaustion, recovery, and latest-status retrieval.
- Focused Worker and Processor tests for completion, retry, failure, cancellation, and status mapping.
- Configurable polling, retry, timeout, and recovery intervals through the `IngestionWorker` configuration section.
- Release notes for the reliable background-ingestion milestone.

### Changed

- `POST /api/documents/upload` returns `202 Accepted` after durable enqueue instead of performing extraction and indexing synchronously.
- Document list status reflects `uploaded`, `processing`, `retry-pending`, `indexed`, or `failed` processing progress.
- The Web UI and demo script poll document processing state before search or ask operations.
- Docker Compose CI verifies asynchronous completion, persistence across API restart, and completed job state.
- Local uploaded files are removed when atomic database enqueue fails.

### Migration notes

- Existing PostgreSQL volumes from v0.2.0 already contain the required ingestion-job schema.
- Environments older than v0.2.0 must back up required data and apply the reviewed idempotent schema scripts or recreate only disposable local volumes.
- Upload clients must poll the returned `processingStatusUrl` and wait for `Completed` before expecting the new document in retrieval results.

### Known limitations

- Only the supported local plain-text extraction path is implemented.
- The deterministic embedding generator is intended for reproducible development and evaluation, not production retrieval quality.
- The FastAPI service remains an integration boundary and does not perform extraction, embeddings, retrieval, or answer generation.
- Docker Compose uses development defaults and exposed local ports.

## 0.2.0 - 2026-07-20

### Added

- Configurable local ports and PostgreSQL development credentials through `.env`.
- Expanded local-development and troubleshooting documentation.
- Dependabot update configuration for GitHub Actions, NuGet, pip, and Docker.
- CodeQL analysis for C# and Python.
- CODEOWNERS coverage for the repository and security-sensitive paths.
- FastAPI endpoint tests, Ruff validation, Docker Compose health checks, and .NET coverage artifacts/floors.
- An idempotent pgvector schema with `vector(8)` embeddings and an HNSW cosine index.
- PostgreSQL `ISemanticIndexStore` with transactional upserts and pgvector cosine search.
- Configuration-driven in-memory/PostgreSQL semantic-index selection.
- A durable `document_ingestion_jobs` schema with constrained states, attempts, timestamps, and claim indexes.

### Changed

- Documentation was aligned with the implemented .NET pipeline and FastAPI boundary.
- CI was split into independent .NET, Python, and container validation jobs.
- PostgreSQL moved to the pinned `pgvector/pgvector:0.8.5-pg16` image.
- The pgvector dimension was aligned with the eight-dimensional deterministic embedding generator.
- Docker Compose used persistent PostgreSQL semantic indexing while isolated tests retained in-memory defaults.

### Migration notes

- Fresh Docker Compose volumes initialize pgvector, `document_chunks`, and `document_ingestion_jobs` automatically.
- Existing PostgreSQL volumes do not rerun entrypoint initialization scripts. Back up required data, then apply reviewed scripts manually or recreate only disposable local volumes.
- Changing embedding dimensions requires an explicit database migration.

### Known limitations

- The deterministic embedding generator is intended for reproducible development and evaluation, not production retrieval quality.
- The FastAPI service remains an integration boundary rather than the active document pipeline.
- Docker Compose uses development defaults and exposed local ports.

## 0.1.0 - 2026-07-10

### Added

- ASP.NET Core REST API with Swagger/OpenAPI and health checks.
- Python FastAPI health and indexing-boundary endpoints.
- Docker Compose environment with PostgreSQL, Redis, Web UI, API, and FastAPI services.
- Local document upload and PostgreSQL-backed metadata.
- Plain-text extraction, fixed-size chunking, deterministic embeddings, and in-memory semantic ranking.
- Semantic search and deterministic source-aware Ask endpoints.
- Web UI, sample documents, demo script, integration tests, and initial CI/documentation.

### Known limitations

- Semantic-index records were not durable across API restarts.
- Document processing was synchronous.
- Authentication, authorization, tenant isolation, and audit logging were not implemented in this version.
