# Architecture Overview

Enterprise AI Document Assistant uses a simple service-oriented structure. The design keeps business workflows in the .NET API and document intelligence concerns in a separate Python service.

## High-Level Flow

```text
User / Client
    |
    v
ASP.NET Core API
    |
    |-- Document upload
    |-- Metadata management
    |-- Authentication and authorization
    |-- API contracts
    |
    +--> PostgreSQL
    |
    +--> Redis / background queue
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

## Design Principles

- Keep the first version small and demonstrable
- Prefer clear service boundaries over over-engineering
- Make every milestone useful for a real client conversation
- Add infrastructure only when the workflow needs it
- Keep setup simple with Docker Compose

## Freelance Use Case

This architecture can be adapted for clients who need:

- Internal document search
- AI support assistant over company policies
- Legal or accounting document analysis
- Knowledge base automation
- Private document Q&A tools
