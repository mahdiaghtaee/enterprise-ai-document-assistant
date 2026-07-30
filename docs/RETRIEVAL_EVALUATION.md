# Retrieval Quality Evaluation

This document describes the reproducible retrieval-quality evaluation path for the Enterprise AI Document Assistant.

## Objective

The evaluation establishes a measurable baseline before changing embedding generators, similarity ranking, chunking, or provider integrations. It uses the same public `IEmbeddingGenerator` and `ISemanticIndexStore` implementations as the application while leaving the public Search and Ask response contracts unchanged.

The current baseline is intentionally a development baseline. It does not claim production retrieval quality.

## Versioned inputs

The evaluation inputs are committed under `evaluation/retrieval/`:

- `corpus.v1.json` contains six synthetic tenant-safe documents, twelve chunks, and seven queries;
- `baseline.v1.json` records the observed deterministic quality metrics and the regression thresholds enforced by CI.

Each query declares one category and the exact document/chunk pairs considered relevant.

Current categories:

- `exact`: query text closely matches one relevant chunk;
- `ambiguous`: more than one chunk is relevant;
- `vocabulary_mismatch`: the query uses different vocabulary from the relevant chunk;
- `empty`: the query is intentionally blank and must be handled without invoking the embedding generator.

The corpus contains no confidential, customer, or production data. Document, tenant, and owner identifiers are fixed so repeated runs are comparable.

## Evaluation command

Run from the repository root:

```bash
dotnet run \
  --project tools/retrieval-evaluation/EnterpriseDocumentAssistant.RetrievalEvaluation.csproj \
  --configuration Release \
  -- \
  --dataset evaluation/retrieval/corpus.v1.json \
  --baseline evaluation/retrieval/baseline.v1.json \
  --output artifacts/retrieval-evaluation.json
```

All arguments have the paths shown above as defaults, so the shorter form is also valid:

```bash
dotnet run --project tools/retrieval-evaluation/EnterpriseDocumentAssistant.RetrievalEvaluation.csproj
```

Exit codes:

| Code | Meaning |
|---|---|
| `0` | Evaluation completed and all thresholds passed |
| `1` | Input, validation, serialization, or runtime failure |
| `2` | Evaluation completed but at least one regression threshold failed |
| `130` | Evaluation was cancelled |

The command writes the full JSON report to the configured output path and prints the same JSON to standard output.

## Runtime path under evaluation

The tool does not reimplement retrieval logic. It directly uses:

- `DeterministicEmbeddingGenerator` for corpus and query vectors;
- `InMemorySemanticIndexStore` for cosine ranking and deterministic tie-breaking;
- the production `SemanticIndexRecord` and `SemanticSearchRequest` contracts;
- the same owner and tenant filters used by the application retrieval path.

The in-memory provider keeps the check fast and independent of PostgreSQL availability. PostgreSQL/pgvector persistence and tenant isolation remain covered by the existing integration and Compose workflows. Provider-specific evaluation can be added later without changing the dataset or metric contract.

## Metrics

Quality metrics are averaged across non-empty queries.

### Precision at K

`Precision@K` is the number of relevant chunks in the first `K` results divided by `K`.

The denominator remains `K` even when fewer results are returned. This makes result-count changes visible instead of silently improving precision.

### Recall at K

`Recall@K` is the number of retrieved relevant chunks divided by the number of relevant chunks declared for the query.

This is particularly important for ambiguous queries that have more than one acceptable source.

### Mean reciprocal rank

The reciprocal rank for a query is `1 / rank` for the first relevant result, or zero when no relevant result appears in the first `K` results. The report averages that value as mean reciprocal rank (`MRR`).

### Empty-query accuracy

Empty queries are not sent to the embedding generator. They pass only when they declare no relevant chunks and produce no ranked results. Empty queries are excluded from precision, recall, MRR, and latency averages.

### Latency

The report records mean and p95 elapsed milliseconds for local query embedding plus in-memory ranking. The latency threshold is a broad regression guard for this deterministic CI workload, not a production service-level objective or capacity claim.

## Version 1 baseline

The committed baseline records the following observed quality at `K = 3`:

| Metric | Observed | Enforced minimum |
|---|---:|---:|
| Precision@3 | `0.277778` | `0.27` |
| Recall@3 | `0.75` | `0.74` |
| MRR | `0.833333` | `0.82` |
| Empty-query accuracy | `1.0` | `1.0` |

The maximum mean local latency is `250 ms`.

The low precision is expected because every scored query returns three results while most queries declare one relevant chunk. The vocabulary-mismatch query currently misses its relevant backup chunk in the first three results. The ambiguous query retrieves only one of its two relevant chunks. These limitations are retained in the report rather than hidden by selecting only successful examples.

The thresholds are close enough to the observed deterministic baseline that losing an exact-match query causes CI to fail, while minor floating-point differences remain acceptable.

## Machine-readable report

The JSON report includes:

- dataset and embedding-model versions;
- query and category counts;
- aggregate quality and latency metrics;
- observed baseline values and active thresholds;
- pass/fail status with explicit failure messages;
- per-query expected relevant chunks;
- ranked document/chunk identifiers, scores, ranks, and relevance flags;
- per-query precision, recall, reciprocal rank, and elapsed time.

The report deliberately contains the synthetic evaluation query and chunk text only through the versioned input files. It must not be reused with confidential documents unless storage, retention, access, and artifact-handling policies are reviewed.

## CI behavior

`.github/workflows/retrieval-evaluation.yml`:

1. restores and runs the dedicated .NET evaluation tests;
2. executes the versioned corpus against the baseline;
3. fails when the tool returns a non-zero status;
4. validates the report schema and required query categories;
5. uploads `retrieval-evaluation.json` as a fourteen-day workflow artifact.

The workflow has read-only repository permissions and requires no paid AI credentials, database service, or telemetry collector.

## Updating the corpus or baseline

A corpus or baseline change must be intentional and reviewed.

1. Create a new versioned corpus when documents, chunks, relevance judgments, query categories, or `TopK` change.
2. Run the previous and proposed versions and include both reports in the pull request.
3. Explain every quality regression rather than simply lowering a threshold.
4. Keep old versioned files when they are useful for historical comparison.
5. Update the baseline only after confirming the ranking change is desired and tenant/owner filtering remains intact.
6. Do not mix provider migration, corpus relabeling, and threshold relaxation in one opaque change.

## Limitations and next work

The first corpus is intentionally small and synthetic. It is useful for deterministic regression detection, not statistical claims about production search quality.

Remaining work includes:

- a larger representative and reviewed corpus;
- inter-annotator relevance review;
- multilingual, long-document, duplicate, and adversarial queries;
- confidence intervals and category-level thresholds;
- PostgreSQL and external-provider comparison runs;
- grounded-answer citation correctness and answer-support metrics;
- load, concurrency, memory, and end-to-end service latency evaluation;
- provider cost and token-usage reporting where applicable.
