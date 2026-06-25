# Worker Service

This folder is reserved for the background worker that will process uploaded documents.

Planned responsibilities:

- Pick up newly uploaded documents
- Send document text to the AI service
- Track indexing status
- Retry failed indexing jobs
- Store processing logs

The worker is intentionally left as a placeholder in version 1. The API and AI service need to be stable before background processing is added.
