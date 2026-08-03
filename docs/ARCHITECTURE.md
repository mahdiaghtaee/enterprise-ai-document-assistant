# Architecture Overview

Enterprise AI Document Assistant is a local-first reference system for managed tenant document ingestion, durable background processing, persistent semantic retrieval, and grounded answers.

ASP.NET Core is packaged once but runs in separate API and Worker modes in Docker Compose. FastAPI remains a small boundary for future Python-specific integrations.

## High-Level Flow

```text
Authenticated client
    |
    | JWT: sub + tenant_id + role
    v
Public ASP.NET Core API (ApplicationMode=Api)
    |
    |-- validate active tenant and durable membership
    |-- User: tenant + owner scope
    |-- Admin: durable Admin + tenant scope
    |-- PlatformAdmin: explicit platform scope
    |
    +--> document_app connection: tenant-scoped writes and reads
    +--> document_platform connection: lifecycle mutations and cross-tenant reads
    +--> shared document-storage volume
    +--> atomic document + Pending job enqueue
    +--> audit and telemetry

Independent ASP.NET Core Worker (ApplicationMode=Worker)
    |
    |-- document_privileged connection only
    |-- shared document-storage volume
    |-- claim with FOR UPDATE SKIP LOCKED
    |-- extract, chunk, embed, and persist
    |-- retry, recover, complete, or fail jobs
    v
PostgreSQL + pgvector
    |
    +--> forced-RLS tenants, memberships, invitations
    +--> forced-RLS documents, chunks, jobs, audit events
    +--> tenant-tagged vector retrieval
```

## Security and Trust Boundaries

### JWT boundary

Every protected request requires:

- validated issuer, audience, signature, lifetime, and token timestamps;
- stable `sub` user identity;
- stable `tenant_id` organization identity;
- a supported application role.

The JWT authenticates the requested identity and tenant. It does not create a tenant or membership and is not the final authorization source.

### Durable lifecycle authorization boundary

For non-PlatformAdmin access, the application checks:

1. the tenant exists;
2. the tenant is `Active`;
3. the JWT subject has an `Active` membership;
4. tenant-admin operations have a durable `Admin` membership.

A JWT that still claims `Admin` after a durable downgrade to `User` is rejected. This prevents the stale token from inheriting tenant-wide document scope. A refreshed correctly scoped token is required.

Removing a membership or disabling a tenant blocks the next protected request without waiting for JWT expiration.

### Ownership boundary

- `User` receives `tenant_id` plus `owner_id = sub` filtering;
- durable `Admin` bypasses the owner filter but remains inside one active tenant;
- `PlatformAdmin` can cross tenants through the explicit platform path.

The API never accepts owner, tenant, or membership scope from document request payloads.

### Database boundary

The migration creates three non-superuser, non-`BYPASSRLS` PostgreSQL roles:

- `document_app`: tenant-runtime operations restricted by forced RLS;
- `document_platform`: tenant lifecycle mutations, cross-tenant reads, and audit insertion without ingestion/document mutation grants;
- `document_privileged`: background ingestion, retries, recovery, and vector/document status mutations.

Runtime tenant operations execute inside a transaction after:

```sql
SELECT set_config('app.tenant_id', @tenantId, true);
```

RLS policies compare row `tenant_id` with the transaction-local value. Missing context returns no rows, and cross-tenant writes fail policy checks.

The public API receives `document_app` and `document_platform`. It never receives `document_privileged`. Only the Worker receives the full ingestion credential.

### Process and storage boundary

Docker Compose runs:

- `document-api`: public port, no hosted ingestion worker, no privileged worker credential;
- `document-worker`: no published host port, hosted worker enabled, privileged credential;
- a named shared volume mounted at `/app/storage/documents` in both services.

The API stores an uploaded file and atomically enqueues metadata/job state. The Worker reads the same file through the named volume. This separates process credentials while preserving the local-storage implementation.

`ApplicationMode=Combined` remains available for isolated tests and compatibility. It is not the recommended deployment trust boundary.

## Managed Tenant Data Model

### Tenants

`tenants` stores display name, `Active`/`Disabled` state, and lifecycle actors/timestamps.

Provisioning creates a tenant and initial Admin in one transaction. Deactivation and reactivation are PlatformAdmin operations.

### Memberships

`tenant_memberships` stores one role/status record per tenant and subject:

- role: `User` or `Admin`;
- status: `Active` or `Removed`.

The final active tenant Admin cannot be removed or downgraded. PostgreSQL mutations lock membership/admin rows before checking the invariant.

### Invitations

`tenant_invitations` stores target subject, intended role, status, expiry, and a SHA-256 token digest.

The plaintext token:

- is generated from 32 random bytes;
- is returned only in the create response;
- is never stored or returned by list APIs;
- is bound to the JWT subject and tenant during acceptance;
- cannot be replayed after acceptance, revocation, or expiry.

## Document Data Model

### Documents

Stores file metadata, processing status, `owner_id`, and `tenant_id`.

### Ingestion jobs

Stores durable lifecycle state, attempts, retry availability, timestamps, controlled error information, and `tenant_id`.

A composite tenant/document foreign key prevents a job from referencing a document in another tenant.

### Semantic chunks

Stores document chunk text, pgvector embeddings, chunk position, and `tenant_id`.

