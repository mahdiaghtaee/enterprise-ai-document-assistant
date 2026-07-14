# Architecture Diagram

The diagram below distinguishes current executable responsibilities from planned extensions.

```mermaid
flowchart TD
    User[User / API Client]
    Web[Web UI]
    Api[ASP.NET Core API]
    Db[(PostgreSQL metadata)]
    Redis[(Redis - future use)]
    Storage[Local document storage]
    Extract[Plain-text extraction]
    Chunk[Fixed-size chunking]
    Embed[Deterministic embeddings]
    Index[In-memory semantic index]
    Answer[Source-aware answer builder]
    Ai[FastAPI service boundary]

    User --> Web
    Web --> Api
    User --> Api

    Api --> Storage
    Api --> Db
    Api --> Redis
    Api --> Extract
    Extract --> Chunk
    Chunk --> Embed
    Embed --> Index
    Index --> Answer
    Api --> Ai
```

## Current Implementation

### ASP.NET Core API

- upload validation and local storage;
- PostgreSQL-backed document metadata;
- plain-text extraction and chunking;
- deterministic embedding generation;
- in-memory semantic indexing and ranking;
- search and source-aware ask endpoints;
- HTTP call to the FastAPI indexing boundary.

### FastAPI Service

- health endpoint;
- placeholder indexing endpoint returning a queued status.

The FastAPI service does not currently perform extraction, embeddings, retrieval, or answer generation.

### Infrastructure

- PostgreSQL is actively used for document metadata;
- Redis is present but not yet used by the workflow;
- Docker Compose starts the Web UI, API, FastAPI service, PostgreSQL, and Redis.

## Planned Extensions

```mermaid
flowchart LR
    Upload[Upload] --> Jobs[(Persistent indexing jobs)]
    Jobs --> Worker[Background worker]
    Worker --> Parser[Document parser / OCR]
    Parser --> Provider[Embedding provider]
    Provider --> Pg[(PostgreSQL + pgvector)]
    Pg --> Retrieve[Semantic retrieval]
    Retrieve --> Model[Local or external model provider]
    Model --> Grounded[Answer with sources]
```

Planned work includes pgvector persistence, background processing, access control, provider integrations, audit events, and observability. See [ROADMAP.md](ROADMAP.md).
