"""Export non-identifying strategy evidence from a successful pytest JUnit run."""

import argparse
import hashlib
import importlib.metadata
import json
from pathlib import Path
import platform
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[2]
REFERENCES = [
    "backend/tests/combined_strategy_support.py",
    "backend/tests/test_official_combined_strategies.py",
    "backend/tests/test_official_directional_reuse.py",
    "backend/app/services/offline_window_reuse.py",
    "docs/validation/official-algorithm-strategy-matrix.json",
]


def summarize(junit_path: Path) -> dict:
    tree = ET.parse(junit_path)
    cases = tree.findall(".//testcase")
    if not cases or any(case.find(tag) is not None for case in cases for tag in ("failure", "error", "skipped")):
        raise ValueError("strategy report requires a nonempty, fully passing suite")
    comparisons = []
    for case in cases:
        properties = {item.attrib["name"]: item.attrib["value"] for item in case.findall("./properties/property")}
        if "array_comparison" in properties:
            comparisons.append({"case": case.attrib["name"],
                                "arrays": json.loads(properties["array_comparison"]),
                                "context": json.loads(properties["validation_context"])})
    if len(comparisons) != 24:
        raise ValueError("expected 18 combined and six directional comparisons")
    return {
        "schema": "official-combined-strategy-validation-v1",
        "scope": "synthetic_engineering_validation_only_not_clinical_or_large_recording_performance",
        "environment": {"python": platform.python_version(),
                        **{name: importlib.metadata.version(name) for name in ("numpy", "scipy")}},
        "tolerances": {"rtol": 1e-10, "atol": 1e-20},
        "reference_identity": "uncached-current-full-executor-strategy-parity-not-independent-formula-reference",
        "evidence_sha256": {name: hashlib.sha256((ROOT / name).read_bytes()).hexdigest() for name in REFERENCES},
        "passed_cases": [case.attrib["name"] for case in cases],
        "comparisons": comparisons,
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("junit", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    report = summarize(args.junit)
    args.output.write_text(json.dumps(report, ensure_ascii=True, indent=2, allow_nan=False) + "\n", encoding="utf-8")
