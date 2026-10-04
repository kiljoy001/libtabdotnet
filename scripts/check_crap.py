#!/usr/bin/env python3
import pathlib
import sys
import xml.etree.ElementTree as ET


def main() -> int:
    if len(sys.argv) != 3:
        print("usage: check_crap.py <coverage-dir> <max-crap>", file=sys.stderr)
        return 2

    root = pathlib.Path(sys.argv[1])
    max_crap = float(sys.argv[2])
    reports = sorted(root.glob("**/coverage.cobertura.xml"))
    if not reports:
        print(f"no Cobertura reports found under {root}", file=sys.stderr)
        return 1

    methods: dict[tuple[str, str, str], dict[str, object]] = {}
    for report in reports:
        tree = ET.parse(report)
        for class_node in tree.findall(".//class"):
            filename = class_node.attrib.get("filename", "")
            for method in class_node.findall("./methods/method"):
                key = (filename, method.attrib.get("name", "(anonymous)"), method.attrib.get("signature", ""))
                merged = methods.setdefault(key, {"complexity": 1.0, "lines": {}})
                merged["complexity"] = max(float(merged["complexity"]), float(method.attrib.get("complexity", "1")))

                line_hits = merged["lines"]
                if not isinstance(line_hits, dict):
                    raise TypeError("line hit map is not a dictionary")

                for line in method.findall(".//line"):
                    number = line.attrib.get("number", "0")
                    line_hits[number] = max(int(line_hits.get(number, 0)), int(line.attrib.get("hits", "0")))

    # No methods is not "every method passes". An empty report clears this gate exactly
    # as loudly as a well-tested library does, which makes the gate useless in the one
    # case it most needs to speak up: when the measurement itself did not happen.
    if not methods:
        print(
            f"no methods found in the {len(reports)} report(s) under {root}: "
            "the coverage filter matched nothing",
            file=sys.stderr)
        return 1

    failures: list[str] = []
    for (filename, name, signature), method in methods.items():
        line_hits = method["lines"]
        if not isinstance(line_hits, dict):
            raise TypeError("line hit map is not a dictionary")

        total = len(line_hits)
        covered = sum(1 for hits in line_hits.values() if int(hits) > 0)
        line_rate = 1.0 if total == 0 else covered / total
        complexity = float(method["complexity"])
        crap = (complexity * complexity * ((1.0 - line_rate) ** 3.0)) + complexity
        if crap > max_crap:
            failures.append(f"{filename}: {name}{signature}: CRAP {crap:.2f}, complexity {complexity:.2f}, line-rate {line_rate:.2f}")

    if failures:
        print("CRAP score failures:")
        for failure in failures:
            print(failure)
        return 1

    print(f"CRAP score: all methods <= {max_crap:g}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
