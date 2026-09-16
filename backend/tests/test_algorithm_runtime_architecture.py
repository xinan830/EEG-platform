from __future__ import annotations

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def test_runtime_contracts_are_present() -> None:
    runtime = ROOT / "app" / "algorithm_runtime"
    assert (runtime / "contracts.py").exists()
    assert (runtime / "parameter_schema.py").exists()
    assert (runtime / "errors.py").exists()


def test_audit_documents_legacy_deletion_gate() -> None:
    document = ROOT.parent / "docs" / "architecture" / "algorithm-runtime-reset-audit.md"
    text = document.read_text(encoding="utf-8")
    assert "Historical Run retrieval" in text
    assert "ChannelMapping" in text
