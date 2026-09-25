from __future__ import annotations

from pathlib import Path
import ast
import json


ROOT = Path(__file__).resolve().parents[1]


def test_runtime_contracts_are_present() -> None:
    runtime = ROOT / "app" / "algorithm_runtime"
    assert (runtime / "contracts.py").exists()
    assert (runtime / "parameter_schema.py").exists()
    assert (runtime / "errors.py").exists()
    assert not (ROOT / "app" / "algorithms" / "user_definition" / "runner.py").exists()


def _imported_modules(path: Path) -> set[str]:
    tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
    modules: set[str] = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            modules.update(alias.name for alias in node.names)
        elif isinstance(node, ast.ImportFrom) and node.module:
            modules.add(node.module)
    return modules


def test_runtime_does_not_import_concrete_algorithm_packages() -> None:
    runtime = ROOT / "app" / "algorithm_runtime"
    imported = set().union(*(_imported_modules(path) for path in runtime.glob("*.py")))
    assert not any(module == "app.algorithms" or module.startswith("app.algorithms.") for module in imported)


def test_builtin_registration_is_owned_by_the_composition_root() -> None:
    bootstrap = ROOT / "app" / "bootstrap" / "builtin_registry.py"
    imported = _imported_modules(bootstrap)
    assert "app.algorithm_runtime.registry" in imported
    assert "app.algorithms.faa.runner" in imported
    assert "app.algorithms.iapf.runner" in imported
    assert "app.algorithms.rbp.runner" in imported
    assert "app.algorithms.theta_beta.runner" in imported


def test_scientific_authority_manifest_has_one_executable_authority_per_capability() -> None:
    manifest_path = ROOT / "app" / "scientific" / "authority_manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    entries = manifest["entries"]
    assert entries

    capabilities = {entry["capability"] for entry in entries}
    for capability in capabilities:
        authorities = [entry for entry in entries if entry["capability"] == capability and entry["authority"]]
        assert len(authorities) <= 1, capability

    for entry in entries:
        if entry["authority"]:
            assert entry["executable"] is True
            assert entry["reference_only"] is False
        if entry["reference_only"]:
            assert entry["executable"] is False
            assert entry.get("delegate_to")


def test_services_do_not_own_or_import_persistence_repositories() -> None:
    services = ROOT / "app" / "services"
    for path in services.glob("*.py"):
        text = path.read_text(encoding="utf-8")
        assert "from app.services." not in "\n".join(
            line for line in text.splitlines() if "repository" in line.lower()
        ), path


def test_services_do_not_import_eeg_core_directly() -> None:
    services = ROOT / "app" / "services"
    for path in services.glob("*.py"):
        imported = _imported_modules(path)
        assert not any(module == "app.eeg_core" or module.startswith("app.eeg_core.") for module in imported), path


def test_services_use_scientific_package_facades_not_concrete_modules() -> None:
    services = ROOT / "app" / "services"
    forbidden = ("app.scientific.primitives.", "app.scientific.quality.")
    for path in services.glob("*.py"):
        imported = _imported_modules(path)
        assert not any(module.startswith(prefix) for module in imported for prefix in forbidden), path


def test_scientific_boundary_has_no_transport_persistence_or_service_imports() -> None:
    scientific = ROOT / "app" / "scientific"
    forbidden_prefixes = ("fastapi", "starlette", "sqlite3", "app.services", "app.persistence", "app.api")
    for path in scientific.rglob("*.py"):
        imported = _imported_modules(path)
        assert not any(module == prefix or module.startswith(prefix + ".") for module in imported for prefix in forbidden_prefixes), path


def test_validation_references_are_not_runtime_registered() -> None:
    manifest = json.loads((ROOT / "app" / "scientific" / "authority_manifest.json").read_text(encoding="utf-8"))
    reference_paths = {
        entry["canonical_import"]
        for entry in manifest["entries"]
        if entry.get("reference_only") is True
    }
    assert reference_paths
    bootstrap_text = (ROOT / "app" / "bootstrap" / "builtin_registry.py").read_text(encoding="utf-8")
    assert not any(path in bootstrap_text for path in reference_paths)


