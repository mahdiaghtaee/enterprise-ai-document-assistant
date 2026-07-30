# Enterprise AI Document Assistant

[![CI](https://github.com/mahdiaghtaee/enterprise-ai-document-assistant/actions/workflows/ci.yml/badge.svg)](https://github.com/mahdiaghtaee/enterprise-ai-document-assistant/actions/workflows/ci.yml)
[![Audit and observability](https://github.com/mahdiaghtaee/enterprise-ai-document-assistant/actions/workflows/observability.yml/badge.svg)](https://github.com/mahdiaghtaee/enterprise-ai-document-assistant/actions/workflows/observability.yml)
[![Retrieval quality](https://github.com/mahdiaghtaee/enterprise-ai-document-assistant/actions/workflows/retrieval-evaluation.yml/badge.svg)](https://github.com/mahdiaghtaee/enterprise-ai-document-assistant/actions/workflows/retrieval-evaluation.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A local-first reference implementation for tenant-isolated document ingestion, durable background processing, persistent semantic retrieval, source-aware answers, auditable operations, and reproducible retrieval-quality evaluation.

The repository combines **ASP.NET Core**, **Python FastAPI**, **PostgreSQL with pgvector**, **Redis**, a small Web UI, and **Docker Compose**. The deterministic document pipeline runs in an ASP.NET Core hosted worker and can be tested without external AI credentials or a telemetry collector.

```text
JWT tenant/user -> Correlated RLS-scoped request -> Durable audit + enqueue ->
Background extraction/indexing -> Tenant-scoped retrieval -> Answer with sources
                                      |
                                      +-> Versioned corpus + regression baseline
```

## Current Scope

Implemented:

- ASP.NET Core REST API with Swagger/OpenAPI
- fail-closed JWT validation for issuer, audience, signature, lifetime, `sub`, `tenant_id`, and role
- tenant-scoped `User` and `Admin` roles plus explicit cross-tenant `PlatformAdmin`
- immutable document owner and tenant identity derived from JWT claims
- PostgreSQL Row-Level Security on documents, semantic chunks, ingestion jobs, and audit events
- separate non-superuser runtime and privileged PostgreSQL roles
- local document storage and PostgreSQL-backed metadata
- atomic document metadata and initial ingestion-job persistence
- durable `Pending`, `Processing`, `Completed`, and `Failed` job states
- transactional job claiming with PostgreSQL `FOR UPDATE SKIP LOCKED`
- hosted background extraction, chunking, embedding, and semantic-index persistence
- bounded retries, graceful shutdown, and abandoned-job recovery
- authenticated processing-status, semantic search, and source-aware Ask endpoints
- validated `X-Correlation-ID` generation, echo, log-safe diagnostic linkage, and service propagation
- OpenTelemetry tracing and metrics for ASP.NET Core, HttpClient, runtime, Search, Ask, upload, and worker processing
- FastAPI OpenTelemetry request instrumentation and correlation propagation
- optional OTLP/HTTP export while retaining collector-free local execution
- structured JSON console logging with trace, span, correlation digest, document, and job context
- liveness and dependency-aware readiness endpoints
- append-only PostgreSQL audit ledger with forced tenant RLS
- atomic trigger audit for document and ingestion-job state changes
- correlated application audit for list, upload, status, Search, Ask, and audit access
- tenant-admin audit visibility and explicit PlatformAdmin cross-tenant visibility
- plain-text extraction and deterministic eight-dimensional local embeddings
- configurable in-memory or PostgreSQL/pgvector semantic index
- versioned tenant-safe retrieval corpus and relevance judgments
- repeatable Precision@K, Recall@K, MRR, empty-query, and local latency evaluation
- machine-readable regression baseline, non-zero failure exit code, and retained CI report artifact
- Docker Compose verification, .NET/Python tests, coverage floors, CodeQL, and Dependency Review

Not implemented yet:

- tenant provisioning, memberships, invitations, domain verification, or quotas
- production identity-provider synchronization, key rotation, or token revocation
- production telemetry backend, dashboards, alerts, or service-level objectives
- audit retention, archival, legal hold, tamper-evident hashing, or external immutable storage
- encrypted document storage and centralized secret management
- production language-model or embedding-provider integration
- representative production-scale or statistically validated retrieval evaluation
- PDF, DOCX, OCR, malware scanning, or file-signature validation

Tenant isolation and durable auditability reduce disclosure and accountability risks, but this reference project must not be used for confidential or regulated documents until identity lifecycle, encryption, secret management, retention, and operational review are completed. See [SECURITY.md](SECURITY.md).

## Quick Start

### Requirements

- Docker Desktop or Docker Engine with Compose
- Git
- Python 3.11+ for the local token helper and demo script
- .NET SDK 8.0+ for local tests and retrieval evaluation

### Start the stack

```bash
git clone https://github.com/mahdiaghtaee/enterprise-ai-document-assistant.git
cd enterprise-ai-document-assistant
cp .env.example .env   # Windows PowerShell: Copy-Item .env.example .env
docker compose up --build
```

Docker Compose uses separate local PostgreSQL credentials for the tenant-restricted API path and the privileged worker/platform path. It does not require a telemetry collector. To export OTLP/HTTP signals, configure `OTEL_EXPORTER_OTLP_ENDPOINT` in `.env`.

| Service | Address |
|---|---|
| Web UI | `http://localhost:3000` |
| Swagger / OpenAPI | `http://localhost:5000/swagger` |
| API health | `http://localhost:5000/health` |
| API liveness | `http://localhost:5000/health/live` |
| API readiness | `http://localhost:5000/health/ready` |
| FastAPI health | `http://localhost:8000/health` |

Generate a tenant-scoped development token:

```bash
python scripts/create_dev_token.py --user demo-user --tenant demo-tenant --role User
```

Generate a tenant administrator token:

```bash
python scripts/create_dev_token.py --user demo-admin --tenant demo-tenant --role Admin
```

Paste the token into Swagger or the Web UI. The helper and repository signing key are local-development tools, not an identity provider.

Run the authenticated demo:

```bash
python scripts/demo_flow.py
```

Run the .NET tests:

```bash
dotnet test tests/api-dotnet/EnterpriseDocumentAssistant.Api.Tests.csproj
```

Run the versioned retrieval-quality evaluation:

```bash
dotnet run --project tools/retrieval-evaluation/EnterpriseDocumentAssistant.RetrievalEvaluation.csproj
```

The command writes `artifacts/retrieval-evaluation.json` and returns a non-zero exit code when the committed baseline thresholds regress.

Detailed setup and implementation guidance:

- [Local development](docs/LOCAL_DEVELOPMENT.md)
- [Authentication and authorization](docs/AUTHENTICATION_AND_AUTHORIZATION.md)
- [Tenant isolation](docs/TENANT_ISOLATION.md)
- [Health, audit, and observability](docs/HEALTH_AND_OBSERVABILITY.md)
- [Retrieval quality evaluation](docs/RETRIEVAL_EVALUATION.md)

## Architecture

```mermaid
flowchart LR
    U[Authenticated client] --> C[Correlation + JWT policy]
    C --> A[ASP.NET Core API]
    A --> T[Tenant session context]
    T --> P[(PostgreSQL forced RLS)]
    P --> L[(Append-only audit ledger)]
    A --> S[Local document storage]
    P --> W[Privileged ingestion worker]
    W --> X[Extract and chunk]
    X --> E[Deterministic embeddings]
    E --> V[(Tenant-tagged pgvector chunks)]
    V --> Q[Tenant and owner scoped Search / Ask]
    V --> R[Versioned retrieval evaluation]
    A --> F[FastAPI boundary]
    A --> O[OpenTelemetry traces metrics logs]
    F --> O
```

The API derives `owner_id` from `sub` and `tenant_id` from the JWT. Runtime PostgreSQL transactions set transaction-local tenant context; forced RLS independently limits documents, chunks, jobs, and audit rows. `User` requests add an owner filter, `Admin` bypasses only the owner filter inside one tenant, and `PlatformAdmin` uses the explicit privileged path.

Every response carries a validated `X-Correlation-ID`. OpenTelemetry HTTP instrumentation propagates W3C trace context between services. Database triggers commit base mutation events atomically with document and ingestion state changes; application events add semantic action, correlation, trace, result count, and duration without storing document text, query text, question text, or bearer tokens.

The retrieval evaluator uses the same deterministic embedding generator and in-memory semantic-index implementation as the application contracts. It measures a committed corpus independently of PostgreSQL availability while the existing integration workflows continue to verify pgvector persistence and tenant isolation.

Architecture details:

- [Health, audit, and observability](docs/HEALTH_AND_OBSERVABILITY.md)
- [Retrieval quality evaluation](docs/RETRIEVAL_EVALUATION.md)
- [Tenant isolation](docs/TENANT_ISOLATION.md)
- [Authentication and authorization](docs/AUTHENTICATION_AND_AUTHORIZATION.md)
- [Architecture overview](docs/ARCHITECTURE.md)
- [pgvector semantic index](docs/PGVECTOR_SCHEMA.md)
- [Background ingestion](docs/BACKGROUND_INGESTION.md)
- [Roadmap](docs/ROADMAP.md)

## API Security and Audit Behavior

Every `/api/documents` endpoint requires `Authorization: Bearer <token>`. Health endpoints remain public.

For document operations:

- `User`: own documents inside the token tenant;
- `Admin`: all owners inside the token tenant;
- `PlatformAdmin`: all tenants through the privileged path;
- missing token: `401`;
- missing required claims or role: `403`;
- document outside authorized owner or tenant scope: `404`.

`GET /api/audit/events` requires `Admin` or `PlatformAdmin`. Tenant administrators see only their tenant through RLS; PlatformAdmin can retrieve cross-tenant records. Application roles have no `UPDATE` or `DELETE` permission on the audit table.

## Verification

CI verifies:

- JWT claim, role, owner, and tenant enforcement;
- forced RLS and non-`BYPASSRLS` runtime roles;
- cross-tenant database read/write rejection;
- valid and invalid correlation identifiers in ASP.NET Core and FastAPI;
- liveness and dependency-aware readiness;
- audit constraints, policies, triggers, and append-only privileges;
- tenant-admin audit isolation and PlatformAdmin visibility;
- exclusion of a known sensitive query string from audit responses;
- successful upload, processing, retrieval, authorization, and persistence after API restart;
- retrieval metric calculations and corpus validation;
- exact, ambiguous, vocabulary-mismatch, and empty-query evaluation categories;
- committed Precision@K, Recall@K, MRR, empty-query accuracy, and latency thresholds;
- generation and retention of a machine-readable retrieval evaluation report.

## Current Limitations

- Only the supported local plain-text extraction path is implemented.
- Tenant lifecycle and membership management are not implemented.
- Privileged worker/platform credentials are loaded by the same API process in Docker Compose; production should separate that trust boundary.
- A collector, telemetry storage, dashboards, alerts, audit retention, and tamper-evident archival are not bundled.
- Token revocation, managed key rotation, encrypted storage, and external identity-provider integration remain absent.
- The deterministic embedding model is intended for reproducible development, not production retrieval quality.
- The first evaluation corpus is small and synthetic; it detects deterministic regressions but does not establish production accuracy.
- Docker Compose uses development defaults and exposes local ports.

The next engineering priority is one provider-backed grounded-answer implementation while preserving deterministic local mode, followed by tenant lifecycle and privileged-worker separation.

## Repository Structure

| Area | Responsibility |
|---|---|
| `src/api-dotnet/` | Authentication, tenant policies, audit, observability, public API, worker, persistence, and semantic retrieval |
| `src/ai-service-python/` | Correlated FastAPI and OpenTelemetry boundary for future Python-specific processing |
| `src/web-ui/` | Authenticated demonstration interface |
| `infra/postgres/` | PostgreSQL roles, RLS, audit, ownership, pgvector, and ingestion initialization |
| `evaluation/retrieval/` | Versioned corpus, relevance judgments, observed baseline, and regression thresholds |
| `tools/retrieval-evaluation/` | Provider-free evaluation command and metric implementation |
| `tests/api-dotnet/` | API, security, audit, RLS, provider, pipeline, and PostgreSQL lifecycle tests |
| `tests/retrieval-evaluation/` | Metric, input-validation, baseline, and empty-query tests |
| `scripts/` | Tenant-aware token helper and end-to-end demo |
| `docs/` | Architecture, security, migrations, observability, evaluation, operations, and roadmap |

## Contributing

Focused contributions are welcome for provider integration, representative retrieval corpora, tenant lifecycle, audit retention, and safe document-format expansion.

Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request. Report security-sensitive findings through [SECURITY.md](SECURITY.md).

## License

Released under the [MIT License](LICENSE).
