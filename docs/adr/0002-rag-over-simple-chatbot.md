# ADR 0002: Use RAG instead of a simple chatbot approach

## Status

Proposed

## Context

A simple chatbot can answer general questions, but enterprise clients often need answers grounded in their own private documents.

For business use cases, the assistant must be able to:

- search internal documents
- retrieve relevant context
- answer using approved sources
- show source references
- avoid unsupported claims
- support future auditing and review

## Decision

Use a Retrieval-Augmented Generation architecture instead of a simple direct chatbot flow.

## Rationale

RAG is a better fit because it allows the system to:

- ground answers in internal documents
- return source references
- separate document indexing from answer generation
- update knowledge without retraining a model
- support private enterprise knowledge bases

## Consequences

### Positive

- More trustworthy business answers
- Better client value
- Easier explanation to enterprise stakeholders
- Supports document-level traceability
- Clearer technical roadmap

### Trade-offs

- Requires document processing
- Requires chunking and retrieval strategy
- Requires embedding and vector search infrastructure
- Requires careful handling of source attribution

## Portfolio Message

This decision makes the project more valuable for freelance positioning because it demonstrates an enterprise AI pattern rather than a generic chatbot demo.
