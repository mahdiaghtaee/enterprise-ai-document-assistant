# API Examples

This document describes the current client-facing API flow for the Enterprise AI Document Assistant.

The API runs inside the container on port `8080`, and Docker Compose publishes it to the host on port `5000`.

## Base URL

```text
http://localhost:5000
```

## Health Check

```bash
curl http://localhost:5000/health
```

Expected response shape:

```json
{
  "service": "document-api",
  "status": "ok",
  "checkedAt": "2026-07-27T00:00:00+00:00"
}
```

## Create Document Metadata

This endpoint registers metadata without enqueueing a physical file for processing.

```bash
curl -X POST http://localhost:5000/api/documents \
  -H "Content-Type: application/json" \
  -d '{
    "fileName": "sample-policy.txt",
    "contentType": "text/plain"
  }'
```

The resulting document has no ingestion job. Calling its processing-status endpoint returns `404`.

## Upload and Enqueue a Document

Only the currently supported plain-text upload path is accepted.

```bash
curl -i -X POST http://localhost:5000/api/documents/upload \
  -F "file=@samples/sample-policy.txt;type=text/plain"
```

A valid request returns `202 Accepted` after the file is stored and the document metadata plus initial `Pending` job are committed atomically.

Expected response shape:

```json
{
  "id": "generated-document-id",
  "fileName": "sample-policy.txt",
  "status": "uploaded",
  "indexingStatus": "queued_for_background_processing",
  "textExtraction": null,
  "chunking": null,
  "embeddings": null,
  "ingestionJobId": 123,
  "processingStatusUrl": "/api/documents/generated-document-id/processing-status"
}
```

The upload response intentionally does not include extraction or embedding results because those operations run in the hosted worker.

## Poll Processing Status

```bash
curl http://localhost:5000/api/documents/generated-document-id/processing-status
```

Example while processing:

```json
{
  "jobId": 123,
  "documentId": "generated-document-id",
  "status": "Processing",
  "attemptCount": 1,
  "maxAttempts": 3,
  "availableAt": "2026-07-27T00:00:00+00:00",
  "startedAt": "2026-07-27T00:00:01+00:00",
  "completedAt": null,
  "failedAt": null,
  "lastErrorCode": null,
  "lastErrorSummary": null,
  "updatedAt": "2026-07-27T00:00:01+00:00",
  "isTerminal": false
}
```

Example after successful indexing:

```json
{
  "jobId": 123,
  "documentId": "generated-document-id",
  "status": "Completed",
  "attemptCount": 1,
  "maxAttempts": 3,
  "availableAt": "2026-07-27T00:00:00+00:00",
  "startedAt": "2026-07-27T00:00:01+00:00",
  "completedAt": "2026-07-27T00:00:02+00:00",
  "failedAt": null,
  "lastErrorCode": null,
  "lastErrorSummary": null,
  "updatedAt": "2026-07-27T00:00:02+00:00",
  "isTerminal": true
}
```

Clients should not submit search or ask requests that depend on the uploaded document until its job reaches `Completed`.

## List Documents

```bash
curl http://localhost:5000/api/documents
```

Document status reflects background-processing progress and may be `uploaded`, `processing`, `retry-pending`, `indexed`, or `failed`.

```json
[
  {
    "id": "generated-document-id",
    "fileName": "sample-policy.txt",
    "contentType": "text/plain",
    "sizeInBytes": 1200,
    "storagePath": "/app/storage/documents/stored-name.txt",
    "status": "indexed",
    "createdAt": "2026-07-27T00:00:00+00:00"
  }
]
```

## Search Documents

```bash
curl -X POST http://localhost:5000/api/documents/search \
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
  "resultCount": 1,
  "results": [
    {
      "documentId": "generated-document-id",
      "fileName": "sample-policy.txt",
      "chunkIndex": 0,
      "text": "Vendor contracts must be reviewed by Operations and Finance before approval.",
      "score": 0.91
    }
  ]
}
```

## Ask a Grounded Question

The current implementation is deterministic and local. It retrieves indexed chunks and returns source attribution without calling an external language-model provider.

```bash
curl -X POST http://localhost:5000/api/documents/ask \
  -H "Content-Type: application/json" \
  -d '{
    "question": "Who needs to approve vendor contracts?",
    "topK": 5
  }'
```

Expected response shape:

```json
{
  "question": "Who needs to approve vendor contracts?",
  "answer": "Based on the indexed documents, the most relevant source is from sample-policy.txt: ...",
  "sourceCount": 1,
  "sources": [
    {
      "documentId": "generated-document-id",
      "fileName": "sample-policy.txt",
      "chunkIndex": 0,
      "score": 0.91,
      "text": "Vendor contracts must be reviewed by Operations and Finance before approval."
    }
  ]
}
```

## End-to-End Demo Sequence

1. Upload a supported document.
2. Read `processingStatusUrl` from the `202 Accepted` response.
3. Poll until the job reaches `Completed` or `Failed`.
4. Inspect the document list status.
5. Search the persistent semantic index.
6. Ask a grounded question and inspect the returned sources.
7. Restart the API container and verify that search still returns the indexed chunks.

The repository demo script performs this sequence automatically:

```bash
python scripts/demo_flow.py
```
