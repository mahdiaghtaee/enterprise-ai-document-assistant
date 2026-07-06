# RAG Ask Endpoint Implementation Plan

## Goal

Add the first portfolio-ready version of a RAG ask endpoint to the ASP.NET Core API.

The endpoint should answer a user question using indexed document chunks and return the source chunks that supported the answer.

This first version should stay deterministic and local. It should not require an external LLM provider yet. That keeps the feature easy to run, review, and demonstrate from a clean checkout.

---

## Endpoint

```http
POST /api/documents/ask
```

## Request

```json
{
  "question": "What is the remote work policy?",
  "topK": 3
}
```

## Response

```json
{
  "question": "What is the remote work policy?",
  "answer": "Based on the indexed documents, the most relevant information is...",
  "sourceCount": 3,
  "sources": [
    {
      "documentId": "00000000-0000-0000-0000-000000000000",
      "fileName": "sample-policy.txt",
      "chunkIndex": 0,
      "score": 0.91,
      "text": "Relevant source text..."
    }
  ]
}
```

---

## Implementation Steps

### 1. Add request and response models

Create models for:

- `DocumentAskRequest`
- `DocumentAskResponse`
- `DocumentAskSource`

The request should contain:

- `Question`
- `TopK`

The response should contain:

- `Question`
- `Answer`
- `SourceCount`
- `Sources`

---

### 2. Reuse the semantic search pipeline

The existing upload and search flow already creates chunks, generates deterministic embeddings, and stores vectors in the in-memory semantic index.

The ask endpoint should reuse:

- `IEmbeddingGenerator`
- `ISemanticIndexStore`
- `SemanticSearchRequest`

This keeps the first implementation small and aligned with the current architecture.

---

### 3. Generate a deterministic grounded answer

For v0.1, the answer should be generated from the retrieved chunks without calling an external model.

Suggested behavior:

- If no source chunks are found, return a clear fallback answer.
- If chunks are found, return a concise answer that explains it is based on the retrieved document context.
- Include the source chunks so a reviewer can verify the answer.

Example fallback:

```text
I could not find enough indexed document context to answer this question. Upload and index a relevant document first, then try again.
```

---

## Validation Rules

- Empty or whitespace-only question returns `400 Bad Request`.
- `topK <= 0` returns `400 Bad Request`.
- If `topK` is not provided, default to `3`.

---

## Definition of Done

- `/api/documents/ask` endpoint exists.
- Endpoint validates invalid input.
- Endpoint returns a deterministic answer.
- Endpoint returns source chunks with scores.
- README or API examples mention the endpoint.
- The feature works after running:

```bash
docker compose up --build
```

---

## Portfolio Value

This feature turns the project from document upload and semantic search into a visible RAG assistant workflow.

It also makes the repository easier to explain in interviews, freelance proposals, and technical reviews because it demonstrates the full path:

```text
Upload document -> Extract text -> Chunk -> Embed -> Search -> Ask -> Return grounded answer with sources
```
