# Security Policy

## Project Status

Enterprise AI Document Assistant is an open-source reference project and development portfolio. It is not currently intended to store confidential, regulated, or production business documents.

The repository demonstrates JWT authentication, durable tenant and membership authorization, database-enforced tenant isolation, one-time invitation handling, user ownership, durable ingestion, safe TXT/PDF/DOCX upload boundaries, tenant-scoped semantic retrieval, append-only audit events, correlation identifiers, structured logging, OpenTelemetry-compatible diagnostics, and optional grounded-answer providers. Production controls such as external identity-provider synchronization, domain verification, encrypted document storage, centralized secret management, managed key rotation, token revocation, tenant quotas, retention/deletion automation, audit archival, alerting, production malware-scanner operations, and external immutable audit storage remain on the roadmap.

## Supported Versions

Security fixes are applied to the latest version of the `main` branch.

## Reporting a Vulnerability

Do not open a public issue for a vulnerability that could expose uploaded documents, credentials, service configuration, authorization boundaries, tenant data, invitation tokens, audit records, or host resources.

Report the problem privately through GitHub's security reporting features when available. Include:

- the affected component;
- steps to reproduce;
- expected and actual behavior;
- potential impact;
- a minimal proof of concept, when appropriate;
- suggested remediation, if known.

Avoid including real credentials, invitation secrets, private documents, personal information, or data belonging to another person or organization.

## Implemented Access Boundary

The ASP.NET Core API validates signed JWT bearer tokens and requires stable `sub`, `tenant_id`, and role claims. JWT claims authenticate the requested identity and tenant, but durable lifecycle state is authoritative for non-platform access.

- `User` requires an active durable membership and can access only its own documents inside the active tenant;
- `Admin` requires an active durable Admin membership and can access all owners and audit events inside its active tenant;
- `PlatformAdmin` uses explicit platform APIs and a separately scoped cross-tenant database role.

A JWT Admin claim does not elevate a durable User membership. The request is rejected until a correctly scoped token is issued. Removing a membership, downgrading an Admin, or disabling a tenant takes effect on the next request without waiting for JWT expiration.

Owner and tenant identity are assigned from validated claims and cannot be supplied by document request bodies. Document list, processing status, semantic search, Ask, returned source text, tenant administration, and audit reads share the same authenticated access context. Foreign document identifiers return `404` outside the caller's authorized scope.

PostgreSQL stores `tenant_id` on tenants, memberships, invitations, documents, semantic chunks, ingestion jobs, and audit events. Forced Row-Level Security protects runtime roles, and composite foreign keys prevent child records from using a tenant different from the referenced document. Runtime transactions set `app.tenant_id` locally; missing context fails closed.

The final active tenant Admin cannot be removed or downgraded. The PostgreSQL path locks the relevant membership rows in the same transaction before enforcing this invariant.

See [authentication and authorization](docs/AUTHENTICATION_AND_AUTHORIZATION.md), [tenant isolation](docs/TENANT_ISOLATION.md), [managed tenant lifecycle](docs/TENANT_LIFECYCLE.md), [safe document extraction](docs/TEXT_EXTRACTION_PIPELINE.md), and [health, audit, and observability](docs/HEALTH_AND_OBSERVABILITY.md).

## Document Upload and Extraction Boundary

Uploaded documents are untrusted input. The API does not treat a filename extension or multipart MIME value as sufficient proof of format.

Before durable enqueue:

- upload size is limited to 10 MB;
- only `.txt`, `.pdf`, and `.docx` are accepted;
- extension and declared content type must agree;
- PDF bytes must have a `%PDF-` signature and must be parseable by PdfPig;
- PDF page count must remain within the configured limit;
- DOCX must be a valid ZIP/OOXML package with the expected content-type manifest and `word/document.xml`;
- DOCX archive entry count and total uncompressed bytes are bounded;
- absolute and traversal-style DOCX archive paths are rejected;
- DOCX XML uses `DtdProcessing.Prohibit`, no external resolver, and a maximum XML-character limit;
- text uploads reject binary NUL data and invalid UTF-8 in the inspected prefix;
- an optional malware scanner may reject the upload before file persistence or database enqueue.

