# Swagger Demo Notes

This document explains how to present the API through Swagger/OpenAPI during a freelance client review.

## Demo Objective

Use Swagger to show that the project is structured like a real backend product, not only an AI experiment.

The demo should communicate:

- The system has clear API boundaries
- The backend can support document upload workflows
- The architecture is ready for search and RAG endpoints
- The project can be extended into a client-specific internal AI assistant

## Suggested Demo Order

### 1. Start the services

```bash
docker compose up --build
```

### 2. Open Swagger

Open the ASP.NET Core API Swagger page in the browser.

Suggested local URL:

```text
http://localhost:5000/swagger
```

The API runs inside the container on port `8080`, but Docker Compose publishes it to the host on port `5000`.

### 3. Show health endpoint

Purpose: prove the API is running.

Suggested endpoint:

```text
GET http://localhost:5000/health
```

Expected message to client:

```text
This endpoint is used by developers and deployment systems to confirm that the backend service is healthy.
```

### 4. Show document metadata endpoint

Purpose: explain how the backend tracks documents.

Expected message to client:

```text
The backend should not only send files to AI. It should track document metadata, processing status, ownership, and future access rules.
```

### 5. Show document upload endpoint

Purpose: explain the business workflow.

Expected message to client:

```text
This is where a company user uploads an internal document such as a policy, report, contract, or knowledge-base file.
```

Use the sample document:

```text
samples/sample-policy.txt
```

### 6. Show planned search and ask endpoints

Purpose: explain the RAG path.

Expected message to client:

```text
After documents are indexed, users can search internal knowledge or ask questions. The answer should be grounded in retrieved document chunks and include source references.
```

## Demo Questions

Use these questions with `samples/sample-policy.txt`:

```text
Who needs to review vendor contracts?
```

```text
When is Finance approval required?
```

```text
What must happen before the contract is signed?
```

## What to Emphasize to Clients

- This is a backend foundation for private enterprise AI.
- The project separates business APIs from AI processing.
- The architecture supports future authentication, roles, tenants, and audit logs.
- RAG is valuable because answers can be linked back to source documents.
- The same architecture can be adapted for HR, legal, finance, education, healthcare, or internal knowledge-base systems.

## Screenshot Checklist

When screenshots are added later, capture:

- Swagger API overview
- Health endpoint response
- Document metadata endpoint
- Document upload endpoint
- Example upload response
- Search or ask endpoint once implemented
- Docker Compose running services

## Portfolio Use

These notes help turn a technical repository into a repeatable sales demo. The goal is to show potential freelance clients a clear path from business problem to working backend architecture.
