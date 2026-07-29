# Architecture Overview

Enterprise AI Document Assistant is a local-first reference system for tenant-isolated document ingestion, durable background processing, persistent semantic retrieval, and source-aware answers.

The executable document pipeline runs in ASP.NET Core. FastAPI remains a small boundary for future Python-specific integrations.

## High-Level Flow

```text
Authenticated client
    |
    | JWT: sub + tenant_id + role
    v
ASP.NET Core API
    |
    |-- User: tenant + owner scope
    |-- Admin: tenant scope
    |-- PlatformAdmin: explicit privileged scope
    |
    +--> Local document storage
    |
    +--> PostgreSQL
    |       - document_app: forced RLS, transaction-local tenant context
    |       - document_privileged: worker/platform policy
    |       - documents, chunks, and jobs tagged with tenant_id
    |       - pgvector cosine retrieval
    |
    +--> Hosted ingestion worker
    |       - claim with FOR UPDATE SKIP LOCKED
    |       - extract, chunk, embed, and persist
    |       - preserve owner and tenant identity
    |       - retry, recover, complete, or fail jobs
    |
    +--> Redis infrastructure
    |
    +--> FastAPI integration boundary
```

## Security and Trust Boundaries

### JWT boundary

Every document request requires:

- validated issuer, audience, signature, lifetime, and token timestamps;
- stable `sub` user identity;
- stable `tenant_id` organization or workspace identity;
- one of `User`, `Admin`, or `PlatformAdmin`.

The API never accepts owner or tenant identity from document request payloads.

### Application authorization boundary

- `User` receives `tenant_id` plus `owner_id = sub` filtering;
- `Admin` bypasses the owner filter but remains inside one tenant;
- `PlatformAdmin` can cross tenants only through the explicit privileged database path.

Document listing, processing status, Search, Ask, and returned source text share this context.

### Database boundary

The migration creates two non-superuser PostgreSQL roles:

- `document_app`: runtime role restricted by forced Row-Level Security;
- `document_privileged`: background worker and platform-administration role allowed by explicit privileged policies.

Neither role has `SUPERUSER` or `BYPASSRLS`.

Runtime operations execute inside a transaction after:

```sql
SELECT set_config('app.tenant_id', @tenantId, true);
```

RLS policies compare row `tenant_id` with the transaction-local value. Missing context produces no matching rows, while cross-tenant writes fail the `WITH CHECK` policy.

## Data Model

### Documents

Stores file metadata, processing status, `owner_id`, and `tenant_id`.

### Ingestion jobs

Stores durable lifecycle state, attempts, retry availability, timestamps, controlled error information, and `tenant_id`.

A composite tenant/document foreign key prevents a job from referencing a document in another tenant.

### Semantic chunks

Stores document chunk text, pgvector embeddings, chunk position, and `tenant_id`.

A composite tenant/document foreign key prevents an indexed chunk from changing tenant independently of its document.

## Upload and Enqueue Flow

```text
Validate JWT and access policy
    |
Derive owner and tenant from claims
    |
Validate and store file
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
Privileged worker polls Pending jobs
    |
Claim one row with FOR UPDATE SKIP LOCKED
    |
Load document metadata and stored tenant/owner
    |
Extract -> Chunk -> Embed -> Upsert tenant-tagged vectors
    |
Complete, retry, recover, or fail
```

The worker uses the privileged connection because it must process jobs across tenants. The reference Compose deployment hosts this worker in the API process; production should separate that privileged trust boundary.

## Search and Ask Flow

```text
Validate JWT
    |
Build owner/tenant access context
    |
Generate deterministic query embedding
    |
Open RLS-scoped or privileged PostgreSQL transaction
    |
Filter authorized rows before pgvector ranking
    |
Return ranked chunks or source-aware answer
```

`User` requests apply owner and tenant scope. `Admin` applies tenant scope. `PlatformAdmin` uses the explicit privileged path.

## Retry and Recovery

- pending jobs are claimed transactionally;
- attempt counts are bounded;
- retryable failures return to `Pending` after a delay;
- permanent or exhausted failures become `Failed`;
- abandoned `Processing` jobs are recovered after a timeout;
- graceful shutdown requeues interrupted work without consuming an attempt.

## Verification Strategy

The repository verifies:

- JWT and role enforcement;
- owner isolation inside one tenant;
- administrator access across owners only inside one tenant;
- platform administrator access across tenants;
- direct RLS reads under multiple tenant contexts;
- rejection of cross-tenant database writes;
- fail-closed reads without tenant context;
- tenant constraints, composite foreign keys, roles, policies, and forced RLS;
- atomic enqueue and ingestion lifecycle behavior;
- retrieval persistence and authorization after API restart.

## Component Responsibilities

### ASP.NET Core

- public REST API and Swagger;
- authentication and authorization;
- tenant/owner access context;
- local file storage;
- atomic document and job persistence;
- hosted ingestion worker;
- text extraction, chunking, deterministic embeddings;
- semantic search and source-aware answers.

### PostgreSQL and pgvector

- durable metadata, jobs, chunks, and embeddings;
- forced tenant Row-Level Security;
- role and policy enforcement;
- job claiming and lifecycle updates;
- vector similarity ranking.

### FastAPI

- health and placeholder indexing-boundary endpoints;
- future Python-specific parsing or provider integrations only when justified and tested.

### Redis

Redis is available for future caching, rate limiting, or coordination but is not part of the current durable workflow.

## Production Gaps

Before sensitive-data use, the system still requires:

- tenant provisioning, memberships, invitation and deactivation workflows;
- external identity-provider synchronization, key rotation, and token revocation;
- durable audit events and tamper-resistant retention;
- correlation identifiers, metrics, and OpenTelemetry traces;
- encrypted storage, centralized secrets, TLS, and restricted networking;
- malware scanning and file-signature validation;
- separate privileged worker/platform deployment;
- backup, restore, deletion, load, failover, and capacity validation;
- retrieval evaluation and prompt-injection controls.

See [Tenant Isolation](TENANT_ISOLATION.md), [Authentication and Authorization](AUTHENTICATION_AND_AUTHORIZATION.md), [Security Policy](../SECURITY.md), [Background Ingestion](BACKGROUND_INGESTION.md), and [Roadmap](ROADMAP.md).
