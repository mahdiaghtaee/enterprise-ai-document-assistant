#!/usr/bin/env python3

import argparse
import glob
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Validate aggregate line and branch coverage from Cobertura reports."
    )
    parser.add_argument("pattern", help="Glob pattern for coverage.cobertura.xml files")
    parser.add_argument("--minimum-line-rate", type=float, default=0.60)
    parser.add_argument("--minimum-branch-rate", type=float, default=0.50)
    return parser.parse_args()


def aggregate_reports(paths: list[Path]) -> tuple[float, float]:
    lines_covered = 0
    lines_valid = 0
    branches_covered = 0
    branches_valid = 0

    for path in paths:
        root = ET.parse(path).getroot()
        lines_covered += int(root.attrib.get("lines-covered", "0"))
        lines_valid += int(root.attrib.get("lines-valid", "0"))
        branches_covered += int(root.attrib.get("branches-covered", "0"))
        branches_valid += int(root.attrib.get("branches-valid", "0"))

    if lines_valid == 0:
        raise ValueError("Coverage reports contain no valid lines.")

    line_rate = lines_covered / lines_valid
    branch_rate = branches_covered / branches_valid if branches_valid else 1.0
    return line_rate, branch_rate


def main() -> int:
    args = parse_args()
    paths = sorted(Path(path) for path in glob.glob(args.pattern, recursive=True))

    if not paths:
        print(f"No coverage reports matched: {args.pattern}", file=sys.stderr)
        return 2

    try:
        line_rate, branch_rate = aggregate_reports(paths)
    except (ET.ParseError, OSError, ValueError) as exc:
        print(f"Unable to validate coverage: {exc}", file=sys.stderr)
        return 2

    print(f"Coverage reports: {len(paths)}")
    print(f"Line coverage: {line_rate:.2%} (minimum {args.minimum_line_rate:.2%})")
    print(f"Branch coverage: {branch_rate:.2%} (minimum {args.minimum_branch_rate:.2%})")

    failures: list[str] = []
    if line_rate < args.minimum_line_rate:
        failures.append("line coverage is below the configured minimum")
    if branch_rate < args.minimum_branch_rate:
        failures.append("branch coverage is below the configured minimum")

    if failures:
        for failure in failures:
            print(f"Coverage check failed: {failure}", file=sys.stderr)
        return 1

    print("Coverage floors satisfied.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
