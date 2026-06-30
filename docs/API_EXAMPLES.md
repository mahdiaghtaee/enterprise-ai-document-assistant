# API Examples

This document describes the intended client-facing API flow for the Enterprise AI Document Assistant.

The examples are written as portfolio/demo documentation. Endpoint names may be adjusted as implementation evolves.

## Base URL

```text
http://localhost:8080
```

## Health Check

Use this endpoint to verify that the backend API is running.

```bash
curl http://localhost:8080/health
```

Expected response:

```json
{
  "status": "Healthy"
}
```

## Upload a Document

Business purpose: allow a company user to upload a PDF, report, policy, contract, or knowledge-base document.

```bash
curl -X POST http://localhost:8080/api/documents \
  -F "file=@sample-policy.pdf" \
  -F "title=Internal Policy Document" \
  -F "department=Operations"
```

Expected response shape:

```json
{
  "documentId": "doc_12345",
  "title": "Internal Policy Document",
  "status": "Uploaded",
  "message": "Document received and queued for processing."
}
```

## List Documents

Business purpose: show uploaded documents and their indexing status.

```bash
curl http://localhost:8080/api/documents
```

Expected response shape:

```json
[
  {
    "documentId": "doc_12345",
    "title": "Internal Policy Document",
    "status": "Indexed",
    "createdAt": "2026-06-30T00:00:00Z"
  }
]
```

## Search Documents

Business purpose: retrieve relevant internal knowledge before answering a user question.

```bash
curl -X POST http://localhost:8080/api/search \
  -H "Content-Type: application/json" \
  -d '{
    "query": "What is the approval process for vendor contracts?",
    "topK": 5
  }'
```

Expected response shape:

```json
{
  "query": "What is the approval process for vendor contracts?",
  "results": [
    {
      "documentId": "doc_12345",
      "title": "Internal Policy Document",
      "score": 0.91,
      "snippet": "Vendor contracts must be reviewed by Operations and Finance before approval."
    }
  ]
}
```

## Ask a Question with RAG

Business purpose: answer a business question using private company documents and include source attribution.

```bash
curl -X POST http://localhost:8080/api/ask \
  -H "Content-Type: application/json" \
  -d '{
    "question": "Who needs to approve vendor contracts?",
    "topK": 5
  }'
```

Expected response shape:

```json
{
  "answer": "Vendor contracts should be reviewed by Operations and Finance before final approval.",
  "sources": [
    {
      "documentId": "doc_12345",
      "title": "Internal Policy Document",
      "chunkId": "chunk_001"
    }
  ]
}
```

## Demo Narrative for Clients

A simple client demo can follow this story:

1. A company uploads policy documents.
2. The backend stores document metadata.
3. The AI service extracts and prepares text.
4. The system indexes the document for semantic retrieval.
5. A user asks a business question.
6. The assistant answers using retrieved internal document context.
7. The answer includes source references so the user can verify it.

## Why These Examples Matter

These examples show the difference between a simple chatbot and an enterprise AI assistant. The project is designed around document ownership, backend APIs, retrieval, traceability, and production-oriented architecture.
