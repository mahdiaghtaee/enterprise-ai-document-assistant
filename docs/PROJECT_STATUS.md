# Project Status

## Status

**Maintenance mode — completed v0.5.x reference milestone.**

The repository has reached the intended reference-implementation boundary for the current development cycle. It remains reviewable and maintained for security, compatibility, documentation, and reproducibility fixes, but no active feature roadmap is being pursued.

## Completed boundary

The current repository includes:

- managed multi-tenant lifecycle and durable authorization;
- PostgreSQL Row-Level Security and separated runtime identities;
- durable background ingestion and pgvector retrieval;
- safe TXT/PDF/DOCX processing boundaries;
- provider-neutral grounded answers with citation gates;
- tamper-evident audit operations and operational observability;
- reviewed English, Persian, and mixed-language evaluation gates;
- validated FastAPI 0.142.x compatibility with the current Python OpenTelemetry stack;
- CI, PostgreSQL integration, document-format integration, observability checks, CodeQL, and Dependency Review.

## Deferred work

The following items are deliberately deferred rather than partially implemented:

- .NET 10 and framework-adjacent major dependency migration;
- external IdP/SCIM integration and enterprise invitation delivery;
- production secret management, encryption, immutable audit anchoring, and legal-retention automation;
- production-scale multilingual/provider accuracy studies;
- OCR execution and richer layout/table reconstruction;
- production HA observability and paging integrations.

These may be reconsidered only if the project is intentionally resumed.

## Maintenance policy

Changes during maintenance mode should be limited to:

1. security fixes;
2. dependency compatibility fixes;
3. CI/reproducibility repairs;
4. documentation corrections;
5. explicitly approved resumption work.

No production-readiness claim is implied by this status. The repository remains a reference implementation with the boundaries documented in `README.md`, `SECURITY.md`, and the operational documentation.
