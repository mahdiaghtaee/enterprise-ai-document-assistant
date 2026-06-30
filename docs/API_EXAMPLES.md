# API Examples

This document describes the current and planned client-facing API flow for the Enterprise AI Document Assistant.

The API runs inside the container on port `8080`, and Docker Compose publishes it to the host on port `5000`.

## Base URL

```text
http://localhost:5000
```

## Health Check

Use this endpoint to verify that the backend API is running.

```bash
curl http://localhost:5000/health
```

Expected response shape:

```json
{
  "service": "document-api",
  "status": "ok",
  "checkedAt": "2026-06-30T00:00:00+00:00"
}
```

## Create Document Metadata

Business purpose: register document metadata before or without uploading a physical file.

```bash
curl -X POST http://localhost:5000/api/documents \
  -H "Content-Type: application/json" \
  -d '{
    "fileName": "sample-policy.txt",
    "contentType": "text/plain"
  }'
```

Expected response shape:

```json
{
  "id": "generated-document-id",
  "fileName": "sample-policy.txt",
  "contentType": "text/plain",
  "status": "metadata-only"
}
```

## Upload a Document

Business purpose: allow a company user to upload a PDF, report, policy, contract, or knowledge-base document.

```bash
curl -X POST http://localhost:5000/api/documents/upload \
  -F "file=@samples/sample-policy.txt;type=text/plain"
```

Expected response shape:

```json
{
  "documentId": "generated-document-id",
  "fileName": "sample-policy.txt",
  "status": "uploaded",
  "indexingStatus": "queued"
}
```

## List Documents

Business purpose: show uploaded documents and their indexing status.

```bash
curl http://localhost:5000/api/documents
```

Expected response shape:

```json
[
  {
    "id": "generated-document-id",
    "fileName": "sample-policy.txt",
    "contentType": "text/plain",
    "status": "uploaded"
  }
]
```

## Planned: Search Documents

Business purpose: retrieve relevant internal knowledge before answering a user question.

```bash
curl -X POST http://localhost:5000/api/search \
  -H "Content-Type: application/json" \
  -d '{
    "query": "What is the approval process for vendor contracts?",
    "topK": 5
  }'
```

Expected future response shape:

```json
{
  "query": "What is the approval process for vendor contracts?",
  "results": [
    {
      "documentId": "generated-document-id",
      "title": "sample-policy.txt",
      "score": 0.91,
      "snippet": "Vendor contracts must be reviewed by Operations and Finance before approval."
    }
  ]
}
```

## Planned: Ask a Question with RAG

Business purpose: answer a business question using private company documents and include source attribution.

```bash
curl -X POST http://localhost:5000/api/ask \
  -H "Content-Type: application/json" \
  -d '{
    "question": "Who needs to approve vendor contracts?",
    "topK": 5
  }'
```

Expected future response shape:

```json
{
  "answer": "Vendor contracts should be reviewed by Operations and Finance before final approval.",
  "sources": [
    {
      "documentId": "generated-document-id",
      "title": "sample-policy.txt",
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
