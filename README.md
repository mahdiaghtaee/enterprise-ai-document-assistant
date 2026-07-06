# Enterprise AI Document Assistant

![CI](https://github.com/mahdiaghtaee/enterprise-ai-document-assistant/actions/workflows/ci.yml/badge.svg)

A production-oriented backend project for building secure enterprise document assistants with **ASP.NET Core**, **Python FastAPI**, **PostgreSQL**, **Redis**, **Docker**, and a Retrieval-Augmented Generation (RAG) architecture.

This repository is designed as a portfolio-grade implementation for organizations that want to search, understand, and query internal documents such as PDFs, contracts, reports, policies, and knowledge-base files.

---

## What Problem This Solves

Many organizations store critical knowledge inside scattered documents. Teams often lose time searching through PDFs, internal reports, policy documents, and operational manuals.

This project provides the backend foundation for a private AI assistant that can:

- Upload and manage business documents
- Extract and prepare document text for indexing
- Store document metadata securely
- Support semantic search over internal knowledge
- Prepare retrieved context for RAG-based question answering
- Serve APIs that can be connected to web, mobile, or internal enterprise tools

---

## Freelance Value

This project demonstrates the type of work I can deliver for clients who need AI-enabled business systems, backend platforms, or internal automation tools.

### Services this project represents

- Enterprise AI assistant development
- RAG-based document question-answering systems
- ASP.NET Core backend API development
- FastAPI microservice development
- Dockerized backend deployment
- PostgreSQL and Redis integration
- AI architecture planning for internal knowledge systems
- Secure document-processing workflow design

---

## Achievements

- Designed a multi-service backend architecture combining **ASP.NET Core** and **Python FastAPI**.
- Added a Docker Compose based local development environment.
- Integrated PostgreSQL and Redis as production-style infrastructure components.
- Implemented the foundation for document upload and metadata management.
- Added Swagger/OpenAPI support for API exploration.
- Added health endpoints to support operational readiness.
- Added GitHub Actions CI foundation for build, test, and Docker Compose validation.
- Defined a clear roadmap for RAG, semantic search, authentication, background indexing, observability, and multi-tenancy.
- Structured the project as a portfolio-ready enterprise AI backend rather than a simple prototype.

---

## Current Features

- ASP.NET Core REST API
- Python FastAPI AI service
- Docker Compose environment
- PostgreSQL database service
- Redis cache/message infrastructure service
- Document upload API foundation
- Swagger/OpenAPI documentation
- Health check endpoints
- GitHub Actions CI foundation

---

## Execution Roadmap

### v0.1 - Foundation

- [ ] Authentication and role-based access control
- [ ] Semantic vector search
- [ ] RAG ask endpoint with source attribution
- [ ] Document chunking and embedding pipeline

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

## Portfolio Roadmap

See [`docs/FREELANCE_PORTFOLIO_PLAN.md`](docs/FREELANCE_PORTFOLIO_PLAN.md) for the managed execution plan.

---

## Documentation and Demo Assets

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — technical architecture and service responsibilities
- [`docs/LOCAL_DEVELOPMENT.md`](docs/LOCAL_DEVELOPMENT.md) — local setup, Docker Compose run steps, and troubleshooting
- [`docs/API_EXAMPLES.md`](docs/API_EXAMPLES.md) — current health/upload endpoints and planned search/RAG examples
- [`docs/DEMO_SCENARIO.md`](docs/DEMO_SCENARIO.md) — client-facing business demo narrative
- [`docs/SWAGGER_DEMO_NOTES.md`](docs/SWAGGER_DEMO_NOTES.md) — how to present the API through Swagger/OpenAPI
- [`docs/HEALTH_AND_OBSERVABILITY.md`](docs/HEALTH_AND_OBSERVABILITY.md) — health, logging, metrics, and audit direction
- [`samples/sample-policy.txt`](samples/sample-policy.txt) — sample document for upload and RAG demo flow

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

## Architecture Overview

```text
Client / Frontend
       |
       v
ASP.NET Core REST API
       |
       |---- PostgreSQL
       |        - Document metadata
       |        - Users / tenant data in future versions
       |
       |---- Redis
       |        - Background workflow support
       |        - Cache / queue-ready infrastructure
       |
       v
Python FastAPI AI Service
       |
       v
Document Processing / Indexing / RAG Pipeline
```

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

The intended end-to-end product flow is:

```text
Upload document
   -> Store metadata
   -> Extract text
   -> Split text into chunks
   -> Generate embeddings
   -> Store vectors
   -> Ask a question
   -> Retrieve relevant context
   -> Generate grounded answer with citations
```

---

## Why This Project Matters

This repository is built around a real business need: companies want AI assistants that understand their private documents without exposing sensitive data to uncontrolled tools.

The project shows how enterprise AI systems should be designed: with clear service boundaries, backend APIs, infrastructure services, future authentication, background processing, and observability in mind.

---

## Project Status

Active development. The current version provides the project foundation and development environment. The next milestone is the full document upload, extraction, indexing, and retrieval workflow.

---

## Author

**Mahdi Aghtaee**  
Senior C#/.NET Developer focused on enterprise backend systems, AI-enabled applications, RAG architecture, SQL systems, and production-oriented software design.

---

## Contact

For freelance projects related to .NET backend systems, enterprise AI assistants, RAG applications, document automation, or SQL-backed business platforms, please contact me through GitHub or LinkedIn.