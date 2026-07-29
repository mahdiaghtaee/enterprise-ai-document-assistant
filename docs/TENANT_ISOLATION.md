# Tenant Isolation

The document API treats `tenant_id` as a required security boundary. Tenant identity is derived from the validated JWT and is never accepted from document request bodies, upload forms, search requests, or query-string parameters.

## Access model

A valid document token contains:

- `sub`: stable user identifier;
- `tenant_id`: stable organization or workspace identifier;
- `role`: `User`, `Admin`, or `PlatformAdmin`;
- the issuer, audience, signature, lifetime, and token timestamps required by JWT validation.

Role behavior:

| Role | Owner scope | Tenant scope | Database path |
|---|---|---|---|
| `User` | Own documents | Own tenant | RLS-restricted runtime role |
| `Admin` | All owners | Own tenant | RLS-restricted runtime role |
| `PlatformAdmin` | All owners | All tenants | Explicit privileged role |

`Admin` is intentionally tenant-scoped. Cross-tenant access requires the separate `PlatformAdmin` role and the privileged database connection path.

## Data model

Tenant identity is stored on:

- `documents.tenant_id`;
- `document_chunks.tenant_id`;
- `document_ingestion_jobs.tenant_id`.

Every value is required and checked for nonblank content. Composite foreign keys ensure that a semantic chunk or ingestion job cannot reference a document under a different tenant.

Existing rows are backfilled to `legacy-tenant`. Existing pre-authentication owners remain `legacy-system`.

## PostgreSQL roles

The migration creates two non-superuser roles:

- `document_app`: runtime API role, subject to tenant Row-Level Security;
- `document_privileged`: background worker and platform-administration role, allowed by explicit privileged policies.

Neither role has `SUPERUSER` or `BYPASSRLS`.

Docker Compose supplies separate connection strings:

```text
ConnectionStrings__Postgres
ConnectionStrings__PostgresPrivileged
```

The development passwords are configured by:

```text
APP_DB_PASSWORD
PRIVILEGED_DB_PASSWORD
```

These defaults are local-only. A deployment must supply managed secrets and should separate the privileged worker or platform-administration path from the public API process.

## Row-Level Security

`infra/postgres/init/zzzzz-tenant-isolation.sql` enables and forces RLS on:

- `documents`;
- `document_chunks`;
- `document_ingestion_jobs`.

The runtime role receives policies equivalent to:

```sql
tenant_id = NULLIF(current_setting('app.tenant_id', true), '')
```

Before a tenant-scoped query or transaction, the application executes:

```sql
SELECT set_config('app.tenant_id', @tenantId, true);
```

The third argument makes the value transaction-local. If the session context is missing, `current_setting(..., true)` produces no matching tenant and runtime reads return no rows. Inserts or updates for a different tenant fail the RLS `WITH CHECK` policy.

## Request flow

1. JWT validation confirms the token signature, issuer, audience, lifetime, `sub`, `tenant_id`, and role.
2. The application constructs a `DocumentAccessContext` from claims.
3. User-level owner filtering is applied when the role is `User`.
4. The runtime PostgreSQL transaction receives the tenant context.
5. PostgreSQL RLS independently restricts documents, chunks, and jobs to the active tenant.
6. Background ingestion uses the privileged connection and preserves the stored tenant through chunk and vector persistence.

## Failure behavior

- Missing or invalid token: `401`.
- Authenticated token missing `sub`, `tenant_id`, or a supported role: `403`.
- Document identifier outside the caller's owner or tenant scope: `404`.
- Runtime database operation without tenant session context: no visible rows.
- Cross-tenant insert or update through the runtime role: rejected by PostgreSQL.

## Verification

Automated tests cover:

- user isolation across owners and tenants;
- tenant administrator access across owners only inside one tenant;
- platform administrator access across tenants;
- missing tenant claim rejection;
- direct PostgreSQL reads using two different tenant contexts;
- direct cross-tenant insert rejection under the runtime role;
- fail-closed runtime reads without tenant context;
- authenticated Compose upload, processing, retrieval, and restart persistence;
- RLS, `FORCE ROW LEVEL SECURITY`, policies, role flags, constraints, and indexes.

## Existing database migration

PostgreSQL entrypoint scripts run only for a fresh volume. For an existing database:

1. back up the database;
2. review `zzzz-document-ownership.sql` and `zzzzz-tenant-isolation.sql`;
3. supply strong runtime-role passwords as environment variables to `psql`;
4. apply the scripts with `ON_ERROR_STOP` enabled;
5. verify all existing rows have the intended tenant assignment;
6. verify both runtime roles can connect;
7. test runtime access with and without `app.tenant_id` before deploying the API.

Do not automatically assign production data to `legacy-tenant` without an explicit migration and ownership review.

## Remaining boundaries

This implementation does not provide:

- tenant provisioning, invitations, membership lifecycle, or domain verification;
- per-tenant quotas, retention, billing, or encryption keys;
- centralized audit events;
- external identity-provider tenant synchronization;
- managed database or JWT secret rotation;
- encrypted document storage;
- independently deployed privileged worker infrastructure.

The project remains a reference implementation and is not approved for confidential or regulated data until those controls and an operational security review are complete.
