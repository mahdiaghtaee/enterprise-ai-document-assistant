# Architecture Diagram

This diagram shows the intended system shape for the current portfolio-ready version of Enterprise AI Document Assistant.

```mermaid
flowchart TD
    User[User / Reviewer]
    Browser[Web UI / API Client]
    Api[ASP.NET Core API]
    Ai[Python FastAPI AI Service]
    Db[(PostgreSQL)]
    Redis[(Redis)]
    Storage[Local Document Storage]
    Index[Semantic Index]

    User --> Browser
    Browser --> Api
    Api --> Storage
    Api --> Db
    Api --> Redis
    Api --> Ai
    Api --> Index
    Ai --> Api

    subgraph Document Flow
        Upload[Upload Document]
        Extract[Extract Text]
        Chunk[Chunk Text]
        Embed[Generate Embeddings]
        Search[Semantic Search]
        Ask[Ask Question]
        Answer[Answer with Sources]
    end

    Upload --> Extract --> Chunk --> Embed --> Search --> Ask --> Answer
```

## Current Implementation

The current implementation already covers:

- ASP.NET Core API
- Python FastAPI service
- Docker Compose environment
- Document upload flow
- Text extraction and chunking
- Deterministic local embeddings
- In-memory semantic index
- Search endpoint
- Ask endpoint with source matches

## Planned Implementation

The next practical improvements are:

- Web UI
- PostgreSQL-backed metadata repository
- Persistent vector storage
- Real embedding provider abstraction
- Authentication
- Background indexing
- Observability
