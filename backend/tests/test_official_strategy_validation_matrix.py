"""Evidence matrix and cross-strategy equivalence for current official algorithms.

This test deliberately reports declared paths independently from the test
coverage it can prove. Shared-combined lifecycle tests do not establish
algorithm-specific combined numerical equivalence.
"""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

import numpy as np
import pytest

from app.algorithms.builtin_modules import build_builtin_modules
from app.algorithms.completeness import official_capability_report
from app.algorithm_runtime.offline_execution import scientific_configuration_sha256
from app.algorithm_runtime.on_demand import DynamicWindowIdentity


ROOT = Path(__file__).resolve().parents[2]
MATRIX_PATH = ROOT / "docs/validation/official-algorithm-strategy-matrix.json"
RTOL = 1e-10
ATOL = 1e-20


def _sha256(value: object) -> str:
    encoded = json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True).encode()
    return hashlib.sha256(encoded).hexdigest()


def _contract_snapshot() -> dict[str, dict[str, object]]:
    result = {}
    for module in sorted(build_builtin_modules(), key=lambda item: item.manifest.algorithm_id):
        manifest = module.manifest
        result[manifest.algorithm_id] = {
            "scientific_version": manifest.scientific_version,
            "implementation_identity": manifest.implementation_identity,
            "manifest_sha256": _sha256(manifest.model_dump(mode="json")),
            "parameter_schema_sha256": _sha256(module.parameter_schema().model_dump(mode="json")),
            "output_schema_sha256": _sha256(manifest.output_schema),
            "supported_modes": manifest.supported_modes,
        }
    return result


def test_frozen_contract_matrix_matches_all_current_official_modules() -> None:
    frozen = json.loads(MATRIX_PATH.read_text(encoding="utf-8"))
    assert frozen["contract_snapshot"] == _contract_snapshot()
    assert len(frozen["contract_snapshot"]) == 9


def test_every_declared_reuse_path_has_algorithm_scoped_test_evidence() -> None:
    matrix = json.loads(MATRIX_PATH.read_text(encoding="utf-8"))
    for module in build_builtin_modules():
        algorithm_id = module.manifest.algorithm_id
        declared = module.manifest.dynamic_capabilities.model_dump(mode="json")
        entry = matrix["algorithms"][algorithm_id]
        for path, status in declared.items():
            evidence = entry["paths"][path]
            if status["implementation"] == "unsupported":
                assert evidence["status"] == "unsupported"
                continue
            assert evidence["implementation"] == "implemented"
            if status["validation"] == "validated":
                assert evidence["status"] == "validated"
                assert evidence["tests"], f"{algorithm_id}/{path}"
                assert all((ROOT / test).is_file() for test in evidence["tests"])
            else:
                assert evidence["status"] == "unvalidated"


def test_full_and_on_demand_cache_identity_matches_only_for_exact_scientific_context() -> None:
    config = {"algorithm_id": "psd", "channel": "F3-REF", "mode": "dynamic", "low_hz": 1,
              "high_hz": 30, "notch_hz": None, "window_s": 5, "step_s": 1}
    context = {"sampling_rate_hz": 200, "signal_mapping": {"F3-REF": ["F3"]},
               "reference": {"kind": "acquisition_montage"}, "filters": {"low_hz": 1, "high_hz": 30},
               "quality_rules": {"contract": "psd-v1"}, "window": {"start_sample": 1000, "end_sample": 2000},
               "window_policy_version": "psd-dynamic-window-v1"}
    base = scientific_configuration_sha256(config, context)
    assert scientific_configuration_sha256(config, dict(context)) == base  # mode is not part of identity
    incompatible = [
        {**context, "sampling_rate_hz": 250},
        {**context, "signal_mapping": {"F3-REF": ["F3", "F4"]}},
        {**context, "filters": {"low_hz": 2, "high_hz": 30}},
        {**context, "window": {"start_sample": 1001, "end_sample": 2000}},
        {**context, "window_policy_version": "psd-dynamic-window-v2"},
    ]
    assert all(scientific_configuration_sha256(config, item) != base for item in incompatible)


