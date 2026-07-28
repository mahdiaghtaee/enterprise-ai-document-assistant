# Authentication and Document Authorization

The ASP.NET Core document API requires a signed JWT bearer token for every document operation. The health endpoint remains public so orchestrators can determine whether the process is running.

## Security model

A valid token must contain:

- `sub`: the stable user identifier used as the document owner;
- `role`: either `User` or `Admin`;
- `iss`, `aud`, `nbf`, and `exp` values accepted by the configured JWT validation parameters.

`User` tokens can create, list, monitor, search, and ask against documents whose `owner_id` matches the token subject. `Admin` tokens can access documents across owners. Requests without a valid token return `401`. Authenticated tokens without a subject claim fail the document-access policy with `403`.

Foreign document identifiers are returned as `404` to ordinary users. This avoids confirming whether another user's document exists.

## Protected endpoints

- `GET /api/documents`
- `POST /api/documents`
- `POST /api/documents/upload`
- `GET /api/documents/{documentId}/processing-status`
- `POST /api/documents/search`
- `POST /api/documents/ask`
- `GET /api/auth/me`

`GET /health` is intentionally anonymous.

## Local development token

Start the stack, then generate a one-hour development token:

```bash
python scripts/create_dev_token.py --user demo-user --role User
```

Generate an administrator token only for local authorization testing:

```bash
python scripts/create_dev_token.py --user demo-admin --role Admin
```

Use the token in Swagger's **Authorize** dialog, paste it into the Web UI authentication panel, or send it as an HTTP header:

```text
Authorization: Bearer <token>
```

The helper is not an identity provider and the repository signing key is development-only. Never reuse it in a deployed environment.

## Configuration

The API reads:

- `Jwt:Issuer`
- `Jwt:Audience`
- `Jwt:SigningKey`

The Development environment includes explicit local values. A non-development deployment must supply its own values through environment variables or a secret manager. Missing or weak JWT configuration prevents application startup.

A production deployment should use a real identity provider and an asymmetric signing strategy or managed key lifecycle rather than distributing a shared HMAC key.

## Database ownership migration

`infra/postgres/init/zzzz-document-ownership.sql` adds `documents.owner_id`, backfills existing rows to `legacy-system`, makes the value required, rejects blank owners, and adds an owner/date index.

PostgreSQL entrypoint scripts run only for a fresh database volume. For an existing volume:

1. back up the database;
2. review the migration;
3. apply it with `ON_ERROR_STOP` enabled;
4. verify that every document has a nonblank owner;
5. deploy the API change.

Existing pre-authentication documents belong to `legacy-system`. A normal user cannot see them unless issued that subject; an `Admin` can review them.

## Authorization invariants

- ownership is assigned from the authenticated `sub`, never from request JSON or form fields;
- document metadata and the initial ingestion job are still committed atomically;
- background processing preserves the owner while creating semantic-index records;
- PostgreSQL and in-memory search apply the same owner filter;
- `Admin` is the only role that bypasses the owner filter;
- source text returned by Search and Ask is subject to the same filter as document listing;
- negative tests cover unauthenticated access, missing subjects, and cross-user retrieval.

## Remaining production gaps

This boundary does not complete tenant/workspace isolation, audit logging, encrypted file storage, centralized secret management, token revocation, key rotation, or external identity-provider integration. The project must not be used for confidential or regulated documents until those controls and operational reviews are complete.
