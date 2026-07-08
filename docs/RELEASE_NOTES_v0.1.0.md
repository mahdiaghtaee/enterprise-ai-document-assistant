# Release Notes - v0.1.0

This is the first portfolio-ready milestone for Enterprise AI Document Assistant.

## Focus

The goal of this release is to present a clear backend foundation for document upload, semantic search, and RAG-style question answering.

## Included

- ASP.NET Core backend API
- Python FastAPI AI service
- Docker Compose local environment
- PostgreSQL and Redis infrastructure services
- Document upload API
- Text extraction pipeline
- Document chunking
- Deterministic local embedding generation
- In-memory semantic search
- Ask endpoint with source matches
- Swagger/OpenAPI documentation
- Health endpoint
- Runnable Python demo script
- Architecture documentation
- Contribution and issue templates

## Current Limitations

- The ask endpoint is deterministic and local.
- No external LLM provider is connected yet.
- Document metadata still needs PostgreSQL-backed persistence.
- Web UI is planned for the next milestone.

## Next Milestone

v0.2.0 should focus on a usable MVP:

- Web UI
- Document list
- Upload screen
- Search screen
- Ask/chat screen
- PostgreSQL-backed document metadata
- More sample documents
- Better integration tests
