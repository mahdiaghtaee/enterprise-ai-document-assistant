#!/usr/bin/env python3
"""Verify segmented multilingual retrieval and answer evaluation reports."""

from __future__ import annotations

import argparse
import json
import random
import sys
from pathlib import Path
from typing import Any


def load_json(path: str) -> dict[str, Any]:
    with Path(path).open(encoding="utf-8") as handle:
        value = json.load(handle)
    if not isinstance(value, dict):
        raise ValueError(f"{path} must contain a JSON object.")
    return value


def percentile(values: list[float], fraction: float) -> float:
    if not values:
        return 0.0
    ordered = sorted(values)
    index = round((len(ordered) - 1) * fraction)
    return ordered[index]


def bootstrap_mean(
    values: list[float],
    iterations: int,
    seed: int,
) -> dict[str, float]:
    if not values:
        return {"mean": 0.0, "lower95": 0.0, "upper95": 0.0}

    mean = sum(values) / len(values)
    if len(values) == 1:
        return {"mean": mean, "lower95": mean, "upper95": mean}

    rng = random.Random(seed)
    samples: list[float] = []
    for _ in range(iterations):
        draw = [values[rng.randrange(len(values))] for _ in values]
        samples.append(sum(draw) / len(draw))

    return {
        "mean": mean,
        "lower95": percentile(samples, 0.025),
        "upper95": percentile(samples, 0.975),
    }


def stable_seed(base_seed: int, label: str) -> int:
    return base_seed + sum((index + 1) * ord(char) for index, char in enumerate(label))


def validate_coverage(
    manifest: dict[str, Any],
    failures: list[str],
) -> None:
    coverage = manifest["coverage"]
    languages = set(coverage["languages"])

    for collection_name, required_key in (
        ("retrievalCases", "retrievalCategories"),
        ("answerCases", "answerCategories"),
    ):
        entries = manifest[collection_name]
        for language in languages:
            present = {
                entry["category"]
                for entry in entries
                if entry["language"] == language
            }
            missing = set(coverage[required_key]) - present
            if missing:
                failures.append(
                    f"{collection_name} language {language} is missing categories: "
                    + ", ".join(sorted(missing))
                )


def build_retrieval_segments(
    report: dict[str, Any],
    manifest: dict[str, Any],
    failures: list[str],
) -> dict[str, Any]:
    by_id = {query["id"]: query for query in report["queries"]}
    iterations = int(manifest["bootstrapIterations"])
    base_seed = int(manifest["seed"])
    grouped: dict[tuple[str, str], list[dict[str, Any]]] = {}

    for metadata in manifest["retrievalCases"]:
        case_id = metadata["id"]
        query = by_id.get(case_id)
        if query is None:
            failures.append(f"Retrieval report is missing case {case_id}.")
            continue

        metrics = query.get("metrics")
        if not isinstance(metrics, dict):
            failures.append(f"Retrieval case {case_id} has no scored metrics.")
            continue

        key = (metadata["language"], metadata["category"])
        grouped.setdefault(key, []).append(metrics)

    segments: dict[str, Any] = {}
    languages = manifest["coverage"]["languages"]
    categories = manifest["coverage"]["retrievalCategories"]

    for language in languages:
        language_cases = [
            metrics
            for (segment_language, _), values in grouped.items()
            if segment_language == language
            for metrics in values
        ]
        language_result = summarize_retrieval(
            language_cases,
            iterations,
            stable_seed(base_seed, f"retrieval:{language}"),
        )
        language_result["categories"] = {}

        for category in categories:
            category_cases = grouped.get((language, category), [])
            language_result["categories"][category] = summarize_retrieval(
                category_cases,
                iterations,
                stable_seed(base_seed, f"retrieval:{language}:{category}"),
            )

        thresholds = manifest["thresholds"]["retrieval"][language]
        compare_minimum(
            failures,
            f"retrieval.{language}.precisionAtK",
            language_result["precisionAtK"]["mean"],
            thresholds["minimumPrecisionAtK"],
        )
        compare_minimum(
            failures,
            f"retrieval.{language}.recallAtK",
            language_result["recallAtK"]["mean"],
            thresholds["minimumRecallAtK"],
        )
        compare_minimum(
            failures,
            f"retrieval.{language}.meanReciprocalRank",
            language_result["meanReciprocalRank"]["mean"],
            thresholds["minimumMeanReciprocalRank"],
        )
        segments[language] = language_result

    return segments


