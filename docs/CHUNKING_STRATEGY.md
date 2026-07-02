# Document Chunking Strategy

## Purpose

Chunking is the step that prepares extracted document text for future embedding generation, indexing, semantic search, and retrieval-augmented generation.

Large documents cannot be embedded or retrieved effectively as one block of text. They need to be split into smaller chunks while preserving enough surrounding context.

---

## Current Strategy

The current implementation uses a fixed-size character-based chunker with overlap.

Default settings:

| Setting | Value |
|---|---:|
| Max chunk length | 1,000 characters |
| Overlap length | 150 characters |

The chunker tries to move chunk endings to a whitespace boundary when possible. This reduces the chance of cutting words in the middle.

---

## Chunk Metadata

Each chunk contains:

- `documentId`
- `fileName`
- `index`
- `startOffset`
- `endOffset`
- `text`
- `characterCount`

This metadata prepares the system for future source attribution and traceability.

---

## Upload Flow

```text
Upload document
  -> Validate upload
  -> Save file to local storage
  -> Store document metadata
  -> Extract text
  -> Split extracted text into chunks
  -> Return extraction and chunking summaries
  -> Queue indexing when extraction succeeds
```

---

## Current Limitations

- Chunks are generated in memory and not persisted yet.
- Chunking is character-based, not token-based.
- Embeddings are not generated yet.
- Vector storage is not implemented yet.

---

## Next Steps

1. Persist extracted text or chunk records.
2. Add embedding generation for chunks.
3. Store chunk vectors in a vector index.
4. Add semantic search over chunk metadata.
5. Use chunk metadata for answer source attribution.
