# Authentication and Document Authorization

The ASP.NET Core document API requires a signed JWT bearer token for every document operation. The health endpoint remains public so orchestrators can determine whether the process is running.

## Security model

A valid token must contain:

- `sub`: stable user identifier used as the document owner;
- `tenant_id`: stable organization or workspace identifier;
- `role`: `User`, `Admin`, or `PlatformAdmin`;
- `iss`, `aud`, `nbf`, and `exp` values accepted by the configured JWT validation parameters.

Role behavior:

- `User` can access only documents whose `owner_id` matches `sub` and whose `tenant_id` matches the token;
- `Admin` can access all document owners inside the token tenant, but cannot cross tenants;
- `PlatformAdmin` can access documents across tenants through the explicit privileged database path.

Requests without a valid token return `401`. Authenticated tokens missing `sub`, `tenant_id`, or a supported role fail the document-access policy with `403`.

Foreign document identifiers are returned as `404` outside the caller's authorized owner or tenant scope. This avoids confirming whether another user's or organization's document exists.

## Protected endpoints

- `GET /api/documents`
- `POST /api/documents`
- `POST /api/documents/upload`
- `GET /api/documents/{documentId}/processing-status`
- `POST /api/documents/search`
- `POST /api/documents/ask`
- `GET /api/auth/me`

`GET /health` is intentionally anonymous.

## Local development tokens

Start the stack, then generate a one-hour user token:

```bash
python scripts/create_dev_token.py --user demo-user --tenant demo-tenant --role User
```

Generate a tenant administrator token:

```bash
python scripts/create_dev_token.py --user demo-admin --tenant demo-tenant --role Admin
```

Generate a platform administrator token only for explicit cross-tenant tests:

```bash
python scripts/create_dev_token.py --user platform-admin --tenant platform --role PlatformAdmin
```

Use the token in Swagger's **Authorize** dialog, paste it into the Web UI authentication panel, or send it as an HTTP header:

```text
Authorization: Bearer <token>
```

The helper is not an identity provider and the repository signing key is development-only. Never reuse it in a deployed environment.

## JWT configuration

The API reads:

- `Jwt:Issuer`
- `Jwt:Audience`
- `Jwt:SigningKey`

The Development environment includes explicit local values. A non-development deployment must supply its own values through environment variables or a secret manager. Missing or weak JWT configuration prevents application startup.

A production deployment should use a real identity provider and managed asymmetric keys or a controlled key lifecycle. It must issue stable, non-reassignable user and tenant identifiers.

## Ownership and tenant migrations

`infra/postgres/init/zzzz-document-ownership.sql` adds `documents.owner_id`, backfills existing rows to `legacy-system`, and makes ownership required.

`infra/postgres/init/zzzzz-tenant-isolation.sql` adds tenant identity to documents, chunks, and ingestion jobs; backfills existing rows to `legacy-tenant`; creates runtime database roles; and enables forced Row-Level Security.

PostgreSQL entrypoint scripts run only for a fresh database volume. Existing databases require a reviewed manual migration after backup. Production data should be mapped to real tenants before deployment rather than left under the legacy tenant.

## Authorization invariants

- user and tenant identity are assigned from validated JWT claims, never request JSON or form fields;
- document metadata and the initial ingestion job are committed atomically with owner and tenant identity;
- background processing preserves both values while creating semantic-index records;
- `User` applies both tenant and owner scope;
- `Admin` bypasses only the owner filter inside its tenant;
- only `PlatformAdmin` uses the privileged cross-tenant path;
- PostgreSQL Row-Level Security independently enforces tenant scope for runtime queries and writes;
- source text returned by Search and Ask is subject to the same scope as document listing;
- negative tests cover anonymous access, missing claims, cross-user access, cross-tenant access, and direct database writes.

See [Tenant Isolation](TENANT_ISOLATION.md) for the database roles, RLS policies, session context, migration, and verification details.

## Remaining production gaps

This boundary does not complete tenant provisioning, membership and invitation workflows, audit logging, encrypted file storage, centralized secret management, token revocation, key rotation, or external identity-provider synchronization. The project must not be used for confidential or regulated documents until those controls and operational reviews are complete.