def summarize_retrieval(
    cases: list[dict[str, Any]],
    iterations: int,
    seed: int,
) -> dict[str, Any]:
    return {
        "caseCount": len(cases),
        "precisionAtK": bootstrap_mean(
            [float(case["precisionAtK"]) for case in cases],
            iterations,
            seed + 1,
        ),
        "recallAtK": bootstrap_mean(
            [float(case["recallAtK"]) for case in cases],
            iterations,
            seed + 2,
        ),
        "meanReciprocalRank": bootstrap_mean(
            [float(case["reciprocalRank"]) for case in cases],
            iterations,
            seed + 3,
        ),
    }


def build_answer_segments(
    report: dict[str, Any],
    manifest: dict[str, Any],
    failures: list[str],
) -> dict[str, Any]:
    by_id = {case["id"]: case for case in report["cases"]}
    iterations = int(manifest["bootstrapIterations"])
    base_seed = int(manifest["seed"])
    grouped: dict[tuple[str, str], list[float]] = {}

    for metadata in manifest["answerCases"]:
        case_id = metadata["id"]
        case = by_id.get(case_id)
        if case is None:
            failures.append(f"Answer report is missing case {case_id}.")
            continue

        key = (metadata["language"], metadata["category"])
        grouped.setdefault(key, []).append(1.0 if case["passed"] else 0.0)

    segments: dict[str, Any] = {}
    languages = manifest["coverage"]["languages"]
    categories = manifest["coverage"]["answerCategories"]

    for language in languages:
        language_values = [
            value
            for (segment_language, _), values in grouped.items()
            if segment_language == language
            for value in values
        ]
        language_result: dict[str, Any] = {
            "caseCount": len(language_values),
            "caseAccuracy": bootstrap_mean(
                language_values,
                iterations,
                stable_seed(base_seed, f"answer:{language}"),
            ),
            "categories": {},
        }

        for category in categories:
            category_values = grouped.get((language, category), [])
            language_result["categories"][category] = {
                "caseCount": len(category_values),
                "caseAccuracy": bootstrap_mean(
                    category_values,
                    iterations,
                    stable_seed(base_seed, f"answer:{language}:{category}"),
                ),
            }

        minimum = manifest["thresholds"]["answers"][language][
            "minimumCaseAccuracy"
        ]
        compare_minimum(
            failures,
            f"answers.{language}.caseAccuracy",
            language_result["caseAccuracy"]["mean"],
            minimum,
        )
        segments[language] = language_result

    return segments


def compare_minimum(
    failures: list[str],
    metric: str,
    actual: float,
    minimum: float,
) -> None:
    if actual + 1e-9 < float(minimum):
        failures.append(
            f"{metric} {actual:.6f} is below minimum {float(minimum):.6f}."
        )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--retrieval-report",
        default="artifacts/retrieval-evaluation.json",
    )
    parser.add_argument(
        "--answer-report",
        default="artifacts/answer-evaluation.json",
    )
    parser.add_argument(
        "--manifest",
        default="evaluation/multilingual/manifest.v1.json",
    )
    parser.add_argument(
        "--output",
        default="artifacts/multilingual-evaluation.json",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        retrieval_report = load_json(args.retrieval_report)
        answer_report = load_json(args.answer_report)
        manifest = load_json(args.manifest)

        failures: list[str] = []
        validate_coverage(manifest, failures)
        retrieval = build_retrieval_segments(
            retrieval_report,
            manifest,
            failures,
        )
        answers = build_answer_segments(
            answer_report,
            manifest,
            failures,
        )

        result = {
            "manifestVersion": manifest["version"],
            "retrievalDatasetVersion": retrieval_report["datasetVersion"],
            "answerDatasetVersion": answer_report["datasetVersion"],
            "seed": manifest["seed"],
            "bootstrapIterations": manifest["bootstrapIterations"],
            "passed": not failures,
            "failures": failures,
            "retrieval": retrieval,
            "answers": answers,
        }

        output_path = Path(args.output)
        output_path.parent.mkdir(parents=True, exist_ok=True)
        serialized = json.dumps(result, ensure_ascii=False, indent=2)
        output_path.write_text(serialized + "\n", encoding="utf-8")
        print(serialized)
        return 0 if result["passed"] else 2
    except (KeyError, TypeError, ValueError, OSError, json.JSONDecodeError) as exc:
        print(f"Multilingual evaluation failed: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
