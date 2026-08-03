# Tenant Isolation

The API treats `tenant_id` as a required security boundary. Tenant identity is derived from the validated JWT and is never accepted from document payloads, upload forms, Search/Ask requests, or query-string scope parameters.

JWT identity is necessary but not sufficient. Durable tenant and membership records authorize non-platform access.

## Access model

A valid token contains:

- `sub`: stable user identifier;
- `tenant_id`: stable organization identifier;
- `role`: `User`, `Admin`, or `PlatformAdmin`;
- valid issuer, audience, signature, timestamps, and lifetime.

| Role | Durable requirement | Owner scope | Tenant scope | Database path |
|---|---|---|---|---|
| `User` | Active tenant + active User/Admin membership | Own documents | Token tenant | `document_app` + RLS |
| `Admin` | Active tenant + active durable Admin membership | All owners | Token tenant | `document_app` + RLS |
| `PlatformAdmin` | Platform policy | All owners | All tenants | `document_platform` |

A stale JWT Admin claim cannot elevate a durable User membership. The request is rejected until the caller receives a correctly scoped token.

## Tenant-aware data model

Tenant identity is stored on:

- `tenants.tenant_id`;
- `tenant_memberships.tenant_id`;
- `tenant_invitations.tenant_id`;
- `documents.tenant_id`;
- `document_chunks.tenant_id`;
- `document_ingestion_jobs.tenant_id`;
- `audit_events.tenant_id`.

Values are required and validated. Composite document/tenant foreign keys prevent chunks or jobs from referencing a document in another tenant.

Existing data is mapped to explicit legacy tenant/membership records by reviewed migrations. Production mappings must be verified before traffic is enabled.

## PostgreSQL roles

Three non-superuser, non-`BYPASSRLS` roles separate responsibilities:

- `document_app`: tenant-scoped public API reads and writes;
- `document_platform`: tenant lifecycle mutations, cross-tenant reads, and audit insertion;
- `document_privileged`: background ingestion writes, retries, recovery, and status/vector mutation.

Docker Compose supplies:

```text
ConnectionStrings__Postgres
ConnectionStrings__PostgresPlatform
ConnectionStrings__PostgresPrivileged
```

Development passwords:

```text
APP_DB_PASSWORD
PLATFORM_DB_PASSWORD
PRIVILEGED_DB_PASSWORD
```

The API container receives the first two connections only. The Worker receives the privileged connection and has no published host port.

## Row-Level Security

Forced RLS applies to:

- `tenants`;
- `tenant_memberships`;
- `tenant_invitations`;
- `documents`;
- `document_chunks`;
- `document_ingestion_jobs`;
- `audit_events`.

Tenant-runtime policies use:

```sql
tenant_id = NULLIF(current_setting('app.tenant_id', true), '')
```

Before a tenant-scoped operation, the application executes inside the transaction:

```sql
SELECT set_config('app.tenant_id', @tenantId, true);
```

The value is transaction-local. Missing context exposes no tenant rows; writes to another tenant fail `WITH CHECK`.

`document_platform` has explicit lifecycle and cross-tenant read policies but no document/job/chunk mutation grants. `document_privileged` has explicit worker policies.

## Request flow

1. JWT validation confirms signature, issuer, audience, lifetime, `sub`, `tenant_id`, and role.
2. PlatformAdmin either enters an explicit platform route/read path or is rejected by endpoint policy.
3. Non-platform authorization loads tenant and membership under the token tenant's RLS context.
4. The tenant must be active and membership active.
5. JWT Admin claims must match durable Admin membership.
6. `DocumentAccessContext` applies owner filtering for User scope.
7. PostgreSQL RLS independently limits tenant rows.
8. Background ingestion uses the independent privileged Worker and preserves stored tenant/owner identity.

## Failure behavior

- missing/invalid token: `401`;
- missing required claims/role: `403`;
- absent/removed membership, disabled tenant, or stale elevated role: `403`;
- foreign document identifier: `404`;
- runtime query without tenant context: no rows;
- cross-tenant runtime write: PostgreSQL rejection;
- final Admin removal/downgrade: `409 last_tenant_admin`;
- invitation replay/expiry/revocation: controlled lifecycle error.

## Verification

Automated tests cover:

- user isolation across owners and tenants;
- durable membership and Admin enforcement;
- immediate denial after removal/deactivation;
- final-Admin protection;
- subject-bound, one-time invitation acceptance and digest-only storage;
- PlatformAdmin cross-tenant reads through `document_platform`;
- direct RLS reads under different tenant contexts;
- direct cross-tenant lifecycle/document insert rejection;
- fail-closed reads without tenant context;
- forced RLS, policies, grants, roles, constraints, and indexes;
- absence of the privileged connection from the API container;
- independent Worker upload/index/restart persistence.

## Existing database migration

Entrypoint scripts run only for a fresh volume. Existing databases require:

1. verified database and stored-file backups;
2. review of ownership, tenant-isolation, audit, and lifecycle migrations;
3. strong distinct runtime/platform/worker passwords;
4. administrator application with `ON_ERROR_STOP`;
5. review of legacy tenant/member mappings and active Admin coverage;
6. verification of roles, grants, RLS policies, constraints, invitation indexes, and direct negative tests;
7. separate API/Worker deployment identities before serving traffic.

Do not leave production data under automatic legacy mappings without explicit ownership review.

## Remaining boundaries

Not implemented:

- trusted invitation delivery or domain verification;
- external IdP/SCIM synchronization;
- key/session/token revocation lifecycle;
- quotas, retention, export, deletion, billing, or tenant-specific encryption keys;
- centralized secret management and managed service identity;
- production network-policy and compliance approval.

See [Managed Tenant Lifecycle](TENANT_LIFECYCLE.md), [Authentication and Authorization](AUTHENTICATION_AND_AUTHORIZATION.md), and [Security Policy](../SECURITY.md).
