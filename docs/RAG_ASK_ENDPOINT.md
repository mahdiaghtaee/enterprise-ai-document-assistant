# Source-aware Ask Endpoint

The first ask endpoint is deterministic and local. It retrieves indexed chunks and returns source context without calling an external language model.

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

`topK` is optional and defaults to `3`.

## Response Shape

```json
{
  "question": "What is the remote work policy?",
  "answer": "Based on the indexed documents, the most relevant source is from sample-policy.txt: \"...\"",
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

The exact answer text is constructed deterministically from the highest-ranked source. It demonstrates retrieval and attribution; it is not production language-model output.

## Current Processing Flow

1. Validate the question and `topK`.
2. Generate a deterministic query embedding with `IEmbeddingGenerator`.
3. Search the in-memory `ISemanticIndexStore`.
4. Map ranked records to source objects.
5. Return a deterministic answer based on the best source and include all selected source records.

This flow currently runs in the ASP.NET Core API. The FastAPI service is not involved in search or answer construction.

## Validation

- empty or whitespace-only questions return `400 Bad Request`;
- `topK <= 0` returns `400 Bad Request`;
- a missing `topK` value defaults to `3`.

## No-context Behavior

When no source chunks are available, the endpoint returns:

```text
I could not find enough indexed document context to answer this question. Upload and index a relevant document first, then try again.
```

## Limitations

- the semantic index is in memory and is cleared when the API restarts;
- the answer is extractive and deterministic;
- there is no model-provider call, reranking, citation verification, or answer-quality evaluation;
- document-level authorization is not implemented.

## Planned Evolution

A production-oriented version should:

- retrieve from a durable pgvector store;
- apply document authorization before retrieval;
- support a provider abstraction for local or external models;
- preserve source identifiers and relevant excerpts;
- apply timeouts, cancellation, error mapping, and audit logging;
- evaluate retrieval and answer quality against a representative dataset;
- defend against prompt injection and unauthorized context disclosure.
