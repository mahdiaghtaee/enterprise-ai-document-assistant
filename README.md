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
- Sample business documents
- Swagger/OpenAPI documentation
- Health check endpoints
- GitHub Actions CI foundation
- MIT license
- Contribution and issue templates

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

### v0.1 - Portfolio Ready

- [x] Clear README
- [x] Architecture diagram
- [x] Runnable local demo
- [x] MIT license
- [x] Contributing guide
- [x] Code of conduct
- [x] Issue templates
- [x] Pull request template
- [x] Release notes draft

### v0.2 - Usable MVP

- [ ] Simple Web UI
- [ ] Document upload screen
- [ ] Document list screen
- [ ] Search screen
- [ ] Ask/chat screen
- [ ] Source chunk viewer
- [ ] PostgreSQL-backed document metadata repository
- [ ] More integration tests

### v1.0 - Production Portfolio Release

- [ ] Authentication and role-based access control
- [ ] OpenTelemetry observability foundation
- [ ] Audit logging direction
- [ ] Background indexing workflow
- [ ] Health and diagnostics hardening
- [ ] Docker Compose hardening
- [ ] CI/CD quality gates
- [ ] First stable release

---

## Documentation and Demo Assets

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — technical architecture and service responsibilities
- [`docs/ARCHITECTURE_DIAGRAM.md`](docs/ARCHITECTURE_DIAGRAM.md) — Mermaid architecture diagram
- [`docs/PHASE_0_1_PLAN.md`](docs/PHASE_0_1_PLAN.md) — practical phase 0 and phase 1 execution plan
- [`docs/LOCAL_DEVELOPMENT.md`](docs/LOCAL_DEVELOPMENT.md) — local setup, Docker Compose run steps, and troubleshooting
- [`docs/API_EXAMPLES.md`](docs/API_EXAMPLES.md) — current health, upload, search, and ask endpoint examples
- [`docs/RAG_ASK_ENDPOINT.md`](docs/RAG_ASK_ENDPOINT.md) — implementation plan and behavior for the RAG ask endpoint
- [`docs/RELEASE_NOTES_v0.1.0.md`](docs/RELEASE_NOTES_v0.1.0.md) — release notes draft for the first milestone
- [`scripts/demo_flow.py`](scripts/demo_flow.py) — runnable end-to-end local demo script
- [`scripts/demo-flow.md`](scripts/demo-flow.md) — manual end-to-end local demo flow
- [`docs/DEMO_SCENARIO.md`](docs/DEMO_SCENARIO.md) — client-facing business demo narrative
- [`docs/SWAGGER_DEMO_NOTES.md`](docs/SWAGGER_DEMO_NOTES.md) — how to present the API through Swagger/OpenAPI
- [`docs/HEALTH_AND_OBSERVABILITY.md`](docs/HEALTH_AND_OBSERVABILITY.md) — health, logging, metrics, and audit direction
- [`samples/sample-policy.txt`](samples/sample-policy.txt) — sample document for upload and RAG demo flow
- [`samples/hr-policy.txt`](samples/hr-policy.txt) — sample HR policy document
- [`samples/contract-policy.txt`](samples/contract-policy.txt) — sample contract review policy document

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
