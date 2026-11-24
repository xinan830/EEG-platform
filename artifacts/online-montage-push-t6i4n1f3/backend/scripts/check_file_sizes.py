"""Check the executable, review-based source file size policy."""

from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[2]
POLICY_PATH = ROOT / "docs" / "code-size-policy.json"
SOURCE_ROOTS = (ROOT / "backend" / "app", ROOT / "frontend" / "src")
EXCLUDED_PARTS = {"__pycache__", "node_modules", "dist"}
VALID_DECISIONS = {"keep", "split", "generated", "approved_exception"}


def source_files() -> list[Path]:
    files: list[Path] = []
    for root in SOURCE_ROOTS:
        for path in root.rglob("*"):
            if path.is_file() and path.suffix in {".py", ".ts", ".vue", ".css"}:
                if not EXCLUDED_PARTS.intersection(path.parts):
                    files.append(path)
    return files


def assess_file(
    relative: str,
    lines: int,
    policy: dict[str, Any],
) -> tuple[list[str], list[str]]:
    """Return warnings and failures for one file without touching the filesystem."""
    target = int(policy["target_lines"])
    strong_review = int(policy["strong_review_lines"])
    critical = int(policy["critical_lines"])
    if lines <= target:
        return [], []

    level = "软上限"
    if lines > critical:
        level = "超大文件"
    elif lines > strong_review:
        level = "高风险"
    warnings = [f"{relative}: {lines} 行，超过 {target} 行目标线（{level}）"]

    review = policy.get("reviews", {}).get(relative)
    if not isinstance(review, dict):
        return warnings, [f"{relative}: 超过目标线但未登记职责与拆分评估"]

    decision = review.get("decision")
    reason = str(review.get("reason", "")).strip()
    try:
        max_lines = int(review.get("max_lines", 0))
    except (TypeError, ValueError):
        max_lines = 0

    failures: list[str] = []
    if decision not in VALID_DECISIONS:
        failures.append(f"{relative}: decision 必须是 {sorted(VALID_DECISIONS)} 之一")
    if not reason:
        failures.append(f"{relative}: 规模评估缺少 reason")
    if max_lines < lines:
        failures.append(f"{relative}: {lines} 行超过已评审上限 {max_lines} 行")
    if decision == "split" and not str(review.get("follow_up", "")).strip():
        failures.append(f"{relative}: split 决策必须提供可追溯的 follow_up")
    if lines > critical and decision == "keep":
        failures.append(
            f"{relative}: 超过 {critical} 行的手写文件不能使用普通 keep 决策"
        )
    return warnings, failures


def validate_policy(policy: dict[str, Any]) -> list[str]:
    required = ("target_lines", "strong_review_lines", "critical_lines", "reviews")
    missing = [key for key in required if key not in policy]
    if missing:
        return [f"规模策略缺少字段：{', '.join(missing)}"]
    try:
        target = int(policy["target_lines"])
        strong_review = int(policy["strong_review_lines"])
        critical = int(policy["critical_lines"])
    except (TypeError, ValueError):
        return ["规模策略阈值必须是整数"]
    if not 0 < target < strong_review < critical:
        return ["规模策略必须满足 0 < target_lines < strong_review_lines < critical_lines"]
    if not isinstance(policy["reviews"], dict):
        return ["规模策略 reviews 必须是对象"]
    return []


def main() -> int:
    policy = json.loads(POLICY_PATH.read_text(encoding="utf-8"))
    failures = validate_policy(policy)
    warnings: list[str] = []
    if not failures:
        for path in source_files():
            relative = path.relative_to(ROOT).as_posix()
            lines = len(path.read_text(encoding="utf-8").splitlines())
            file_warnings, file_failures = assess_file(relative, lines, policy)
            warnings.extend(file_warnings)
            failures.extend(file_failures)

    if warnings:
        print("代码规模评审提示：")
        print("\n".join(f"- {item}" for item in warnings))
    if failures:
        print("代码规模检查失败：")
        print("\n".join(f"- {item}" for item in failures))
        return 1
    print("代码规模检查通过（400 行为目标软上限；超限文件必须登记职责评估）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
