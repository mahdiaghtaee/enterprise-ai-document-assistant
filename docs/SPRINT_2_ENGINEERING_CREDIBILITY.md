# Sprint 2 — Engineering Credibility

## Goal

After Sprint 1 positioned the project for freelance clients, Sprint 2 focuses on engineering trust.

The objective is to show that the project is not only well-presented, but also follows professional backend engineering practices.

## Target Audience

- Freelance clients with technical reviewers
- Engineering managers
- CTOs evaluating implementation quality
- Backend developers reviewing project structure

## Outcomes

By the end of this sprint, the repository should show:

- Build automation
- Test readiness
- Clear quality checks
- Production-oriented health and logging notes
- A cleaner path toward actual document indexing and RAG implementation

## Deliverables

### 1. GitHub Actions workflow

Add a workflow that can run basic checks on pull requests and pushes to `main`.

Suggested workflow:

```text
.github/workflows/ci.yml
```

Expected jobs:

- Restore .NET dependencies
- Build .NET solution or projects
- Run tests when available
- Optionally validate Docker Compose configuration

### 2. Test project improvement

Add or prepare a test foundation for:

- API-level tests
- service-level tests
- document workflow tests
- AI service integration boundary tests

### 3. Health check documentation

Document health endpoints and what each service should expose.

### 4. Logging and observability notes

Add notes for:

- structured logs
- request correlation IDs
- document processing status
- future metrics
- future audit logs

### 5. Architecture decision records

Add an ADR folder:

```text
docs/adr/
```

Initial ADR candidates:

- Why ASP.NET Core for the main backend
- Why Python FastAPI for the AI service
- Why PostgreSQL and Redis
- Why RAG instead of a simple chatbot

## Definition of Done

- The repository has a visible CI workflow
- The documentation explains engineering decisions
- The project looks maintainable to a technical reviewer
- The next implementation path is clear

## Management Priority

High. Sprint 2 improves trust for technical freelance clients and supports higher-value project conversations.
