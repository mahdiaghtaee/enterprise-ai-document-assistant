# Managed Tenant Lifecycle and Worker Trust Boundary

The platform now treats JWT tenant and role claims as authenticated assertions, not the final authorization decision. Durable tenant and membership records are checked on every protected document, tenant-admin, and audit request.

## Security model

A non-PlatformAdmin request is authorized only when all of the following are true:

1. the JWT is valid and contains `sub`, `tenant_id`, and a supported role;
2. the tenant exists in `tenants`;
3. the tenant status is `Active`;
4. an `Active` membership exists for the JWT subject in that tenant;
5. an Admin operation has an active durable `Admin` membership.

A JWT that claims `Admin` while the durable membership is `User` is rejected. The client must obtain a fresh token with the corrected role. This fail-closed rule prevents a stale elevated claim from granting tenant-wide document access.

`PlatformAdmin` is a platform role and is intentionally not represented as a tenant membership. It uses separately authorized platform APIs and a narrow database role.

## Data model

### `tenants`

Stores:

- tenant identifier and display name;
- `Active` or `Disabled` status;
- creation actor and timestamps;
- deactivation actor and timestamp.

Disabling a tenant blocks its members on their next request without waiting for token expiration. Reactivation restores access only for memberships that remain active.

### `tenant_memberships`

Stores one durable membership per tenant and subject:

- role: `User` or `Admin`;
- status: `Active` or `Removed`;
- creation, update, and removal metadata.

The final active tenant Admin cannot be removed or downgraded. PostgreSQL implementations lock the relevant membership/admin rows inside the mutation transaction before enforcing this invariant.

### `tenant_invitations`

Stores:

- target tenant and authenticated subject identifier;
- intended `User` or `Admin` role;
- `Pending`, `Accepted`, `Revoked`, or `Expired` status;
- expiry and lifecycle metadata;
- a SHA-256 digest of the invitation token.

The plaintext token is generated from 32 cryptographically random bytes, returned once in the create response, and never stored in PostgreSQL or audit metadata.

Invitation acceptance is:

- bound to the JWT `sub` and `tenant_id`;
- one-time;
- expiration-aware;
- revocation-aware;
- transactional with membership activation.

Email delivery and identity proofing are not implemented. A production deployment must deliver the one-time token through a trusted channel after verifying the recipient identity.

## API surface

### PlatformAdmin

```http
POST /api/platform/tenants
POST /api/platform/tenants/{tenantId}/status
```

Provisioning creates the tenant and its initial Admin atomically.

Example request:

```json
{
  "tenantId": "acme",
  "displayName": "Acme Corporation",
  "initialAdminUserId": "user-123"
}
```

Status request:

```json
{
  "status": "Disabled"
}
```

Supported statuses are `Active` and `Disabled`.

### Tenant Admin

```http
GET    /api/tenant/members
PUT    /api/tenant/members/{userId}/role
DELETE /api/tenant/members/{userId}
POST   /api/tenant/invitations
GET    /api/tenant/invitations
POST   /api/tenant/invitations/{invitationId}/revoke
```

Invitation creation example:

```json
{
  "inviteeUserId": "user-456",
  "role": "User",
  "lifetimeHours": 24
}
```

The response contains the plaintext `token` once. Listing invitations never returns token material.

### Invitation acceptance

```http
POST /api/tenant/invitations/accept
```

```json
{
  "token": "one-time-token"
}
```

The request requires an authenticated `User` or `Admin` JWT for the invited subject and tenant. It does not require an existing membership.

## Stable lifecycle errors

