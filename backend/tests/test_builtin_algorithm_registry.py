from app.bootstrap import build_builtin_registry
from app.algorithms.builtin_modules import build_builtin_modules
from app.algorithms.completeness import (
    find_registration_gaps,
    find_directional_reuse_gap,
    find_on_demand_to_full_gap,
    official_capability_report,
    official_registration_report,
)
from pathlib import Path


def test_builtin_registry_contains_the_current_executable_official_modules() -> None:
    registry = build_builtin_registry()
    assert [item.manifest.algorithm_id for item in registry.list()] == ["band_ratio", "brainbeat", "faa", "iapf", "peak_frequency", "psd", "rbp", "stft", "theta_beta"]
    assert registry.get("theta_beta").manifest.scientific_version == "official-theta-beta-v2"


def test_each_builtin_official_algorithm_explicitly_declares_its_current_version() -> None:
    modules = build_builtin_modules()
    by_id: dict[str, list] = {}
    for module in modules:
        by_id.setdefault(module.manifest.algorithm_id, []).append(module.manifest)

    assert len(by_id) == 9
    for algorithm_id, manifests in by_id.items():
        current = [item for item in manifests if item.is_current_version]
        assert len(current) == 1, algorithm_id


def test_builtin_resolution_uses_explicit_current_declaration_not_version_sorting() -> None:
    registry = build_builtin_registry()
    for module in build_builtin_modules():
        manifest = module.manifest
        assert manifest.is_current_version
        assert registry.get_current(manifest.algorithm_id).manifest.scientific_version == manifest.scientific_version


def test_builtin_manifests_have_complete_runtime_contracts_and_runners() -> None:
    modules = build_builtin_modules()
    identities = set()
    for module in modules:
        manifest = module.manifest
        identity = (manifest.algorithm_id, manifest.scientific_version)
        assert identity not in identities
        identities.add(identity)
        assert manifest.supported_modes
        assert set(manifest.supported_modes) <= {"static", "dynamic"}
        assert manifest.output_schema.get("fields"), manifest.algorithm_id
        assert manifest.definition_persistence is not None, manifest.algorithm_id
        assert manifest.definition_persistence.name.startswith("Official ")
        assert manifest.definition_persistence.version
        parameters = module.parameter_schema()
        assert isinstance(parameters.parameters, list)
        assert module.config_model.model_config.get("extra") == "forbid"
        assert callable(module.execute_static)
        if "dynamic" in manifest.supported_modes:
            assert callable(module.execute_dynamic)
        for field in manifest.output_schema["fields"]:
            assert field.get("name") and field.get("unit") and field.get("meaning"), manifest.algorithm_id


def test_official_registration_gate_covers_every_backend_module() -> None:
    root = Path(__file__).resolve().parents[2]
    assert official_registration_report(root) == []


def test_directional_reuse_gate_requires_wpf_implementation_to_match_manifest() -> None:
    assert find_directional_reuse_gap("psd", True, "public bool SupportsFullRunWindowPreview => true;") is None
    assert find_directional_reuse_gap("faa", False, "public bool SupportsFullRunWindowPreview => true;") is not None
    issue = find_directional_reuse_gap("psd", True, "public bool SupportsOnDemandWindows => true;")
    assert issue is not None
    assert issue.render() == "psd: WPF full→preview: missing full-result preview adapter (adapter missing)"


def test_on_demand_to_full_gate_requires_module_owned_window_validator() -> None:
    assert find_on_demand_to_full_gap("psd", True, True) is None
    assert find_on_demand_to_full_gap("faa", False, False) is None
    issue = find_on_demand_to_full_gap("faa", True, False)
    assert issue is not None
    assert issue.render() == "faa: backend on-demand→full: missing can_reuse_offline_window (validator missing)"


def test_declared_inverse_reuse_matches_every_builtin_module() -> None:
    for module in build_builtin_modules():
        declaration = module.manifest.dynamic_capabilities
        assert declaration is not None
        assert (declaration.on_demand_result_to_full.implementation == "implemented") == callable(
            getattr(module, "can_reuse_offline_window", None)
        ), module.manifest.algorithm_id


def test_registration_gate_reports_algorithm_layer_and_exact_missing_item() -> None:
    issues = find_registration_gaps(
        {"psd"}, {"psd"}, set(), {"psd"},
    )
    assert len(issues) == 1
    assert issues[0].render() == (
        "psd: WPF result handling: missing AlgorithmRunResultHandlerRegistry handler"
    )


def test_capability_report_does_not_infer_combined_validation_from_independent_modes() -> None:
    report = official_capability_report(Path(__file__).resolve().parents[2])
    for algorithm_id, item in report.items():
        assert item["paths"]["full"]["status"] == "validated", algorithm_id
        assert item["paths"]["on_demand"]["status"] == "validated", algorithm_id
        assert item["paths"]["combined_preview_background_full"]["status"] == "unvalidated", algorithm_id
        assert item["release_ready"] is False, algorithm_id
    assert report["psd"]["paths"]["full_result_to_preview"]["status"] == "validated"
    assert report["faa"]["paths"]["full_result_to_preview"]["status"] == "unsupported"
