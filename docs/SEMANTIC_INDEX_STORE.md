# Semantic Index Store

## Purpose

The semantic index store is an abstraction layer for keeping generated embeddings searchable without coupling the application to a specific storage provider.

It keeps the current implementation lightweight while preserving a clean extension point for future storage providers.

---

## Current Components

- `ISemanticIndexStore`
- `SemanticIndexRecord`
- `SemanticSearchRequest`
- `SemanticSearchResult`
- `InMemorySemanticIndexStore`

The in-memory implementation is intended for tests, local development, and architecture validation.

---

## Record Metadata

Each indexed record keeps:

- `documentId`
- `fileName`
- `chunkIndex`
- `text`
- `embedding`
- `dimensions`

This prepares the system for future search responses that can point back to the original document and chunk.

---

## Current Behavior

The current store can:

1. Accept generated embeddings through `UpsertAsync`.
2. Validate document and chunk metadata.
3. Search indexed records through `SearchAsync`.
4. Filter records by embedding dimension.
5. Limit results using `TopK`.

---

## Next Steps

1. Improve ranking logic.
2. Persist indexed records beyond process memory.
3. Wire embedding generation into the upload indexing flow.
4. Add a semantic search endpoint.
5. Return source metadata in search results.
