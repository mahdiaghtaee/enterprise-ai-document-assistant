# Architecture Overview

Enterprise AI Document Assistant uses a service-oriented structure. The design keeps business workflows in the .NET API and document intelligence concerns in a separate Python service.

## Architecture Goal

The project is designed as a backend foundation for a private enterprise AI assistant that can process internal documents and answer questions using retrieved document context.

## High-Level Flow

```text
User / Client
    |
    v
ASP.NET Core API
    |
    |-- Document upload
    |-- Metadata management
    |-- API contracts
    |-- Future authentication and authorization
    |
    +--> PostgreSQL
    |       - Document metadata
    |       - Processing status
    |       - Future user and workspace data
    |
    +--> Redis
    |       - Cache-ready infrastructure
    |       - Future background job coordination
    |
    v
Python AI Service
    |
    |-- Text extraction
    |-- Chunking
    |-- Embeddings
    |-- Semantic search
    |-- RAG answer generation
```

## Why .NET + Python?

The split is intentional:

- **ASP.NET Core** is a strong fit for stable APIs, authentication, business workflows, and enterprise integration.
- **Python** is a strong fit for AI libraries, document processing, embeddings, and experimentation.

This separation makes the project easier to explain to clients and easier to extend without mixing unrelated concerns.

## Main Components

### ASP.NET Core API

Responsible for:

- Receiving document uploads
- Managing document metadata
- Exposing client-facing endpoints
- Calling the AI service
- Later: authentication, authorization, audit logs, and workspace management

### Python AI Service

Responsible for:

- Document text extraction
- Text cleanup
- Chunk generation
- Embedding generation
- Semantic search
- Answer generation with source references

### PostgreSQL

Planned responsibilities:

- Document metadata
- User and workspace data
- Indexing state
- Conversation history
- Optional vector storage with pgvector

### Redis

Planned responsibilities:

- Background job coordination
- Indexing queue
- Caching short-lived AI responses or document states

### Vector Store

The vector store is planned as an abstraction so the project can support different backends later, such as:

- Qdrant
- PostgreSQL with pgvector
- Azure AI Search
- Other enterprise search systems

## Request Flow: Document Upload

```text
User uploads document
    |
    v
ASP.NET Core API validates request
    |
    v
Metadata is stored in PostgreSQL
    |
    v
Document processing task is prepared
    |
    v
FastAPI service extracts and chunks text
    |
    v
Embeddings are generated
    |
    v
Chunks are indexed for semantic search
```

## Request Flow: Ask a Question

```text
User asks a question
    |
    v
ASP.NET Core API receives request
    |
    v
AI service prepares query embedding
    |
    v
Relevant chunks are retrieved
    |
    v
RAG prompt is assembled
    |
    v
Answer is generated with source references
    |
    v
API returns grounded answer to user
```

## Design Principles

- Keep the first version small and demonstrable
- Prefer clear service boundaries over over-engineering
- Make every milestone useful for a real client conversation
- Add infrastructure only when the workflow needs it
- Keep setup simple with Docker Compose
- Preserve source attribution for generated answers

## Production Considerations

Future production versions should include:

- Authentication
- Role-based access control
- Tenant isolation
- Background workers
- Observability
- Structured logging
- Integration tests
- CI/CD
- Secure document storage
- Source attribution in all generated answers

## Freelance Use Case

This architecture can be adapted for clients who need:

- Internal document search
- AI support assistant over company policies
- Legal or accounting document analysis
- Knowledge base automation
- Private document Q&A tools

## Portfolio Message

This architecture demonstrates more than a chatbot. It shows an enterprise backend approach for AI systems, with service boundaries, infrastructure components, data persistence, future access control, and a clear RAG path.