The independent worker repeats relevant safety limits instead of trusting only API-side validation. It enforces strict UTF-8, PDF page limits, DOCX archive/XML limits, total extracted-character limits, and cancellation. A textless/image-only PDF returns `ocr-required` and is not silently indexed as an empty document.

The reference project uses PdfPig content-order extraction for text-bearing PDFs. PDF is a presentation format; reading order and complex layout/table reconstruction are not guaranteed by this boundary.

### Malware scanning

`FileThreatScanning:Provider=Disabled` is the explicit local default. No malware service is bundled or contacted in that mode, and `/health` reports the selected provider.

`FileThreatScanning:Provider=ClamAv` uses the clamd TCP `INSTREAM` protocol. When enabled:

- a clean `OK` verdict allows upload processing to continue;
- a `FOUND` verdict rejects the upload with the controlled `malware-detected` code;
- timeout, socket/I/O failure, or an unexpected scanner response fails closed with `malware-scanner-unavailable`;
- raw scanner responses and signature names are not returned, logged, audited, or recorded as metric dimensions.

A production deployment must operate its scanner as a separately secured service with current signatures, restricted network access, health/availability monitoring, and an explicit failure policy. Enabling the integration point alone is not equivalent to operating a production malware-defense program.

Password-protected documents, OCR execution, content disarm/reconstruction, sandboxed rendering, and legacy Office formats remain unsupported.

## Invitation Security

Tenant invitations are bound to a target tenant, authenticated subject identifier, intended role, and expiry.

- plaintext tokens are generated from 32 cryptographically random bytes;
- only a SHA-256 digest is stored;
- plaintext is returned once and never appears in listing responses;
- acceptance requires a JWT whose `sub` and `tenant_id` match the invitation;
- accepted, revoked, or expired invitations cannot be replayed;
- invitation tokens and token digests are excluded from audit metadata and telemetry.

The repository does not implement email delivery, domain verification, recipient identity proofing, or Admin-invitation approval. Production delivery must use a trusted channel and must not place invitation tokens in logs, analytics, referrer URLs, or support tickets.

## Database and Process Trust Boundaries

Docker Compose separates three non-superuser, non-`BYPASSRLS` database roles:

- `document_app`: tenant-scoped public API access;
- `document_platform`: platform lifecycle mutations, cross-tenant reads, and audit insertion without ingestion/document mutation privileges;
- `document_privileged`: background-ingestion writes, retries, and recovery.

The public `document-api` container receives `document_app` and `document_platform`; it does not receive `document_privileged`. The independent `document-worker` receives `document_privileged`, has no published host port, and shares only the named document-storage volume required for queued processing.

`ApplicationMode=Api`, `Worker`, or `Combined` controls process behavior. Compose uses separate API and Worker processes. Combined mode exists for isolated tests and compatibility, not as the recommended production trust boundary.

## Audit Boundary

The audit table grants application roles only the required `SELECT` and `INSERT` privileges. Database triggers record document and ingestion state changes in the same transaction. Application audit events add action, result count, duration, correlation ID, and trace ID without storing document text, source chunks, search queries, questions, generated answers, provider response bodies, invitation tokens, bearer tokens, file content, malware-signature names, or raw scanner responses.

Lifecycle audit events record tenant provisioning/status changes, membership role/removal operations, and invitation creation/acceptance/revocation. They store bounded identifiers and outcomes but no invitation secret.

Successful upload audit records may include bounded content type, size, and scanner provider/status values. They do not include the original document bytes, extracted text, scanner response, or scanner signature name.

## Correlation and Telemetry Safety

`X-Correlation-ID` is a diagnostic identifier, not an authorization credential. Supplied values are length- and character-validated before being included in response headers, logs, traces, or audit events.

Telemetry tags and metrics avoid user identifiers, file names, query text, question text, invitation tokens, generated answers, and other unbounded high-cardinality content. Logs must not include:

