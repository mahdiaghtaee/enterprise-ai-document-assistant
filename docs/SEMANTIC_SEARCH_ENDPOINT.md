# Semantic Search Endpoint

## Purpose

The semantic search endpoint is the first API surface for searching indexed document chunks.

It uses the current RAG foundation:

1. Query text
2. Embedding generation
3. Semantic index lookup
4. Matched chunk response with source metadata

---

## Endpoint

```http
POST /api/documents/search
```

## Request

```json
{
  "query": "enterprise AI search",
  "topK": 5
}
```

## Response

```json
{
  "query": "enterprise AI search",
  "resultCount": 1,
  "results": [
    {
      "documentId": "00000000-0000-0000-0000-000000000000",
      "fileName": "sample.txt",
      "chunkIndex": 0,
      "text": "Matched document chunk text",
      "score": 1.0
    }
  ]
}
```

---

## Current Flow

Uploaded plain-text documents are processed as follows:

```text
Upload
  -> Text extraction
  -> Chunking
  -> Embedding generation
  -> Semantic index storage
```

Search requests are processed as follows:

```text
Search query
  -> Query embedding
  -> Semantic index lookup
  -> Matched chunks with metadata
```

---

## Current Limitations

- The current embedding generator is deterministic and local.
- The current semantic index store is in memory.
- Ranking is intentionally basic and can be improved later.
- Indexed records are lost when the API process restarts.

---

## Next Steps

1. Improve ranking logic.
2. Add persistent index storage.
3. Add source attribution to answer generation.
4. Add an ask endpoint that uses search results as context.
