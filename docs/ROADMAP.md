# Roadmap

This roadmap keeps the project focused on a realistic freelance-ready product: a secure internal document assistant that can be deployed, demonstrated, and extended for client needs.

## Milestone 1 — Working Local Demo

Goal: make the project easy to run and understand in less than five minutes.

- Keep Docker Compose as the default local setup
- Validate health checks for the .NET API and Python AI service
- Keep Swagger enabled for API exploration
- Add a small sample upload flow
- Document the system boundaries clearly

## Milestone 2 — Persistent Document Metadata

Goal: replace the in-memory repository with real persistence.

- Add PostgreSQL database access
- Create document metadata model
- Add migrations
- Store uploaded document metadata
- Keep local file storage for the first version

## Milestone 3 — Document Processing Pipeline

Goal: move from upload-only to useful document indexing.

- Extract text from PDF and text files
- Split documents into chunks
- Store chunks with document references
- Add indexing status to the document model
- Add a background worker for indexing jobs

## Milestone 4 — Semantic Search and RAG

Goal: allow users to ask questions and receive grounded answers.

- Add embeddings
- Store vectors with pgvector or a compatible vector store
- Add semantic search endpoint
- Add question-answer endpoint
- Return source references with every answer

## Milestone 5 — Product Readiness

Goal: make the project presentable to clients.

- Add JWT authentication
- Add role-based access
- Add integration tests
- Add GitHub Actions workflow
- Add deployment notes
- Add screenshots and a short demo video

## Not in Scope Yet

These features are useful but should not be added before the core workflow is stable:

- Multi-tenant billing
- Complex admin dashboard
- Advanced document permissions
- Cloud deployment automation
- OCR for scanned documents

## Positioning

This project should communicate one clear message: practical backend and AI engineering for businesses that need document automation, internal search, and knowledge assistant tools.
