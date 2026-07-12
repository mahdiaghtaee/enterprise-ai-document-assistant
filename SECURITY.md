# Security Policy

## Project Status

Enterprise AI Document Assistant is an open-source reference project and development portfolio. It is not currently intended to store confidential, regulated, or production business documents.

The repository demonstrates document upload, metadata persistence, retrieval, and a local RAG-style workflow. Production controls such as authentication, tenant isolation, encrypted document storage, centralized secret management, and complete audit logging are still on the roadmap.

## Supported Versions

Security fixes are applied to the latest version of the `main` branch.

## Reporting a Vulnerability

Please do not open a public issue for a vulnerability that could expose uploaded documents, credentials, service configuration, or host resources.

Report the problem privately through GitHub's security reporting features when available. Include:

- the affected component;
- steps to reproduce;
- expected and actual behavior;
- potential impact;
- a minimal proof of concept, when appropriate;
- suggested remediation, if known.

Please avoid including real credentials, private documents, personal information, or data belonging to another person or organization.

## Development Security Notes

The default Docker Compose configuration is for local development only.

- Database credentials in `docker-compose.yml` are development credentials.
- PostgreSQL, Redis, and the AI service expose local ports for debugging.
- The application currently has no user authentication or tenant isolation.
- Uploaded content should be treated as untrusted input.
- Use environment variables or a secret manager for real deployment secrets.
- Restrict service-to-service traffic and do not expose internal services publicly.
- Add TLS, document authorization, audit events, malware scanning, file-size limits, and content-type validation before handling sensitive documents.

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
