# Changelog

All notable changes to this project are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project intends to use semantic versioning once tagged releases begin.

## Unreleased

### Added

- Configurable local ports and PostgreSQL development credentials through `.env`.
- Expanded local-development and troubleshooting documentation.
- Explicit documentation of the current .NET document pipeline and the FastAPI service boundary.
- Dependabot update configuration for GitHub Actions, NuGet, pip, and Docker.
- CodeQL analysis for C# and Python.
- CODEOWNERS coverage for the repository and security-sensitive paths.
- FastAPI endpoint tests for health, indexing responses, and request validation.
- Ruff linting and formatting checks for Python code.
- Runtime Docker Compose checks for the ASP.NET Core and FastAPI health endpoints.
- Cobertura-format .NET coverage collection and retained CI artifacts.
- CI coverage floors of 60% line coverage and 50% branch coverage.
- Dependency Review for pull requests targeting `main`.
- An idempotent pgvector initialization script with a `document_chunks` table, fixed `vector(8)` embeddings, and an HNSW cosine-distance index.
- A PostgreSQL implementation of `ISemanticIndexStore` with transactional upserts and pgvector cosine search.
- Configuration-driven selection between `InMemory` and `Postgres` semantic-index providers.
- Provider validation tests for dimensions, finite values, defaults, and unsupported configuration.
- Compose CI coverage that uploads, searches, restarts the API, searches again, and verifies persisted chunk rows.

### Changed

- Clarified that extraction, chunking, deterministic embeddings, semantic retrieval, and answer construction currently run in the ASP.NET Core API.
- Replaced the stale feature roadmap with implementation-based milestones.
- Moved resume, interview, social-post, and repository-visibility notes out of the software repository.
- Restricted GitHub Actions workflow permissions to read-only repository contents unless a workflow requires more.
- Updated GitHub Actions to current Node 24-compatible major versions.
- Split CI into independent .NET, Python, and container validation jobs.
- Replaced the local `postgres:16-alpine` image with the pinned `pgvector/pgvector:0.8.5-pg16` image while keeping PostgreSQL 16.
- Aligned the pgvector column dimension with the eight-dimensional deterministic embedding generator.
- Configured Docker Compose to use persistent PostgreSQL semantic indexing while retaining the in-memory default for isolated tests.

## 0.1.0 - 2026-07-10

### Added

- ASP.NET Core REST API with Swagger/OpenAPI and health checks.
- Python FastAPI service with health and indexing-boundary endpoints.
- Docker Compose environment with PostgreSQL, Redis, Web UI, API, and FastAPI services.
- Local document upload and storage workflow.
- PostgreSQL-backed document metadata repository.
- Plain-text extraction and fixed-size chunking.
- Deterministic local embedding generation.
- In-memory semantic index with similarity-based ranking.
- Semantic search endpoint with source metadata.
- Deterministic source-aware ask endpoint.
- Web UI for health, upload, listing, search, questions, and source inspection.
- Sample documents and an end-to-end demo script.
- API integration tests.
- GitHub Actions validation for tests, Docker Compose configuration, and container builds.
- Architecture, security, API, local-development, and operations documentation.

### Known Limitations

- Semantic-index records were not durable across API restarts in version 0.1.0.
- Document processing is synchronous.
- The FastAPI service does not yet perform extraction, embeddings, retrieval, or answer generation.
- Authentication, authorization, tenant isolation, and audit logging are not implemented.
- Docker Compose uses development defaults and exposed local ports.
