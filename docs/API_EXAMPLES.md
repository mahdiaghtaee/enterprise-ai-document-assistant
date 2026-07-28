# API Examples

The API is published by Docker Compose at `http://localhost:5000`. The health endpoint is public; every document endpoint requires a JWT bearer token.

## Create a local token

```bash
TOKEN=$(python scripts/create_dev_token.py --user demo-user --role User)
```

Windows PowerShell:

```powershell
$TOKEN = python scripts/create_dev_token.py --user demo-user --role User
```

The helper and default signing key are for local development only.

## Health check

```bash
curl http://localhost:5000/health
```

## Current principal

```bash
curl http://localhost:5000/api/auth/me \
  -H "Authorization: Bearer $TOKEN"
```

Expected shape:

```json
{
  "userId": "demo-user",
  "roles": ["User"],
  "canAccessAllDocuments": false
}
```

## Create document metadata

This registers metadata owned by the authenticated subject without enqueueing a physical file.

```bash
curl -X POST http://localhost:5000/api/documents \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "fileName": "sample-policy.txt",
    "contentType": "text/plain"
  }'
```

The owner is never accepted from the JSON body. It is derived from the JWT `sub` claim.

## Upload and enqueue

```bash
curl -i -X POST http://localhost:5000/api/documents/upload \
  -H "Authorization: Bearer $TOKEN" \
  -F "file=@samples/sample-policy.txt;type=text/plain"
```

A valid request returns `202 Accepted` after the file is stored and document metadata plus the initial `Pending` job are committed atomically.

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

## Poll processing status

```bash
curl http://localhost:5000/api/documents/generated-document-id/processing-status \
  -H "Authorization: Bearer $TOKEN"
```

An ordinary user receives `404` for another subject's document identifier. An `Admin` token can inspect status across owners.

## List visible documents

```bash
curl http://localhost:5000/api/documents \
  -H "Authorization: Bearer $TOKEN"
```

A `User` sees only documents whose `ownerId` equals the token subject. An `Admin` sees all documents.

```json
[
  {
    "id": "generated-document-id",
    "fileName": "sample-policy.txt",
    "contentType": "text/plain",
    "sizeInBytes": 1200,
    "storagePath": "/app/storage/documents/stored-name.txt",
    "status": "indexed",
    "createdAt": "2026-07-28T00:00:00+00:00",
    "ownerId": "demo-user"
  }
]
```

## Search authorized documents

```bash
curl -X POST http://localhost:5000/api/documents/search \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "query": "What is the approval process for vendor contracts?",
    "topK": 5
  }'
```

Owner filtering is applied before PostgreSQL vector ranking. No chunks belonging to another ordinary user are returned.

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

## Ask a grounded question

```bash
curl -X POST http://localhost:5000/api/documents/ask \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "question": "Who needs to approve vendor contracts?",
    "topK": 5
  }'
```

The answer and every returned source are built only from chunks visible to the authenticated subject.

## Authorization failure examples

Anonymous document request:

```bash
curl -i http://localhost:5000/api/documents
```

Expected status: `401 Unauthorized`.

A signed token without `sub` is authenticated but fails the document policy with `403 Forbidden`.

A valid ordinary-user token that references another owner's document receives `404 Not Found` from the status endpoint and no foreign matches from Search or Ask.

## End-to-end demo

```bash
python scripts/demo_flow.py
```

The script creates a local user token when `JWT_TOKEN` is not supplied, uploads a sample, polls background processing, searches, and asks a grounded question.
