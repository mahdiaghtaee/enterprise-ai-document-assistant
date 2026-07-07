# Enterprise AI Document Assistant

![CI](https://github.com/mahdiaghtaee/enterprise-ai-document-assistant/actions/workflows/ci.yml/badge.svg)

A backend portfolio project for building enterprise document assistants with **ASP.NET Core**, **Python FastAPI**, **PostgreSQL**, **Redis**, **Docker**, and a Retrieval-Augmented Generation architecture.

This repository demonstrates how business documents can move through upload, text extraction, chunking, embedding, semantic search, and question answering with source matches.

---

## Project Value

This project is designed to show practical backend and AI engineering skills:

- ASP.NET Core API design
- Python FastAPI service integration
- Docker Compose development workflow
- PostgreSQL and Redis infrastructure
- Document upload and processing workflow
- Semantic retrieval and RAG-style answer flow
- Portfolio-ready documentation and demo material

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
- Runnable Python demo script
- Swagger/OpenAPI documentation
- Health check endpoints
- GitHub Actions CI foundation

---

## Current RAG Scope

The current ask endpoint is deterministic and local. It does not call an external LLM provider yet.

The current flow is:

```text
Upload document -> Extract text -> Chunk -> Embed -> Search -> Ask -> Return answer with sources
```

Future versions can replace the deterministic answer builder with an external or local model provider.

---

## Execution Roadmap

### v0.1 - Foundation

- [ ] Authentication and role-based access control
- [x] Semantic vector search
- [x] RAG ask endpoint with source attribution
- [x] Document chunking and embedding pipeline

### v0.2 - Enterprise Readiness

- [ ] PostgreSQL-backed document metadata repository
- [x] End-to-end demo script
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
- [`scripts/demo_flow.py`](scripts/demo_flow.py) — runnable end-to-end local demo script
- [`scripts/demo-flow.md`](scripts/demo-flow.md) — manual end-to-end local demo flow
- [`docs/DEMO_SCENARIO.md`](docs/DEMO_SCENARIO.md) — client-facing business demo narrative
- [`docs/SWAGGER_DEMO_NOTES.md`](docs/SWAGGER_DEMO_NOTES.md) — how to present the API through Swagger/OpenAPI
- [`docs/HEALTH_AND_OBSERVABILITY.md`](docs/HEALTH_AND_OBSERVABILITY.md) — health, logging, metrics, and audit direction
- [`samples/sample-policy.txt`](samples/sample-policy.txt) — sample document for upload and RAG demo flow

---

## Getting Started

```bash
docker compose up --build
```

Open Swagger:

```text
http://localhost:5000/swagger
```

Run the end-to-end demo:

```bash
python scripts/demo_flow.py
```

Or follow the manual guide:

```text
scripts/demo-flow.md
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
