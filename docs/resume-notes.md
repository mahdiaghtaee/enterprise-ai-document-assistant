# Resume notes

This project is meant to show practical backend and AI integration skills rather than a toy CRUD application.

## What it demonstrates

- ASP.NET Core API design
- Minimal API endpoints
- File upload workflow
- Service abstraction and dependency injection
- Separate Python AI service
- Docker-based local development
- PostgreSQL and Redis as part of the system architecture
- A clear path toward RAG, document indexing and background processing

## Suggested resume bullet

Built an enterprise document assistant using ASP.NET Core and FastAPI, with Docker-based local development, document upload workflow, service-to-service communication, and planned RAG-based indexing for question answering over internal documents.

## Good interview talking points

- Why the .NET API and Python AI service are separate
- Why the first repository implementation is in-memory
- How the upload flow will evolve into a queue-based indexing flow
- How PostgreSQL/pgvector can be used for metadata and vector search
- How Redis can later support background job coordination
