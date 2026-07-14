# Changelog

All notable changes to this project are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project intends to use semantic versioning once tagged releases begin.

## Unreleased

### Added

- Configurable local ports and PostgreSQL development credentials through `.env`.
- Expanded local-development and troubleshooting documentation.
- Explicit documentation of the current .NET document pipeline and the FastAPI service boundary.

### Changed

- Clarified that extraction, chunking, deterministic embeddings, semantic retrieval, and answer construction currently run in the ASP.NET Core API.
- Replaced the stale feature roadmap with implementation-based milestones.
- Moved resume, interview, social-post, and repository-visibility notes out of the software repository.

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

- Semantic-index records are not durable across API restarts.
- Document processing is synchronous.
- The FastAPI service does not yet perform extraction, embeddings, retrieval, or answer generation.
- Authentication, authorization, tenant isolation, and audit logging are not implemented.
- Docker Compose uses development defaults and exposed local ports.
