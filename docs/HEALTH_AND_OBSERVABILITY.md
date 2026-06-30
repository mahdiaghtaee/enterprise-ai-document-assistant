# Health and Observability Notes

This document defines the expected health and observability direction for the Enterprise AI Document Assistant.

## Goal

A production-oriented backend project should make it easy to understand whether services are running, whether requests are failing, and where document-processing work is delayed.

## Health Checks

### API Health

The ASP.NET Core API should expose a health endpoint for deployment and local validation.

Suggested endpoint:

```text
GET /health
```

Expected purpose:

- confirm the API process is running
- confirm required dependencies are reachable
- support Docker, CI, and deployment checks

### AI Service Health

The Python FastAPI service should expose its own health endpoint.

Suggested endpoint:

```text
GET /health
```

Expected purpose:

- confirm the AI service is running
- confirm model or processing dependencies are available when needed
- help diagnose service-to-service communication issues

### Dependency Health

Future health checks should include:

- PostgreSQL connection
- Redis connection
- document storage path access
- vector store connection
- AI service availability

## Structured Logging

Logs should include structured fields such as:

- request ID
- correlation ID
- document ID
- tenant or workspace ID in future versions
- processing status
- endpoint name
- elapsed time
- error category

## Document Processing Status

Document processing should expose clear states:

```text
Uploaded
Queued
Processing
Indexed
Failed
```

These states help clients understand where each document is in the workflow.

## Future Metrics

Useful future metrics:

- document upload count
- failed document processing count
- average indexing time
- search request count
- RAG answer request count
- average response time
- AI service failure count

## Audit and Compliance Direction

For enterprise clients, future versions should consider:

- who uploaded a document
- who asked a question
- which documents were retrieved
- which answer was generated
- which source chunks supported the answer

## Portfolio Message

These notes show that the project is designed with production operation in mind, not only local AI experimentation.