def test_runtime_baseline_fixture_is_anonymized_and_locks_scientific_identities() -> None:
    fixture = json.loads((ROOT / "tests" / "fixtures" / "algorithm_runtime_baseline.json").read_text(encoding="utf-8"))
    assert fixture["source_data"] == "none"
    assert fixture["offline_spectral"]["algorithm_version"] == "offline-spectral-v3"
    assert fixture["offline_spectral"]["frequency_point_count"] == 117
    assert fixture["iapf"]["scientific_version"] == "official-iapf-v2"
    assert fixture["theta_beta_v2"]["scientific_version"] == "official-theta-beta-v2"
    rules = fixture["comparison_rules"]
    assert "sample_coordinates" in rules["exact_fields"]
    assert rules["floating_point"]["default_rtol"] > 0
    assert rules["floating_point"]["default_atol"] > 0


def test_definition_metrics_do_not_keep_a_second_dynamic_execution_loop() -> None:
    executor = (ROOT / "app" / "services" / "run_analysis_executor.py").read_text(encoding="utf-8")
    assert "_execute_definition_metric" not in executor
    assert "_execute_dynamic_definition_metric" not in executor


def test_retired_metric_executor_implementations_are_removed() -> None:
    assert not (ROOT / "app" / "legacy" / "definition_metric_runner.py").exists()
    assert not (ROOT / "app" / "legacy" / "user_definition.py").exists()
    assert not (ROOT / "app" / "algorithms" / "user_definition" / "runner.py").exists()


def test_retired_definition_engine_is_owned_by_legacy_boundary() -> None:
    facade = (ROOT / "app" / "eeg_core" / "definition_engine.py").read_text(encoding="utf-8")
    implementation = ROOT / "app" / "legacy" / "definition_engine.py"
    assert implementation.exists()
    assert "app.legacy.definition_engine" in facade
    assert "def execute_graph" not in facade


def test_product_runtime_does_not_import_or_name_switch_to_retired_official_paths() -> None:
    main = (ROOT / "app" / "main.py").read_text(encoding="utf-8")
    runs = (ROOT / "app" / "services" / "runs.py").read_text(encoding="utf-8")
    executor = (ROOT / "app" / "services" / "run_analysis_executor.py").read_text(encoding="utf-8")

    assert "official_definitions" not in main
    assert '"Official IAPF"' not in runs
    assert '"Official THETA_BETA"' not in runs
    assert "def _official_spectrum" not in executor
    assert "def _execute(self, analysis_type" not in runs


def test_run_services_have_no_opt_in_retired_user_execution_path() -> None:
    runs = (ROOT / "app" / "services" / "runs.py").read_text(encoding="utf-8")
    queue = (ROOT / "app" / "services" / "run_queue.py").read_text(encoding="utf-8")
    executor = (ROOT / "app" / "services" / "run_analysis_executor.py").read_text(encoding="utf-8")
    for source in (runs, queue, executor):
        assert "enable_legacy_definition_execution" not in source
        assert "DefinitionMetricRunner" not in source
        assert "UserDefinitionAlgorithm" not in source


def test_playback_service_uses_a_single_processor_boundary() -> None:
    playback = (ROOT / "app" / "services" / "playback.py").read_text(encoding="utf-8")
    boundary = (ROOT / "app" / "services" / "playback_processor.py").read_text(encoding="utf-8")

    assert "app.legacy.playback_processor" not in playback
    assert "create_playback_processor" in playback
    assert "app.legacy.playback_processor" in boundary
    assert "class PlaybackProcessor(Protocol)" in boundary
    assert "PlaybackProcessor" in playback


def test_persistence_repositories_have_no_service_facades() -> None:
    service_dir = ROOT / "app" / "services"
    for name in ("algorithm_preset_repository.py", "definition_repository.py", "run_repository.py"):
        assert not (service_dir / name).exists()


def test_official_catalog_and_validation_have_no_redundant_eeg_core_entry_points() -> None:
    eeg_core = ROOT / "app" / "eeg_core"
    assert not (eeg_core / "official_definitions.py").exists()
    assert not (eeg_core / "official_algorithm_shadows.py").exists()
    assert not (eeg_core / "official_algorithms" / "registry.py").exists()
    assert not (eeg_core / "analysis_contract.py").exists()
    assert not (eeg_core / "quality.py").exists()


def test_official_runtime_uses_field_level_output_schemas_only() -> None:
    official_paths = [
        ROOT / "algorithm_runtime" / "contracts.py",
        ROOT / "algorithms",
        ROOT / "api" / "algorithms.py",
        ROOT / "eeg_core" / "official_algorithms" / "contracts.py",
    ]
    for path in official_paths:
        files = [path] if path.is_file() else list(path.rglob("*.py"))
        for item in files:
            assert "output_unit" not in item.read_text(encoding="utf-8"), item


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
    assert "Compatibility adapters remain intentionally" in text
    assert "machine-checked" in text
