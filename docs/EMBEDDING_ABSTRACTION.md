# Embedding Abstraction

## Purpose

The embedding abstraction prepares the RAG pipeline for provider-independent embedding generation.

Instead of coupling the application directly to one AI vendor, the API defines request and response models plus an `IEmbeddingGenerator` interface. Provider-specific implementations can be added later without changing the document-processing pipeline.

---

## Current Components

The current implementation includes:

- `IEmbeddingGenerator`
- `EmbeddingRequest`
- `EmbeddingInput`
- `EmbeddingResponse`
- `EmbeddingVector`
- `DeterministicEmbeddingGenerator`

The deterministic generator is local and test-friendly. It does not call any external AI provider and does not require API keys.

---

## Why a Deterministic Generator Exists

The local deterministic implementation is not intended to replace production embeddings.

It exists to make the architecture testable and reviewable before real providers are added.

It allows CI to validate:

- request validation
- vector response shape
- metadata preservation
- deterministic output
- provider-independent contracts

---

## Metadata Preserved Per Vector

Each generated vector preserves:

- `documentId`
- `fileName`
- `chunkIndex`
- `text`
- `values`
- `dimensions`

This prepares the system for future vector storage, semantic search, and source attribution.

---

## Future Provider Implementations

Planned provider implementations can include:

- OpenAI embeddings
- Azure OpenAI embeddings
- Ollama/local model embeddings
- Hugging Face-compatible embedding services

These providers should implement `IEmbeddingGenerator` and return the same `EmbeddingResponse` shape.

---

## Current Limitations

- The deterministic generator is only for tests and local development.
- Vectors are not persisted yet.
- Vector storage is not implemented yet.
- Semantic search is not implemented yet.

---

## Next Steps

1. Add a vector store abstraction.
2. Persist chunk vectors in an in-memory or pluggable vector store.
3. Add semantic search over stored vectors.
4. Add real provider implementations behind `IEmbeddingGenerator`.
