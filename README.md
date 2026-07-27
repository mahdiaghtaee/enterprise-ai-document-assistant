# Enterprise AI Document Assistant

[![CI](https://github.com/mahdiaghtaee/enterprise-ai-document-assistant/actions/workflows/ci.yml/badge.svg)](https://github.com/mahdiaghtaee/enterprise-ai-document-assistant/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A local-first reference implementation for durable document ingestion, persistent semantic retrieval, and source-aware answers.

The repository combines **ASP.NET Core**, **Python FastAPI**, **PostgreSQL with pgvector**, **Redis**, a small Web UI, and **Docker Compose**. The deterministic document pipeline runs in an ASP.NET Core hosted worker and can be tested without external AI credentials. FastAPI remains a boundary for future Python-specific document or model integrations.

```text
Upload -> Store file -> Atomically enqueue -> Background extract/chunk/embed -> Persist vectors -> Search -> Answer with sources
```

## Current Scope

Implemented:

- ASP.NET Core REST API with Swagger/OpenAPI
- local document storage and PostgreSQL-backed document metadata
- atomic document metadata and initial ingestion-job persistence
- durable `Pending`, `Processing`, `Completed`, and `Failed` job states
- transactional job claiming with PostgreSQL `FOR UPDATE SKIP LOCKED`
- hosted background extraction, chunking, embedding, and semantic-index persistence
- bounded delayed retries and abandoned-job recovery
- graceful-shutdown return to the queue without consuming an attempt
- public document processing-status API with controlled failure details
- plain-text extraction and fixed-size chunking
- deterministic eight-dimensional local embeddings
- configurable in-memory or PostgreSQL/pgvector semantic index
- pgvector cosine-similarity search with source metadata
- indexed chunks that survive API container restarts in Docker Compose
- semantic search and source-aware ask endpoints
- FastAPI health and indexing-boundary endpoints
- Redis infrastructure for future caching or coordination
- Web UI, sample documents, demo script, integration tests, CI, CodeQL, and Dependency Review

Not implemented yet:

- authentication, authorization, document ownership, or tenant isolation
- production language-model or embedding-provider integration
- audit logging and distributed observability
- retrieval-quality evaluation on a representative corpus
- PDF, DOCX, or OCR extraction

The project must not be used for confidential or regulated documents until the required access-control, audit, and operational controls are added. See [SECURITY.md](SECURITY.md).

## Quick Start

### Requirements

- Docker Desktop or Docker Engine with Compose
- Git

### Start the stack

```bash
git clone https://github.com/mahdiaghtaee/enterprise-ai-document-assistant.git
cd enterprise-ai-document-assistant
cp .env.example .env   # Windows PowerShell: Copy-Item .env.example .env
docker compose up --build
```

Docker Compose explicitly selects the PostgreSQL semantic-index provider. Without `SemanticIndex:Provider`, the application defaults to the in-memory provider.

| Service | Address |
|---|---|
| Web UI | `http://localhost:3000` |
| Swagger / OpenAPI | `http://localhost:5000/swagger` |
| ASP.NET Core health | `http://localhost:5000/health` |
| FastAPI health | `http://localhost:8000/health` |

Run the demo:

```bash
python scripts/demo_flow.py
```

Run the .NET tests:

```bash
dotnet test tests/api-dotnet/EnterpriseDocumentAssistant.Api.Tests.csproj
```

Detailed setup and persistence verification are in [docs/LOCAL_DEVELOPMENT.md](docs/LOCAL_DEVELOPMENT.md).

## Architecture

```mermaid
flowchart LR
    U[Web UI / API Client] --> A[ASP.NET Core API]
    A --> S[Local document storage]
    A --> P[(PostgreSQL documents and jobs)]
    P --> W[ASP.NET Core ingestion worker]
    W --> X[Text extraction]
    X --> C[Chunking]
    C --> E[Deterministic embeddings]
    E --> V[(pgvector semantic index)]
    V --> Q[Search and source-aware answers]
    A --> R[(Redis infrastructure)]
    A --> F[FastAPI service boundary]
```

The API stores the file, then creates the document metadata and initial `Pending` job in one PostgreSQL transaction. The hosted worker claims available jobs without duplicate active processing, advances lifecycle state, and persists semantic-index records through the configured provider. FastAPI proves the service boundary but does not currently perform extraction, embedding, retrieval, or answer generation.

Architecture details:

- [Architecture overview](docs/ARCHITECTURE.md)
- [pgvector semantic index](docs/PGVECTOR_SCHEMA.md)
- [Background ingestion](docs/BACKGROUND_INGESTION.md)
- [Engineering case study](docs/CASE_STUDY.md)
- [Local-first architecture decision](docs/adr/0001-local-first-document-intelligence.md)
- [Roadmap](docs/ROADMAP.md)

## API Workflow

### Upload

`POST /api/documents/upload`

The API validates and stores the file, then atomically persists document metadata and a `Pending` ingestion job. It returns `202 Accepted` with the document ID, job ID, and a processing-status URL. Extraction and indexing continue in the hosted worker.

If database enqueue fails after storage succeeds, the local file is removed to avoid an untracked storage orphan.

### Processing status

`GET /api/documents/{documentId}/processing-status`

Returns the current job state, attempts, lifecycle timestamps, controlled error details, and terminal-state indicator.

### Search

`POST /api/documents/search`

The API embeds the query with the same deterministic generator and returns the highest-scoring indexed chunks. The PostgreSQL provider uses pgvector cosine distance and preserves file-name source metadata.

### Ask

`POST /api/documents/ask`

The API retrieves relevant chunks and constructs a deterministic answer containing source context. This demonstrates retrieval and attribution; it is not presented as production LLM output.

## Retry and Recovery

Unexpected processing failures are retried after a configurable delay until the bounded attempt limit is reached. Known content or validation failures become terminal immediately.

The worker periodically recovers `Processing` jobs whose processing lease has expired. Jobs with attempts remaining return to `Pending`; exhausted jobs become `Failed`. See [the background ingestion documentation](docs/BACKGROUND_INGESTION.md) for configuration and state-transition details.

## Persistence Check

After a document reaches `Completed`, search for it and restart the API container:

```bash
docker compose restart document-api
```

Wait for the API health endpoint and repeat the search. Indexed chunks remain available because they are stored in PostgreSQL. CI runs a persistence scenario automatically.

## Current Limitations

- Only the supported local plain-text extraction path is implemented.
- FastAPI does not yet perform extraction, embedding, retrieval, or answer generation.
- Authentication, authorization, document ownership, tenant isolation, and audit logging are absent.
- The deterministic embedding model is intended for reproducible development, not production retrieval quality.
- Background processing uses PostgreSQL as the durable queue; advanced scheduling and separate worker deployment are not implemented.
- Docker Compose uses development defaults and exposes local ports.

The next major milestone is identity and document authorization, followed by auditability and observability.

## Repository Structure

| Area | Responsibility |
|---|---|
| `src/api-dotnet/` | Public API, durable ingestion worker, metadata persistence, document pipeline, semantic-index providers |
| `src/ai-service-python/` | FastAPI boundary for future Python-specific processing |
| `src/web-ui/` | Demonstration interface |
| `infra/postgres/` | PostgreSQL, pgvector, and ingestion-job initialization |
| `tests/api-dotnet/` | API, provider, pipeline, and PostgreSQL lifecycle tests |
| `scripts/` | End-to-end demonstration flow |
| `samples/` | Uploadable example documents |
| `docs/` | Architecture, operations, security, and roadmap |

## Contributing

Focused contributions are welcome for tests, validation, access control, observability, retrieval evaluation, and safe document-format expansion.

Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request. Report security-sensitive findings through [SECURITY.md](SECURITY.md).

## License

Released under the [MIT License](LICENSE).
