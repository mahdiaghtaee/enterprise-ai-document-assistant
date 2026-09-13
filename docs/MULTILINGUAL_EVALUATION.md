# Multilingual Quality Evaluation

This document defines the reviewed, deterministic multilingual quality suite used to detect retrieval and grounded-answer regressions without claiming production accuracy.

## Scope

The suite covers three explicit language segments:

- `en`: English;
- `fa`: Persian;
- `mixed`: mixed English/Persian content and questions.

All content is synthetic and non-sensitive. No production documents, customer data, provider credentials, or paid provider calls are required.

## Versioned Inputs

The suite uses:

- `evaluation/retrieval/corpus.v2.json` for multilingual retrieval cases;
- `evaluation/retrieval/baseline.v2.json` for the aggregate deterministic retrieval gate;
- `evaluation/answers/cases.v2.json` for multilingual answer and grounding cases;
- `evaluation/answers/baseline.v2.json` for the aggregate deterministic answer gate;
- `evaluation/multilingual/manifest.v1.json` for language/category assignments, reviewer rationale, support/citation/completeness judgments, deterministic bootstrap settings, and segmented thresholds.

The v1 inputs remain available as the original small baseline. The v2 inputs expand coverage without changing the public Search or Ask contracts.

## Reviewed Coverage

Every language segment contains retrieval cases for:

- exact retrieval;
- vocabulary mismatch;
- ambiguity;
- duplicates;
- long-document context;
- format-derived text.

Every language segment also contains answer cases for:

- grounded answers;
- insufficient evidence;
- conflicting evidence;
- adversarial/instruction-like source text;
- format-derived evidence.

The manifest records the explicit reviewer rationale for every case. Answer cases also record support, citation, and completeness judgments suitable for repository review.

## Metrics

The existing retrieval evaluator continues to produce per-query:

- Precision@K;
- Recall@K;
- reciprocal rank;
- latency.

The existing answer evaluator continues to produce deterministic case outcomes for grounding, insufficient-evidence, provider-call, and citation gates.

`scripts/verify_multilingual_evaluation.py` joins those machine-readable reports with the review manifest and produces:

- per-language retrieval Precision@K, Recall@K, and MRR;
- per-language and per-category retrieval metrics;
- per-language and per-category answer accuracy;
- deterministic 95% bootstrap confidence intervals with a fixed seed;
- explicit threshold failures for English, Persian, and mixed-language segments.

A Persian or mixed-language regression can therefore fail CI even when an aggregate metric remains above its aggregate threshold.

## Run Locally

From the repository root:

```bash
dotnet run \
  --project tools/retrieval-evaluation/EnterpriseDocumentAssistant.RetrievalEvaluation.csproj \
  --configuration Release \
  -- \
  --dataset evaluation/retrieval/corpus.v2.json \
  --baseline evaluation/retrieval/baseline.v2.json \
  --output artifacts/retrieval-evaluation-v2.json

dotnet run \
  --project tools/answer-evaluation/EnterpriseDocumentAssistant.AnswerEvaluation.csproj \
  --configuration Release \
  -- \
  --dataset evaluation/answers/cases.v2.json \
  --baseline evaluation/answers/baseline.v2.json \
  --output artifacts/answer-evaluation-v2.json

python3 scripts/verify_multilingual_evaluation.py \
  --retrieval-report artifacts/retrieval-evaluation-v2.json \
  --answer-report artifacts/answer-evaluation-v2.json \
  --manifest evaluation/multilingual/manifest.v1.json \
  --output artifacts/multilingual-evaluation.json
```

The final command returns a non-zero exit code when a segmented threshold fails.

## CI

`.github/workflows/multilingual-evaluation.yml` runs the v2 retrieval and answer evaluations, enforces segmented language gates, verifies the machine-readable report contract, and retains all three reports as workflow artifacts.

The workflow remains credential-free and deterministic.

## Statistical Interpretation

Bootstrap intervals are reproducible because both the seed and iteration count are versioned in the manifest. They describe uncertainty within this reviewed synthetic case set only.

They do not establish:

- production factual accuracy;
- population-level representativeness;
- provider quality;
- production latency or scale;
- regulatory suitability;
- Persian-language coverage beyond the committed cases.

Any external-provider comparison must remain opt-in, use non-sensitive data, and be reviewed separately from pull-request CI.

## Maintenance Rules

When adding or changing a case:

1. update the corresponding versioned dataset;
2. update the manifest metadata and reviewer rationale;
3. preserve explicit language and category coverage;
4. run the deterministic reports;
5. review measured metrics before tightening or relaxing thresholds;
6. avoid changing thresholds solely to make a regression pass;
7. update documentation only to claims directly supported by the committed reports.
