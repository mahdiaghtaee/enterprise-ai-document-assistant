# Freelance Portfolio Execution Plan

This plan turns the repository into a client-facing technical portfolio asset.

## Goal

Position this project as a proof of capability for freelance work in:

- Enterprise AI assistants
- RAG-based document question answering
- ASP.NET Core backend development
- FastAPI AI microservices
- Dockerized backend systems
- PostgreSQL / Redis infrastructure
- SQL-backed business applications

## Management Principle

Every improvement must answer one client question:

> Can this developer understand a business problem and deliver a reliable technical solution?

## Phase 1 — Trust and Positioning

### Objective

Make the repository understandable within 30 seconds for a potential client.

### Deliverables

- Improve README for business value and freelance positioning
- Add Achievements section
- Add technology stack table
- Add architecture overview
- Add recommended demo flow
- Add clear project status and next milestone

### Acceptance Criteria

- A non-technical client understands what problem the project solves
- A technical client understands the architecture
- The project clearly shows which freelance services it represents

## Phase 2 — Demo Readiness

### Objective

Make the repository runnable and demonstrable.

### Deliverables

- Add `.env.example`
- Add API usage examples
- Add sample document upload request
- Add sample response formats
- Add screenshots from Swagger or API flow
- Add a short demo section to README

### Acceptance Criteria

- A visitor can clone and run the project locally
- A potential client can see the expected product flow
- The project feels like a product foundation, not a code dump

## Phase 3 — Engineering Credibility

### Objective

Show that the project follows professional engineering practices.

### Deliverables

- Add GitHub Actions build workflow
- Add integration test foundation
- Add structured logging notes
- Add health check documentation
- Add architecture decision records under `docs/adr/`

### Acceptance Criteria

- The repository shows testability
- The repository shows maintainability
- The repository shows production-oriented thinking

## Phase 4 — AI/RAG Capability

### Objective

Turn the repository into a real AI document assistant foundation.

### Deliverables

- Add document extraction pipeline
- Add chunking strategy
- Add embedding provider abstraction
- Add vector store abstraction
- Add semantic search endpoint
- Add RAG question-answering endpoint
- Add source attribution in answers

### Acceptance Criteria

- The demo supports upload -> index -> search -> answer
- RAG behavior is clear and explainable
- The architecture can be adapted for client projects

## Phase 5 — Freelance Conversion

### Objective

Make the GitHub profile help generate freelance leads.

### Deliverables

- Create GitHub profile README
- Pin the strongest repositories
- Add service-oriented project descriptions
- Add LinkedIn/service contact path
- Add case-study style summaries

### Acceptance Criteria

- The profile communicates a focused freelance offer
- The strongest projects are visible first
- A potential client knows what to contact Mahdi for

## Current Priority

The current priority is **Phase 1 + Phase 2** for `enterprise-ai-document-assistant`.

After that, the second priority is improving `enterprise-ai-toolkit` as a reusable .NET AI library that supports the same freelance positioning.
