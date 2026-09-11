"""检查生产代码文件规模；遗留超限文件只能按基线保持或减少。"""

from __future__ import annotations

import json
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
BASELINE_PATH = ROOT / "docs" / "code-size-baseline.json"
LIMIT = 400
SOURCE_ROOTS = (ROOT / "backend" / "app", ROOT / "frontend" / "src")
EXCLUDED_PARTS = {"__pycache__", "node_modules", "dist"}


def source_files() -> list[Path]:
    files: list[Path] = []
    for root in SOURCE_ROOTS:
        for path in root.rglob("*"):
            if path.is_file() and path.suffix in {".py", ".ts", ".vue", ".css"}:
                if not EXCLUDED_PARTS.intersection(path.parts):
                    files.append(path)
    return files


def main() -> int:
    baseline = json.loads(BASELINE_PATH.read_text(encoding="utf-8"))
    failures: list[str] = []
    for path in source_files():
        relative = path.relative_to(ROOT).as_posix()
        lines = len(path.read_text(encoding="utf-8").splitlines())
        if lines > LIMIT:
            allowed = baseline.get(relative)
            if allowed is None:
                failures.append(f"{relative}: {lines} 行，超过 {LIMIT} 行且未登记遗留基线")
            elif lines > int(allowed):
                failures.append(f"{relative}: {lines} 行，超过遗留基线 {allowed} 行")
    if failures:
        print("代码规模检查失败：")
        print("\n".join(f"- {item}" for item in failures))
        return 1
    print(f"代码规模检查通过（硬上限 {LIMIT} 行；遗留基线只允许减少）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
