# Security Policy

## Project Status

Enterprise AI Document Assistant is an open-source reference project and development portfolio. It is not currently intended to store confidential, regulated, or production business documents.

The repository now demonstrates JWT authentication, role-based authorization, document ownership, durable ingestion, metadata persistence, and owner-filtered retrieval. Production controls such as tenant/workspace isolation, encrypted document storage, centralized secret management, key rotation, token revocation, and complete audit logging remain on the roadmap.

## Supported Versions

Security fixes are applied to the latest version of the `main` branch.

## Reporting a Vulnerability

Please do not open a public issue for a vulnerability that could expose uploaded documents, credentials, service configuration, authorization boundaries, or host resources.

Report the problem privately through GitHub's security reporting features when available. Include:

- the affected component;
- steps to reproduce;
- expected and actual behavior;
- potential impact;
- a minimal proof of concept, when appropriate;
- suggested remediation, if known.

Please avoid including real credentials, private documents, personal information, or data belonging to another person or organization.

## Implemented Access Boundary

The ASP.NET Core document API validates signed JWT bearer tokens and requires a stable `sub` claim. Ordinary `User` tokens can access only documents owned by that subject. `Admin` tokens can access documents across owners.

Ownership is assigned by the API from the authenticated subject and cannot be supplied by upload or metadata request bodies. Document list, processing status, semantic search, Ask, and returned source text share the same owner filter. Foreign document identifiers return `404` to ordinary users.

This is an application-level user boundary, not a complete multi-tenant security model. See [authentication and authorization documentation](docs/AUTHENTICATION_AND_AUTHORIZATION.md).

## Development Security Notes

The default Docker Compose configuration is for local development only.

- The repository JWT signing key and token helper are development-only.
- Database credentials in `docker-compose.yml` are development credentials.
- PostgreSQL, Redis, and the AI service expose local ports for debugging.
- Tenant/workspace isolation and audit logging are not implemented.
- Existing pre-authentication documents are assigned to `legacy-system` during migration.
- Uploaded content should be treated as untrusted input.
- Use environment variables or a secret manager for deployment secrets.
- Restrict service-to-service traffic and do not expose internal services publicly.
- Add TLS, tenant authorization, audit events, encryption, malware scanning, file-signature validation, and operational limits before handling sensitive documents.

## JWT Deployment Requirements

A deployment derived from this project must replace every development JWT setting. At minimum:

- use a managed identity provider;
- validate issuer, audience, signature, expiration, and intended token type;
- use managed asymmetric signing keys or a controlled HMAC key lifecycle;
- implement key rotation and revocation behavior;
- issue stable, non-reassignable subject identifiers;
- review administrator-role assignment and privilege escalation paths;
- avoid logging bearer tokens or document content;
- add rate limits and monitoring for authentication failures.

Missing or weak JWT configuration causes API startup to fail rather than silently exposing document endpoints.

## Dependency and Container Hygiene

For any deployment derived from this project:

- pin and review dependency updates;
- scan application and container dependencies;
- use minimal runtime images;
- run services as non-root users where possible;
- rotate credentials and API keys;
- keep PostgreSQL and Redis off the public internet;
- apply database backups, retention rules, and deletion policies appropriate to the data.

## AI-Specific Considerations

A production document assistant should also address:

- prompt injection in uploaded documents;
- unauthorized retrieval across users or workspaces;
- accidental disclosure through generated answers;
- source attribution and answer traceability;
- retention and deletion of embeddings and derived content;
- provider data-handling terms when external AI services are used.
