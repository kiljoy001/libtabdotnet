#!/usr/bin/env python3
import pathlib
import sys
import xml.etree.ElementTree as ET


def main() -> int:
    if len(sys.argv) != 4:
        print("usage: check_coverage.py <coverage-dir> <min-line-percent> <min-branch-percent>", file=sys.stderr)
        return 2

    root = pathlib.Path(sys.argv[1])
    min_line = float(sys.argv[2])
    min_branch = float(sys.argv[3])
    reports = sorted(root.glob("**/coverage.cobertura.xml"))
    if not reports:
        print(f"no Cobertura reports found under {root}", file=sys.stderr)
        return 1

    lines: dict[tuple[str, str], dict[str, int]] = {}
    for report in reports:
        tree = ET.parse(report)
        for class_node in tree.findall(".//class"):
            filename = class_node.attrib.get("filename", "")
            for line in class_node.findall(".//line"):
                key = (filename, line.attrib.get("number", "0"))
                merged = lines.setdefault(key, {"hits": 0, "branches_covered": 0, "branches_total": 0})
                merged["hits"] = max(merged["hits"], int(line.attrib.get("hits", "0")))

                if line.attrib.get("branch", "").lower() != "true":
                    continue

                covered, total = parse_condition(line.attrib.get("condition-coverage", ""))
                merged["branches_covered"] = max(merged["branches_covered"], covered)
                merged["branches_total"] = max(merged["branches_total"], total)

    lines_total = len(lines)
    lines_covered = sum(1 for line in lines.values() if line["hits"] > 0)
    branches_total = sum(line["branches_total"] for line in lines.values())
    branches_covered = sum(line["branches_covered"] for line in lines.values())

    # A report describing nothing is not a library with perfect coverage. Without this
    # an empty report - a filter that matched no assembly, a renamed project, a test run
    # that produced no output - divides nothing by nothing, reports 100%, and clears a
    # 95% gate. The whole point of a gate is that it fails when the thing it measures is
    # not there.
    if lines_total == 0:
        print(
            f"no lines found in the {len(reports)} report(s) under {root}: "
            "the coverage filter matched nothing",
            file=sys.stderr)
        return 1

    line_percent = percent(lines_covered, lines_total)
    branch_percent = percent(branches_covered, branches_total)
    print(f"line coverage: {line_percent:.2f}% ({lines_covered}/{lines_total})")
    print(f"branch coverage: {branch_percent:.2f}% ({branches_covered}/{branches_total})")

    if line_percent < min_line or branch_percent < min_branch:
        return 1

    return 0


def parse_condition(value: str) -> tuple[int, int]:
    start = value.find("(")
    slash = value.find("/", start + 1)
    end = value.find(")", slash + 1)
    if start < 0 or slash < 0 or end < 0:
        return (0, 0)

    return (int(value[start + 1:slash]), int(value[slash + 1:end]))


def percent(covered: int, total: int) -> float:
    if total == 0:
        return 100.0

    return covered * 100.0 / total


if __name__ == "__main__":
    raise SystemExit(main())
