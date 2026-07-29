# API Examples

The API is published by Docker Compose at `http://localhost:5000`. The health endpoint is public; every document endpoint requires a JWT containing `sub`, `tenant_id`, and a supported role.

## Create a local token

```bash
TOKEN=$(python scripts/create_dev_token.py --user demo-user --tenant demo-tenant --role User)
```

Windows PowerShell:

```powershell
$TOKEN = python scripts/create_dev_token.py --user demo-user --tenant demo-tenant --role User
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
  "tenantId": "demo-tenant",
  "roles": ["User"],
  "canAccessAllTenants": false,
  "canAccessAllDocumentsInTenant": false
}
```

A tenant `Admin` returns `canAccessAllDocumentsInTenant: true` and `canAccessAllTenants: false`. Only `PlatformAdmin` returns cross-tenant access.

## Create document metadata

This registers metadata under the authenticated owner and tenant without enqueueing a physical file.

```bash
curl -X POST http://localhost:5000/api/documents \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "fileName": "sample-policy.txt",
    "contentType": "text/plain"
  }'
```

Neither owner nor tenant is accepted from the JSON body. Both are derived from JWT claims.

## Upload and enqueue

```bash
curl -i -X POST http://localhost:5000/api/documents/upload \
  -H "Authorization: Bearer $TOKEN" \
  -F "file=@samples/sample-policy.txt;type=text/plain"
```

A valid request returns `202 Accepted` after the file is stored and document metadata plus the initial `Pending` job are committed atomically with owner and tenant identity.

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

A `User` receives `404` for a document outside its owner or tenant scope. A tenant `Admin` can inspect all owners in the same tenant. A `PlatformAdmin` can inspect across tenants.

## List visible documents

```bash
curl http://localhost:5000/api/documents \
  -H "Authorization: Bearer $TOKEN"
```

Visibility:

- `User`: own documents inside `demo-tenant`;
- `Admin`: all owners inside `demo-tenant`;
- `PlatformAdmin`: all tenants through the privileged path.

```json
[
  {
    "id": "generated-document-id",
    "fileName": "sample-policy.txt",
    "contentType": "text/plain",
    "sizeInBytes": 1200,
    "storagePath": "/app/storage/documents/stored-name.txt",
    "status": "indexed",
    "createdAt": "2026-07-29T00:00:00+00:00",
    "ownerId": "demo-user",
    "tenantId": "demo-tenant"
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

Tenant scope is enforced by PostgreSQL Row-Level Security before vector ranking. The application additionally applies the owner filter for `User` tokens.

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

The answer and every source are built only from chunks visible to the authenticated owner and tenant scope.

## Authorization failure examples

Anonymous document request:

```bash
curl -i http://localhost:5000/api/documents
```

Expected status: `401 Unauthorized`.

A signed token missing `sub`, `tenant_id`, or a supported role fails with `403 Forbidden`.

A valid token outside the document's owner or tenant scope receives `404 Not Found` from status lookup and no foreign matches from Search or Ask.

## Cross-tenant local verification

```bash
TENANT_A_USER=$(python scripts/create_dev_token.py --user user-a --tenant tenant-a --role User)
TENANT_A_ADMIN=$(python scripts/create_dev_token.py --user admin-a --tenant tenant-a --role Admin)
TENANT_B_ADMIN=$(python scripts/create_dev_token.py --user admin-b --tenant tenant-b --role Admin)
PLATFORM_ADMIN=$(python scripts/create_dev_token.py --user platform-admin --tenant platform --role PlatformAdmin)
```

After uploading with `TENANT_A_USER`:

- search with `TENANT_A_USER`: document returned;
- search with another ordinary tenant-a user: document hidden by owner scope;
- search with `TENANT_A_ADMIN`: document returned;
- search with `TENANT_B_ADMIN`: document hidden by tenant RLS;
- search with `PLATFORM_ADMIN`: document returned.

## End-to-end demo

```bash
python scripts/demo_flow.py
```

The script creates a tenant-scoped local user token when `JWT_TOKEN` is absent, uploads a sample, polls processing, searches, and asks a grounded question.
