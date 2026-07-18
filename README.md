# Enterprise AI Document Assistant

[![CI](https://github.com/mahdiaghtaee/enterprise-ai-document-assistant/actions/workflows/ci.yml/badge.svg)](https://github.com/mahdiaghtaee/enterprise-ai-document-assistant/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A local-first reference implementation for document ingestion, persistent semantic retrieval, and source-aware answers.

The repository combines **ASP.NET Core**, **Python FastAPI**, **PostgreSQL with pgvector**, **Redis**, a small Web UI, and **Docker Compose**. The deterministic document pipeline runs inside the ASP.NET Core API and can be tested without external AI credentials. FastAPI remains a boundary for future Python-specific document or model integrations.

```text
Upload -> Persist metadata -> Extract -> Chunk -> Embed -> Persist vectors -> Search -> Answer with sources
```

## Current Scope

Implemented:

- ASP.NET Core REST API with Swagger/OpenAPI
- local document storage and PostgreSQL-backed document metadata
- plain-text extraction and fixed-size chunking
- deterministic eight-dimensional local embeddings
- configurable in-memory or PostgreSQL/pgvector semantic index
- pgvector cosine-similarity search with source metadata
- indexed chunks that survive API container restarts in Docker Compose
- semantic search and source-aware ask endpoints
- FastAPI health and indexing-boundary endpoints
- Redis infrastructure for future caching and background work
- Web UI, sample documents, demo script, integration tests, CI, CodeQL, and Dependency Review

Not implemented yet:

- background indexing, retries, and durable processing states
- authentication, authorization, or tenant isolation
- production language-model integration
- audit logging and distributed observability
- retrieval-quality evaluation on a representative corpus

The project must not be used for confidential or regulated documents until those controls are added. See [SECURITY.md](SECURITY.md).

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
    A --> P[(PostgreSQL metadata)]
    A --> R[(Redis infrastructure)]
    A --> L[Local document pipeline]
    L --> X[Text extraction]
    X --> C[Chunking]
    C --> E[Deterministic embeddings]
    E --> V[(pgvector semantic index)]
    V --> Q[Source-aware answer builder]
    A --> F[FastAPI service boundary]
```

The ASP.NET Core API owns the executable deterministic workflow. PostgreSQL stores both document metadata and semantic-index records. FastAPI proves the service boundary but does not currently perform extraction, embedding, retrieval, or answer generation.

Architecture details:

- [Architecture overview](docs/ARCHITECTURE.md)
- [pgvector semantic index](docs/PGVECTOR_SCHEMA.md)
- [Engineering case study](docs/CASE_STUDY.md)
- [Local-first architecture decision](docs/adr/0001-local-first-document-intelligence.md)
- [Roadmap](docs/ROADMAP.md)

## API Workflow

### Upload

`POST /api/documents/upload`

The API validates and stores the file, persists metadata, extracts text, creates chunks, generates deterministic embeddings, and writes them through the configured semantic-index provider. Docker Compose uses PostgreSQL with pgvector.

### Search

`POST /api/documents/search`

The API embeds the query with the same deterministic generator and returns the highest-scoring chunks. The PostgreSQL provider uses pgvector cosine distance and preserves file-name source metadata.

### Ask

`POST /api/documents/ask`

The API retrieves relevant chunks and constructs a deterministic answer containing source context. This demonstrates retrieval and attribution; it is not presented as production LLM output.

## Persistence Check

After uploading and searching a document:

```bash
docker compose restart document-api
```

Wait for the API health endpoint and repeat the search. Indexed chunks remain available because they are stored in PostgreSQL. CI runs this scenario automatically.

## Current Limitations

- Only the supported local text-extraction path is implemented.
- Processing is synchronous.
- FastAPI does not yet perform extraction, embedding, or retrieval.
- Authentication, authorization, tenant isolation, and audit logging are absent.
- The deterministic embedding model is intended for reproducible development, not production retrieval quality.
- Docker Compose uses development defaults and exposes local ports.

The next major milestone is background document ingestion with durable processing states and retries.

## Repository Structure

| Area | Responsibility |
|---|---|
| `src/api-dotnet/` | Public API, metadata persistence, deterministic document pipeline, semantic-index providers |
| `src/ai-service-python/` | FastAPI boundary for future Python-specific processing |
| `src/web-ui/` | Demonstration interface |
| `infra/postgres/` | PostgreSQL and pgvector initialization |
| `tests/api-dotnet/` | API, provider, and pipeline tests |
| `scripts/` | End-to-end demonstration flow |
| `samples/` | Uploadable example documents |
| `docs/` | Architecture, operations, security, and roadmap |

## Contributing

Focused contributions are welcome for tests, validation, background processing, observability, access control, and retrieval evaluation.

Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request. Report security-sensitive findings through [SECURITY.md](SECURITY.md).

## License

Released under the [MIT License](LICENSE).
