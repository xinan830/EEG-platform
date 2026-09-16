from __future__ import annotations

from pathlib import Path
import json


ROOT = Path(__file__).resolve().parents[1]


def test_runtime_contracts_are_present() -> None:
    runtime = ROOT / "app" / "algorithm_runtime"
    assert (runtime / "contracts.py").exists()
    assert (runtime / "parameter_schema.py").exists()
    assert (runtime / "errors.py").exists()
    assert (ROOT / "app" / "algorithms" / "user_definition" / "runner.py").exists()


def test_runtime_baseline_fixture_is_anonymized_and_locks_scientific_identities() -> None:
    fixture = json.loads((ROOT / "tests" / "fixtures" / "algorithm_runtime_baseline.json").read_text(encoding="utf-8"))
    assert fixture["source_data"] == "none"
    assert fixture["offline_spectral"]["algorithm_version"] == "offline-spectral-v3"
    assert fixture["offline_spectral"]["frequency_point_count"] == 117
    assert fixture["iapf"]["scientific_version"] == "official-iapf-v2"
    assert fixture["theta_beta_v2"]["scientific_version"] == "official-theta-beta-v2"


def test_definition_metrics_do_not_keep_a_second_dynamic_execution_loop() -> None:
    executor = (ROOT / "app" / "services" / "run_analysis_executor.py").read_text(encoding="utf-8")
    assert "UserDefinitionAlgorithm" in executor
    assert "_execute_dynamic_definition_metric" not in executor


def test_product_runtime_does_not_import_or_name_switch_to_retired_official_paths() -> None:
    main = (ROOT / "app" / "main.py").read_text(encoding="utf-8")
    runs = (ROOT / "app" / "services" / "runs.py").read_text(encoding="utf-8")
    executor = (ROOT / "app" / "services" / "run_analysis_executor.py").read_text(encoding="utf-8")

    assert "official_definitions" not in main
    assert '"Official IAPF"' not in runs
    assert '"Official THETA_BETA"' not in runs
    assert "def _official_spectrum" not in executor
    assert "def _execute(self, analysis_type" not in runs


def test_retired_calculators_are_not_imported_by_product_code() -> None:
    product_files = [
        ROOT / "app" / "main.py",
        ROOT / "app" / "services" / "runs.py",
        ROOT / "app" / "services" / "run_analysis_executor.py",
        ROOT / "app" / "api" / "algorithms.py",
    ]
    for path in product_files:
        assert "offline_analysis" not in path.read_text(encoding="utf-8"), path


def test_audit_documents_legacy_deletion_gate() -> None:
    document = ROOT.parent / "docs" / "architecture" / "algorithm-runtime-reset-audit.md"
    text = document.read_text(encoding="utf-8")
    assert "Historical Run retrieval" in text
    assert "ChannelMapping" in text
