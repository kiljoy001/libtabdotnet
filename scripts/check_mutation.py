#!/usr/bin/env python3
"""Validate a Stryker.NET JSON report has a calculable mutation score."""

from __future__ import annotations

import json
import sys
from pathlib import Path


COUNTED_STATUSES = {"Killed", "Survived", "Timeout", "NoCoverage"}


def main() -> int:
    if len(sys.argv) != 3:
        print("usage: check_mutation.py REPORT_JSON MINIMUM_PERCENT", file=sys.stderr)
        return 2

    report_path = Path(sys.argv[1])
    minimum = float(sys.argv[2])
    report = json.loads(report_path.read_text(encoding="utf-8"))

    statuses: dict[str, int] = {}
    for file_report in report.get("files", {}).values():
        for mutant in file_report.get("mutants", []):
            status = mutant.get("status", "Unknown")
            statuses[status] = statuses.get(status, 0) + 1

    killed = statuses.get("Killed", 0)
    counted = sum(statuses.get(status, 0) for status in COUNTED_STATUSES)
    if counted == 0:
        print("mutation score: not available (0 tested mutants)", file=sys.stderr)
        print(f"mutation statuses: {statuses}", file=sys.stderr)
        return 1

    score = killed * 100.0 / counted
    print(f"mutation score: {score:.2f}% ({killed}/{counted})")
    if score < minimum:
        print(f"mutation score below required {minimum:.2f}%", file=sys.stderr)
        print(f"mutation statuses: {statuses}", file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