A composite tenant/document foreign key prevents an indexed chunk from changing tenant independently of its document.

### Audit events

Stores append-only tenant-aware events for document, ingestion, Search, Ask, audit access, provisioning, tenant status, memberships, and invitations.

Audit metadata excludes bearer tokens, invitation tokens/digests, questions, source text, generated answers, provider bodies, and file content.

## Provisioning and Invitation Flow

```text
PlatformAdmin JWT
    |
POST /api/platform/tenants
    |
Platform database transaction
    |
Insert tenant + initial Admin atomically
    |
Tenant Admin creates invitation
    |
Generate 32 random bytes -> return plaintext once
    |
Persist SHA-256 digest + target subject + role + expiry
    |
Invited subject authenticates and accepts token
    |
Lock invitation -> validate subject/status/expiry -> activate membership -> accept token
```

Email delivery and identity proofing are outside the repository. A production system must deliver invitation secrets through a trusted channel.

## Upload and Enqueue Flow

```text
Validate JWT and durable membership
    |
Derive owner and tenant from claims
    |
Validate and store file on shared volume
    |
Open tenant-scoped PostgreSQL transaction
    |
Insert document + Pending job atomically
    |
Commit and return 202 Accepted
```

If database persistence fails after file storage, the newly stored file is removed.

## Worker Flow

```text
Independent privileged worker polls Pending jobs
    |
Claim one row with FOR UPDATE SKIP LOCKED
    |
Load document metadata and stored tenant/owner
    |
Read shared stored file
    |
Extract -> Chunk -> Embed -> Upsert tenant-tagged vectors
    |
Complete, retry, recover, or fail
```

The Worker uses the privileged connection because it processes jobs across tenants. It is not exposed as a public API service.

## Search and Ask Flow

```text
Validate JWT
    |
Check active tenant + durable membership/role
    |
Build owner/tenant access context
    |
Generate deterministic query embedding
    |
Open RLS-scoped or platform-read PostgreSQL transaction
    |
Filter authorized rows before pgvector ranking
    |
Run evidence/citation grounding gate
    |
Return deterministic or optional provider answer + independent sources
```

Retrieved source metadata is created before provider generation and is never parsed from provider output.

## Retry and Recovery

- pending jobs are claimed transactionally;
- attempt counts are bounded;
- retryable failures return to `Pending` after a delay;
- permanent or exhausted failures become `Failed`;
- abandoned `Processing` jobs are recovered after a timeout;
- graceful shutdown requeues interrupted work without consuming an attempt.

## Verification Strategy

The repository verifies:

- JWT claim and role validation;
- active tenant and durable membership enforcement;
- immediate denial after removal, downgrade, or deactivation;
- final-Admin protection;
- invitation one-time use, subject binding, expiry/revocation, and digest-only storage;
- owner isolation inside one tenant;
- Admin access across owners only inside one tenant;
- PlatformAdmin cross-tenant reads through the narrow platform role;
- direct RLS reads under multiple tenant contexts;
- rejection of cross-tenant lifecycle/document writes;
- fail-closed reads without tenant context;
- roles, grants, constraints, indexes, policies, and forced RLS;
- absence of the privileged connection from the API container;
- independent Worker processing through the shared volume;
- atomic enqueue, ingestion lifecycle, restart persistence, and retrieval;
- audit secret exclusion and append-only behavior;
- retrieval and answer regression baselines.

## Component Responsibilities

### Public ASP.NET Core API

- JWT authentication;
- durable tenant/membership authorization;
- tenant provisioning and management APIs;
- invitation lifecycle;
- owner/tenant access context;
- local shared file storage;
- atomic document/job enqueue;
- processing-status reads;
- semantic search and grounded answers;
- audit and telemetry.

### ASP.NET Core Worker

- privileged job claiming;
- text extraction and chunking;
- deterministic embeddings;
- semantic-index writes;
- retries, recovery, completion, and controlled failure.

### PostgreSQL and pgvector

- durable lifecycle, metadata, jobs, chunks, embeddings, and audit records;
- forced tenant Row-Level Security;
- database role and policy enforcement;
- job claiming and lifecycle updates;
- vector similarity ranking.

### FastAPI

- health and indexing-boundary endpoints;
- future Python-specific parsing or provider integrations only when justified and tested.

### Redis

Redis is available for future caching, rate limiting, or coordination but is not part of the current durable workflow.

## Production Gaps

Before sensitive-data use, the system still requires:

- external IdP/SCIM synchronization, domain verification, managed key rotation, and session/token revocation;
- trusted invitation delivery and recipient proofing;
- tenant quotas, retention, export, deletion, legal hold, and recovery workflows;
- encrypted storage, centralized secrets, TLS, and restricted networking;
- production telemetry storage, dashboards, alerts, SLOs, and audit retention;
- malware scanning and file-signature validation;
- backup, restore, load, failover, and capacity validation;
- representative retrieval/answer evaluation and approved provider governance.

See [Managed Tenant Lifecycle](TENANT_LIFECYCLE.md), [Tenant Isolation](TENANT_ISOLATION.md), [Authentication and Authorization](AUTHENTICATION_AND_AUTHORIZATION.md), [Security Policy](../SECURITY.md), [Background Ingestion](BACKGROUND_INGESTION.md), and [Roadmap](ROADMAP.md).
