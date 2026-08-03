# Authentication and Document Authorization

The ASP.NET Core API requires a signed JWT bearer token for protected operations. Health endpoints remain public for orchestration.

JWT validation authenticates the subject and requested tenant. Durable tenant and membership state determines whether non-platform access is still authorized.

## JWT requirements

A valid token must contain:

- `sub`: stable user identifier used as the document owner;
- `tenant_id`: stable organization identifier;
- `role`: `User`, `Admin`, or `PlatformAdmin`;
- `iss`, `aud`, `nbf`, and `exp` values accepted by the configured JWT validation parameters.

Requests without a valid token return `401`. Authenticated tokens missing required claims or a supported role return `403`.

## Durable authorization requirements

For `User` and `Admin`, a protected request also requires:

- a matching record in `tenants`;
- tenant status `Active`;
- an `Active` membership for `sub` in `tenant_id`;
- durable membership role `Admin` for tenant-admin or tenant-wide owner access.

Role behavior:

- `User`: active membership plus `tenant_id` and `owner_id = sub` scope;
- `Admin`: active durable Admin membership, all owners inside one active tenant;
- `PlatformAdmin`: explicit platform APIs and narrow cross-tenant database path; no tenant membership required.

A JWT that claims `Admin` while durable membership is `User` is rejected. The client must obtain a refreshed correctly scoped token. This prevents stale elevated claims from gaining tenant-wide access.

Removing a membership or disabling a tenant blocks the next protected request without waiting for token expiration.

Foreign document identifiers return `404` outside authorized owner/tenant scope so the API does not confirm another user's or organization's document exists.

## Protected endpoints

Document access policy:

- `GET /api/documents`
- `POST /api/documents`
- `POST /api/documents/upload`
- `GET /api/documents/{documentId}/processing-status`
- `POST /api/documents/search`
- `POST /api/documents/ask`
- `GET /api/auth/me`

Durable tenant Admin policy:

- `GET /api/tenant/members`
- `PUT /api/tenant/members/{userId}/role`
- `DELETE /api/tenant/members/{userId}`
- `POST /api/tenant/invitations`
- `GET /api/tenant/invitations`
- `POST /api/tenant/invitations/{invitationId}/revoke`
- `GET /api/audit/events`

PlatformAdmin policy:

- `POST /api/platform/tenants`
- `POST /api/platform/tenants/{tenantId}/status`

Invitation acceptance:

- `POST /api/tenant/invitations/accept`

Acceptance requires authenticated `sub`, `tenant_id`, and User/Admin role, but intentionally does not require an existing membership. The invitation itself is bound to the authenticated subject and tenant.

## Local development tokens

Generate development tokens:

```bash
python scripts/create_dev_token.py --user demo-user --tenant demo-tenant --role User
python scripts/create_dev_token.py --user demo-admin --tenant demo-tenant --role Admin
python scripts/create_dev_token.py --user platform-admin --tenant platform --role PlatformAdmin
```

Use them in Swagger, the Web UI, or:

```text
Authorization: Bearer <token>
```

A signed token does not provision a tenant or membership. Run `python scripts/demo_flow.py` for an automatic local provisioning/invitation flow, or use the lifecycle APIs documented in [Managed Tenant Lifecycle](TENANT_LIFECYCLE.md).

The helper is not an identity provider and the repository signing key is development-only.

## JWT configuration

The API reads:

- `Jwt:Issuer`
- `Jwt:Audience`
- `Jwt:SigningKey`

Development includes explicit local values. A deployed environment must supply managed values. Missing or weak JWT configuration prevents startup.

Production should use stable, non-reassignable subject/tenant identifiers, managed signing keys, key rotation, and identity-provider session/token revocation. Durable membership removal protects this application immediately but does not revoke the upstream identity session.

## Database migrations

- `infra/postgres/init/zzzz-document-ownership.sql`: required document owner identity;
- `infra/postgres/init/zzzzz-tenant-isolation.sql`: tenant identity, composite keys, runtime/privileged RLS;
- `infra/postgres/init/zzzzzz-audit-observability.sql`: append-only audit boundary;
- `infra/postgres/init/zzzzzzz-tenant-lifecycle.sql`: tenants, memberships, invitations, platform role, lifecycle RLS, and legacy mappings.

Entrypoint scripts run only for fresh PostgreSQL volumes. Existing databases require reviewed manual migrations after backup and validation.

## Authorization invariants

- user and tenant identity come from validated JWT claims, never document payloads;
- durable lifecycle state is authoritative for non-platform access;
- final active tenant Admin cannot be removed or downgraded;
- invitation acceptance is subject/tenant-bound, one-time, expiry-aware, and revocation-aware;
- only SHA-256 invitation-token digests are stored;
- document metadata and initial job commit atomically with owner and tenant;
- background processing preserves owner/tenant identity;
- `User` applies tenant and owner scope;
- durable `Admin` bypasses only owner scope inside one tenant;
- `PlatformAdmin` uses the narrow `document_platform` path;
- forced PostgreSQL RLS independently enforces tenant scope;
- Search/Ask source text uses the same authorization scope;
- negative tests cover anonymous access, missing claims, absent/removed membership, disabled tenant, stale Admin role, cross-user/tenant access, final-Admin protection, invitation replay, and direct database writes.

## Remaining production gaps

This boundary does not provide external identity-provider/SCIM synchronization, trusted invitation delivery, domain verification, managed token revocation/key rotation, encrypted file storage, centralized secret management, quotas, retention/export/deletion, or production compliance review.

See [Managed Tenant Lifecycle](TENANT_LIFECYCLE.md), [Tenant Isolation](TENANT_ISOLATION.md), and [Security Policy](../SECURITY.md).
