# Enterprise AI Document Assistant

![CI](https://github.com/mahdiaghtaee/enterprise-ai-document-assistant/actions/workflows/ci.yml/badge.svg)

A production-oriented backend project for building secure enterprise document assistants with **ASP.NET Core**, **Python FastAPI**, **PostgreSQL**, **Redis**, **Docker**, and a Retrieval-Augmented Generation (RAG) architecture.

This repository is designed as a portfolio-grade implementation for organizations that want to search, understand, and query internal documents such as PDFs, contracts, reports, policies, and knowledge-base files.

---

## Current Features

- ASP.NET Core REST API
- Python FastAPI AI service
- Docker Compose environment
- PostgreSQL database service
- Redis cache/message infrastructure service
- Document upload API foundation
- Text extraction and chunking pipeline
- Deterministic local embedding generation
- In-memory semantic index store
- Semantic document search endpoint
- RAG-style ask endpoint with source attribution
- Swagger/OpenAPI documentation
- Health check endpoints
- GitHub Actions CI foundation

---

## Execution Roadmap

### v0.1 - Foundation

- [ ] Authentication and role-based access control
- [x] Semantic vector search
- [x] RAG ask endpoint with source attribution
- [x] Document chunking and embedding pipeline

### v0.2 - Enterprise Readiness

- [ ] OpenTelemetry observability foundation
- [ ] Audit logging direction
- [ ] Background indexing workflow
- [ ] Health and diagnostics hardening

### v1.0 - Production Portfolio Release

- [ ] Integration tests
- [ ] Docker Compose hardening
- [ ] CI/CD quality gates
- [ ] Complete documentation and demo flow
- [ ] First stable release

---

## Documentation and Demo Assets

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — technical architecture and service responsibilities
- [`docs/LOCAL_DEVELOPMENT.md`](docs/LOCAL_DEVELOPMENT.md) — local setup, Docker Compose run steps, and troubleshooting
- [`docs/API_EXAMPLES.md`](docs/API_EXAMPLES.md) — current health, upload, search, and ask endpoint examples
- [`docs/RAG_ASK_ENDPOINT.md`](docs/RAG_ASK_ENDPOINT.md) — implementation plan and behavior for the RAG ask endpoint
- [`docs/DEMO_SCENARIO.md`](docs/DEMO_SCENARIO.md) — client-facing business demo narrative
- [`docs/SWAGGER_DEMO_NOTES.md`](docs/SWAGGER_DEMO_NOTES.md) — how to present the API through Swagger/OpenAPI
- [`docs/HEALTH_AND_OBSERVABILITY.md`](docs/HEALTH_AND_OBSERVABILITY.md) — health, logging, metrics, and audit direction
- [`samples/sample-policy.txt`](samples/sample-policy.txt) — sample document for upload and RAG demo flow

---

## Getting Started

### Prerequisites

- Docker
- Docker Compose
- .NET SDK
- Python 3.11+

### Run locally

```bash
docker compose up --build
```

After the services start, open the API documentation:

```text
http://localhost:5000/swagger
```

---

## Recommended Demo Flow

```text
Upload document
   -> Store metadata
   -> Extract text
   -> Split text into chunks
   -> Generate embeddings
   -> Store vectors
   -> Ask a question
   -> Retrieve relevant context
   -> Generate grounded answer with sources
```

---

## Technology Stack

| Area | Technology |
|---|---|
| Backend API | ASP.NET Core |
| AI Service | Python, FastAPI |
| Database | PostgreSQL |
| Cache / Background Infrastructure | Redis |
| API Documentation | Swagger / OpenAPI |
| Local Environment | Docker Compose |
| CI | GitHub Actions |
| AI Architecture | RAG, Semantic Search, Document Indexing |

---

## Author

**Mahdi Aghtaee**  
Senior C#/.NET Developer focused on enterprise backend systems, AI-enabled applications, RAG architecture, SQL systems, and production-oriented software design.