| Code | Meaning |
|---|---|
| `tenant_already_exists` | Provisioning attempted for an existing tenant |
| `tenant_not_found` | Managed tenant is absent |
| `tenant_disabled` | Mutation requires an active tenant |
| `membership_not_found` | Active target membership is absent |
| `last_tenant_admin` | Mutation would remove/downgrade the final active Admin |
| `pending_invitation_exists` | A non-expired pending invitation already exists |
| `invitation_not_found` | Invitation identifier or token is invalid |
| `invitation_not_pending` | Invitation was accepted, revoked, or expired |
| `invitation_expired` | Invitation lifetime elapsed |
| `invitation_subject_mismatch` | JWT subject does not match invitee |
| `invalid_membership_role` | Role is not `User` or `Admin` |
| `invalid_tenant_status` | Status is not `Active` or `Disabled` |
| `invalid_invitation_lifetime` | Requested lifetime exceeds configured bounds |

Missing or inactive authorization state is returned as `403 Forbidden` by authorization policies rather than exposing whether a tenant or membership exists.

## PostgreSQL isolation

`tenants`, `tenant_memberships`, and `tenant_invitations` have forced Row-Level Security.

Database roles:

| Role | Purpose |
|---|---|
| `document_app` | Tenant-scoped public API operations using transaction-local `app.tenant_id` |
| `document_platform` | Platform lifecycle mutations, cross-tenant reads, and audit insertion; no document/job/chunk mutation |
| `document_privileged` | Background ingestion mutations and recovery |

All three roles are non-superuser and do not have `BYPASSRLS`.

The public API receives `document_app` and `document_platform`. It does not receive `document_privileged`. The independent worker receives `document_privileged` and is not exposed on a host port.

## Process modes

The same ASP.NET Core image supports:

- `ApplicationMode=Api`: HTTP API, no hosted ingestion worker;
- `ApplicationMode=Worker`: hosted ingestion worker, API paths blocked, loopback-only listener in Compose;
- `ApplicationMode=Combined`: compatibility mode for isolated tests and non-Compose development.

Docker Compose runs separate `document-api` and `document-worker` services. A named volume shares uploaded files so the API can enqueue metadata and the worker can read the stored document.

## Audit and telemetry

Lifecycle audit event types include:

- `tenant.provisioned`;
- `tenant.deactivated`;
- `tenant.reactivated`;
- `tenant.membership_role_changed`;
- `tenant.membership_removed`;
- `tenant.invitation_created`;
- `tenant.invitation.accepted`;
- `tenant.invitation.revoked`.

Audit metadata may include bounded identifiers, intended role, expiry, and outcome. It excludes:

- invitation plaintext tokens;
- invitation token digests;
- bearer tokens;
- document/source text;
- provider credentials.

## Local workflow

Start the split stack:

```bash
docker compose up --build
```

Run the demo. It provisions the local tenant, issues and accepts a one-time invitation, uploads a document, and waits for the independent worker:

```bash
python scripts/demo_flow.py
```

Set `JWT_TOKEN` only when using an already provisioned external identity. The repository token helper remains a development utility, not a production identity provider.

## Migration guidance

Fresh Compose volumes execute `infra/postgres/init/zzzzzzz-tenant-lifecycle.sql` automatically.

Existing installations must:

1. back up tenant data, audit data, document metadata, chunks, jobs, and stored files;
2. review the generated legacy tenant/member mappings;
3. apply the idempotent lifecycle migration using an administrator connection with the required environment passwords;
4. rotate and distribute distinct `document_app`, `document_platform`, and `document_privileged` secrets;
5. deploy the API and worker separately;
6. verify tenant status, membership roles, final-Admin coverage, RLS, invitations, and background processing before serving traffic.

Docker entrypoint scripts do not rerun for an existing PostgreSQL volume.

## Explicit limitations

This milestone does not implement:

- email or SMS invitation delivery;
- domain ownership verification;
- external identity-provider synchronization;
- SCIM provisioning;
- managed JWT key rotation or token revocation;
- per-tenant quotas;
- retention, export, deletion, or legal-hold workflows;
- centralized secret management;
- approval workflows for Admin invitations;
- organization billing.

Durable membership checks provide immediate application-level revocation. They do not replace identity-provider session revocation, signing-key lifecycle, device controls, or production incident response.
