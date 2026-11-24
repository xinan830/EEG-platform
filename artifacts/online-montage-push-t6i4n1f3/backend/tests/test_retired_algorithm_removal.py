"""Retired scientific entry points must not re-enter product or validation tools."""

import ast
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
RETIRED = (
    "app.legacy",
    "app.eeg_core.realtime_spectral",
    "app.eeg_core.official_algorithms",
    "app.algorithms.theta_beta.official",
)


def test_retired_modules_have_no_source_or_consumers():
    for module in RETIRED:
        path = ROOT.joinpath(*module.split("."))
        assert not path.with_suffix(".py").exists(), module
        assert not list(path.rglob("*.py")), module
    for area in ("app", "scripts", "tools"):
        for path in (ROOT / area).rglob("*.py"):
            tree = ast.parse(path.read_text(encoding="utf-8"))
            for node in ast.walk(tree):
                modules = [node.module] if isinstance(node, ast.ImportFrom) else (
                    [alias.name for alias in node.names] if isinstance(node, ast.Import) else [])
                for module in modules:
                    assert not any(module and (module == old or module.startswith(old + "."))
                                   for old in RETIRED), path
            text = path.read_text(encoding="utf-8")
            assert "LEGACY_FAA_INITIAL_DISCARD_S" not in text, path
            assert "LIVE_ANALYSIS_CONTRACT" not in text, path
