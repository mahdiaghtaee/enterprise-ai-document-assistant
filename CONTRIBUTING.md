# Contributing

Thanks for your interest in contributing to Enterprise AI Document Assistant.

This project is a portfolio-grade open-source backend for document upload, semantic search, and RAG-style question answering. Contributions should keep the project practical, easy to run, and explainable.

## Project Direction

The project focuses on:

- ASP.NET Core backend APIs
- Python FastAPI AI service integration
- Document upload and text processing
- Semantic search and grounded answers
- Docker-based local development
- Production-oriented architecture

## Local Development

Start the stack:

```bash
docker compose up --build
```

Open Swagger:

```text
http://localhost:5000/swagger
```

Run the demo:

```bash
python scripts/demo_flow.py
```

## Good First Contributions

Good starter contributions include:

- Improving documentation
- Adding sample documents
- Improving API examples
- Adding tests around upload, search, or ask flows
- Improving validation and error messages
- Improving Docker setup and health checks

## Pull Request Guidelines

Before opening a pull request:

1. Keep the change focused.
2. Explain the business or technical reason for the change.
3. Include testing notes.
4. Update documentation when behavior changes.
5. Avoid mixing unrelated refactoring with feature work.

## Commit Messages

Use short, clear commit messages. Conventional Commits are welcome where practical:

- `feat:`
- `fix:`
- `docs:`
- `refactor:`
- `test:`
- `chore:`

Examples:

```text
Add ask endpoint validation tests
Document local demo flow
Add sample HR policy document
```

## Code Quality

- Prefer small, reviewable changes.
- Keep architecture consistent with ADR documents.
- Add tests for new functionality when possible.
- Avoid introducing breaking API changes without documentation.
