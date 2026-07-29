# Security Policy

## Project Status

Enterprise AI Document Assistant is an open-source reference project and development portfolio. It is not currently intended to store confidential, regulated, or production business documents.

The repository demonstrates JWT authentication, role-based authorization, user ownership, database-enforced tenant isolation, durable ingestion, and tenant-scoped semantic retrieval. Production controls such as tenant provisioning, identity lifecycle, encrypted document storage, centralized secret management, key rotation, token revocation, complete audit logging, and operational monitoring remain on the roadmap.

## Supported Versions

Security fixes are applied to the latest version of the `main` branch.

## Reporting a Vulnerability

Do not open a public issue for a vulnerability that could expose uploaded documents, credentials, service configuration, authorization boundaries, tenant data, or host resources.

Report the problem privately through GitHub's security reporting features when available. Include:

- the affected component;
- steps to reproduce;
- expected and actual behavior;
- potential impact;
- a minimal proof of concept, when appropriate;
- suggested remediation, if known.

Avoid including real credentials, private documents, personal information, or data belonging to another person or organization.

## Implemented Access Boundary

The ASP.NET Core API validates signed JWT bearer tokens and requires stable `sub`, `tenant_id`, and role claims.

- `User` can access only its own documents inside its tenant;
- `Admin` can access all document owners inside its tenant;
- `PlatformAdmin` can access tenants through an explicit privileged database path.

Owner and tenant identity are assigned from validated claims and cannot be supplied by document request bodies. Document list, processing status, semantic search, Ask, and returned source text share the same access context. Foreign document identifiers return `404` outside the caller's authorized scope.

PostgreSQL stores `tenant_id` on documents, semantic chunks, and ingestion jobs. Forced Row-Level Security protects the runtime database role, and composite foreign keys prevent child records from using a tenant different from the referenced document. Runtime transactions set `app.tenant_id` locally; missing context fails closed.

See [authentication and authorization](docs/AUTHENTICATION_AND_AUTHORIZATION.md) and [tenant isolation](docs/TENANT_ISOLATION.md).

## Development Security Notes

The default Docker Compose configuration is for local development only.

- The repository JWT signing key and token helper are development-only.
- Database passwords in `.env.example` are local credentials.
- The tenant-runtime and privileged database credentials are both available to the API container in the reference deployment.
- PostgreSQL, Redis, and the AI service expose local ports for debugging.
- Existing pre-authentication data is assigned to `legacy-system` and `legacy-tenant` during migration.
- Uploaded content must be treated as untrusted input.
- Tenant provisioning, audit logging, encrypted storage, and malware scanning are not implemented.
- Use a secret manager, TLS, restricted networking, managed identity, and an independently reviewed privileged-service boundary before deployment.

## Tenant Deployment Requirements

A deployment derived from this project must:

- issue stable, non-reassignable user and tenant identifiers;
- verify tenant membership and role assignment at the identity provider;
- restrict who can issue or obtain `PlatformAdmin` tokens;
- separate public API credentials from privileged worker and administration credentials;
- use unique managed database passwords and rotate them;
- verify Row-Level Security, policies, and runtime role flags after every migration;
- test cross-tenant list, status, search, Ask, insert, update, and deletion paths;
- define tenant lifecycle, retention, export, and deletion behavior;
- prevent tenant identifiers from being changed through client-controlled document fields;
- record access and administrative events in a tamper-resistant audit system.

## JWT Deployment Requirements

Replace every development JWT setting. At minimum:

- use a managed identity provider;
- validate issuer, audience, signature, expiration, and intended token type;
- use managed asymmetric signing keys or a controlled HMAC key lifecycle;
- implement key rotation and revocation behavior;
- review administrator and platform-administrator privilege escalation paths;
- avoid logging bearer tokens or document content;
- add rate limits and monitoring for authentication failures.

Missing or weak JWT configuration causes API startup to fail rather than silently exposing document endpoints.

## Dependency and Container Hygiene

For any deployment derived from this project:

- pin and review dependency updates;
- scan application and container dependencies;
- use minimal, non-root runtime images where possible;
- rotate credentials and API keys;
- keep PostgreSQL and Redis off the public internet;
- apply database backups, retention rules, and deletion policies appropriate to the data;
- separate privileged background processing from the public API trust boundary.

## AI-Specific Considerations

A production document assistant should also address:

- prompt injection in uploaded documents;
- unauthorized retrieval across users or tenants;
- accidental disclosure through generated answers;
- source attribution and answer traceability;
- retention and deletion of embeddings and derived content;
- provider data-handling terms when external AI services are used.
