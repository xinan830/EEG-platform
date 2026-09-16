from __future__ import annotations

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def test_runtime_contracts_are_present() -> None:
    runtime = ROOT / "app" / "algorithm_runtime"
    assert (runtime / "contracts.py").exists()
    assert (runtime / "parameter_schema.py").exists()
    assert (runtime / "errors.py").exists()
    assert (ROOT / "app" / "algorithms" / "user_definition" / "runner.py").exists()


def test_definition_metrics_do_not_keep_a_second_dynamic_execution_loop() -> None:
    executor = (ROOT / "app" / "services" / "run_analysis_executor.py").read_text(encoding="utf-8")
    assert "UserDefinitionAlgorithm" in executor
    assert "_execute_dynamic_definition_metric" not in executor


def test_audit_documents_legacy_deletion_gate() -> None:
    document = ROOT.parent / "docs" / "architecture" / "algorithm-runtime-reset-audit.md"
    text = document.read_text(encoding="utf-8")
    assert "Historical Run retrieval" in text
    assert "ChannelMapping" in text