def test_dynamic_window_identity_rejects_changed_source_config_and_sample_bounds() -> None:
    common = dict(project_scope=None, recording_id="synthetic-recording", source_sha256="source-a",
                  algorithm_id="psd", definition_id="definition-a", definition_version="1",
                  definition_sha256="digest-a", implementation_build="build-a", scientific_version="v1",
                  implementation_identity="psd-runtime-v3", configuration_sha256="config-a",
                  window_policy_version="psd-dynamic-window-v1", generation=0,
                  reset_origin_sample=0, context_identity="context-a",
                  start_sample=1000, end_sample=2000)
    baseline = DynamicWindowIdentity(**common)
    variants = [
        {**common, "source_sha256": "source-b"},
        {**common, "configuration_sha256": "config-b"},
        {**common, "start_sample": 1001},
        {**common, "end_sample": 2001},
        {**common, "implementation_identity": "psd-runtime-v4"},
        {**common, "context_identity": "context-b"},
        {**common, "reset_origin_sample": 100},
    ]
    assert all(DynamicWindowIdentity(**item) != baseline for item in variants)


def test_numeric_reference_report_has_frozen_environment_tolerances_and_checksum() -> None:
    report = json.loads((ROOT / "docs/validation/official-algorithm-numeric-baseline.json").read_text(encoding="utf-8"))
    assert report["scope"] == "engineering_validation_only_not_clinical_validation"
    assert report["tolerances"] == {"rtol": RTOL, "atol": ATOL}
    assert report["environment"]["numpy"] == "2.3.4"
    assert report["environment"]["scipy"] == "1.16.3"
    assert report["array_artifact"]["sha256"]
    assert set(report["algorithms"]) == {module.manifest.algorithm_id for module in build_builtin_modules()}


def test_strategy_baseline_freezes_all_nine_algorithm_series_and_artifact_arrays() -> None:
    report = json.loads((ROOT / "docs/validation/official-algorithm-strategy-baseline.json").read_text(encoding="utf-8"))
    artifact_path = ROOT / report["artifact"]["path"]
    assert hashlib.sha256(artifact_path.read_bytes()).hexdigest() == report["artifact"]["sha256"]
    expected_ids = {module.manifest.algorithm_id for module in build_builtin_modules()}
    assert set(report["algorithms"]) == expected_ids
    with np.load(artifact_path, allow_pickle=False) as artifact:
        for algorithm_id in expected_ids:
            series_key = f"{algorithm_id}__series_json"
            assert series_key in artifact.files
            assert report["algorithms"][algorithm_id]["window_count"] > 0
            assert any(name.startswith(f"{algorithm_id}__") for name in artifact.files)
    for algorithm_id, entry in report["algorithms"].items():
        combined = entry["combined"]
        assert combined["preview_state"] == "ready"
        assert combined["background_full_state"] == "completed"
        assert combined["independent_lifecycle_identity"] is True


def test_capability_report_validates_only_explicit_algorithm_scoped_evidence() -> None:
    report = official_capability_report(ROOT)
    for algorithm_id, item in report.items():
        combined = item["paths"]["combined_preview_background_full"]
        assert combined["status"] == "validated", algorithm_id
        assert item["release_ready"] is True, algorithm_id


def test_new_modules_do_not_inherit_combined_or_inverse_validation() -> None:
    from app.algorithms.capabilities import official_dynamic_capabilities
    from combined_strategy_support import ALGORITHM_IDS

    declared = official_dynamic_capabilities(full_to_preview=True, on_demand_to_full=True)
    assert declared.combined_preview_background_full.validation == "unvalidated"
    assert declared.on_demand_result_to_full.validation == "unvalidated"
    assert {module.manifest.algorithm_id for module in build_builtin_modules()} == set(ALGORITHM_IDS)
