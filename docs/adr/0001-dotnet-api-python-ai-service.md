# ADR 0001: Use ASP.NET Core for the main API and Python FastAPI for AI processing

## Status

Proposed

## Context

The project needs to support both enterprise backend workflows and AI/document-processing workflows.

These responsibilities are different:

- Business APIs, validation, future authentication, metadata management, and enterprise integration are backend engineering concerns.
- Text extraction, embeddings, semantic search, and RAG experimentation are AI engineering concerns.

Combining both concerns into one service would make the project harder to evolve and harder to explain to clients.

## Decision

Use **ASP.NET Core** as the main backend API and **Python FastAPI** as a separate AI service.

## Rationale

ASP.NET Core is a strong fit for:

- stable backend APIs
- enterprise integration
- authentication and authorization
- structured business workflows
- SQL-backed applications
- long-term maintainability

Python FastAPI is a strong fit for:

- document processing
- AI libraries
- embedding generation
- semantic search experimentation
- RAG workflows
- fast AI prototyping

## Consequences

### Positive

- Clear service boundaries
- Easier client explanation
- Better alignment with enterprise backend needs
- Easier future replacement of AI internals
- Better fit for portfolio positioning around .NET + AI

### Trade-offs

- Requires service-to-service communication
- Requires Docker Compose or another orchestration approach
- Requires careful API contracts between the .NET backend and AI service

## Portfolio Message

This decision demonstrates production-oriented architecture thinking. The project is not presented as a simple chatbot; it is structured as an enterprise backend system with a dedicated AI processing service.