- bearer or invitation tokens;
- document or source-chunk content;
- search queries, questions, or generated answers;
- provider response bodies or API keys;
- malware scanner response bodies or signature names;
- database passwords or full connection strings;
- uploaded file bytes.

An OTLP exporter endpoint is operational configuration and must be restricted to trusted infrastructure. Exporter failure must not weaken authorization or tenant isolation.

## Development Security Notes

The default Docker Compose configuration is for local development only.

- The repository JWT signing key and token helper are development-only.
- Database passwords in `.env.example` are local credentials.
- The demo can provision a local tenant and membership using development PlatformAdmin/Admin tokens.
- PostgreSQL, Redis, and the AI service expose local ports for debugging.
- Existing data is mapped to explicit legacy tenants/memberships during migration and must be reviewed.
- Uploaded content must be treated as untrusted input even after format inspection.
- Local malware scanning is deliberately disabled unless a ClamAV endpoint is configured; the stack does not bundle a scanner daemon.
- Invitation delivery, encrypted storage, audit retention, and production telemetry storage are not implemented.
- Use a secret manager, TLS, restricted networking, managed identity, independently deployable service identities, and an operated malware-scanning boundary before production deployment.

## Tenant Deployment Requirements

A deployment derived from this project must:

- issue stable, non-reassignable user and tenant identifiers;
- synchronize durable memberships with a trusted identity lifecycle or establish an equivalent reviewed process;
- restrict who can issue or obtain `PlatformAdmin` tokens;
- protect the separate public API, platform-management, and worker database identities;
- use unique managed database passwords and rotate them;
- verify Row-Level Security, policies, grants, role flags, invitation constraints, and final-Admin protection after every migration;
- test cross-tenant list, status, search, Ask, audit, membership, invitation, insert, update, and deletion paths;
- define tenant deactivation, retention, export, archival, and deletion behavior;
- prevent tenant identifiers and membership roles from being changed through document payloads;
- deliver invitation tokens only through trusted channels;
- export or protect audit records with controls appropriate to the applicable threat model.

## JWT Deployment Requirements

Replace every development JWT setting. At minimum:

- use a managed identity provider;
- validate issuer, audience, signature, expiration, intended token type, and subject stability;
- use managed asymmetric signing keys or a controlled HMAC key lifecycle;
- implement key rotation and session/token revocation behavior;
- review administrator and PlatformAdmin privilege-escalation paths;
- issue refreshed tokens after durable role changes;
- avoid logging bearer tokens or document content;
- add rate limits and monitoring for authentication failures.

Durable membership revocation limits application access immediately, but it does not revoke the identity-provider session or invalidate a stolen token outside this application.

## Audit Deployment Requirements

The implemented audit ledger is append-only to application roles, but a production deployment must also define:

- retention and archival periods;
- access-review and break-glass procedures;
- backup and restore verification;
- tamper-evident hashing or immutable external storage where required;
- alerting for audit persistence failures, suspicious lifecycle mutations, and repeated invitation failures;
- legal hold, subject-access, export, and deletion obligations;
- protection of platform and worker database roles.

## Dependency and Container Hygiene

For any deployment derived from this project:

- pin and review dependency updates;
- scan application and container dependencies;
- use minimal, non-root runtime images where possible;
- rotate credentials, invitation-delivery secrets, and provider keys;
- keep PostgreSQL and Redis off the public internet;
- apply database backups, retention rules, and deletion policies appropriate to the data;
- retain the separated worker trust boundary rather than copying its credential into the public API;
- secure collector and telemetry-backend endpoints;
- operate malware-scanning infrastructure with current signatures and restricted connectivity when enabled;
- monitor parser/scanner failures and review configured file, archive, page, XML, extraction, and processing-time limits.

## AI-Specific Considerations

A production document assistant should also address:

- prompt injection in uploaded documents;
- unauthorized retrieval across users or tenants;
- accidental disclosure through generated answers;
- source attribution and answer traceability;
- retention and deletion of embeddings and derived content;
- provider data-handling terms when external AI services are used;
- tenant-specific approval before external provider activation.
