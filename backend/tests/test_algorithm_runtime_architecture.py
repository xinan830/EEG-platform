from __future__ import annotations

from pathlib import Path
import ast
import json

import pytest


ROOT = Path(__file__).resolve().parents[1]


def test_runtime_contracts_are_present() -> None:
    runtime = ROOT / "app" / "algorithm_runtime"
    assert (runtime / "contracts.py").exists()
    assert (runtime / "parameter_schema.py").exists()
    assert (runtime / "errors.py").exists()
    assert not (ROOT / "app" / "algorithms" / "user_definition" / "runner.py").exists()


def _imported_modules_from_source(
    source: str,
    package_parts: tuple[str, ...],
    *,
    filename: str = "<memory>",
    include_type_checking: bool = True,
) -> set[str]:
    tree = ast.parse(source, filename=filename)
    modules: set[str] = set()

    def _walk(node: ast.AST) -> None:
        if not include_type_checking and isinstance(node, ast.If):
            test = node.test
            if (isinstance(test, ast.Name) and test.id == "TYPE_CHECKING") or (
                isinstance(test, ast.Attribute) and test.attr == "TYPE_CHECKING"
            ):
                for stmt in node.orelse:
                    _walk(stmt)
                return
        if isinstance(node, ast.Import):
            modules.update(alias.name for alias in node.names)
        elif isinstance(node, ast.ImportFrom):
            if node.level > 0:
                up = node.level - 1
                assert up <= len(package_parts), f"Relative import escapes root in {filename}"
                anchor = package_parts[: len(package_parts) - up]
                base_parts = anchor + tuple(node.module.split(".")) if node.module else anchor
                base = ".".join(base_parts)
            else:
                base = node.module or ""
            if base:
                modules.add(base)
                for alias in node.names:
                    if alias.name != "*":
                        candidate = f"{base}.{alias.name}"
                        candidate_path = ROOT.joinpath(*candidate.split("."))
                        if candidate_path.with_suffix(".py").exists() or (candidate_path / "__init__.py").exists():
                            modules.add(candidate)
        for child in ast.iter_child_nodes(node):
            _walk(child)

    _walk(tree)
    return modules


def _imported_modules(path: Path, *, include_type_checking: bool = True) -> set[str]:
    rel = path.relative_to(ROOT)
    package_parts = rel.parent.parts
    return _imported_modules_from_source(
        path.read_text(encoding="utf-8"),
        package_parts,
        filename=str(path),
        include_type_checking=include_type_checking,
    )


def test_runtime_does_not_import_concrete_algorithm_packages() -> None:
    runtime = ROOT / "app" / "algorithm_runtime"
    imported = set().union(*(_imported_modules(path) for path in runtime.glob("*.py")))
    assert not any(module == "app.algorithms" or module.startswith("app.algorithms.") for module in imported)


def test_builtin_registration_is_owned_by_the_composition_root() -> None:
    bootstrap = ROOT / "app" / "bootstrap" / "builtin_registry.py"
    imported = _imported_modules(bootstrap)
    assert "app.algorithm_runtime.registry" in imported
    assert "app.algorithms.builtin_modules" in imported

    module_list = ROOT / "app" / "algorithms" / "builtin_modules.py"
    module_imports = _imported_modules(module_list)
    assert "app.algorithms.faa.runner" in module_imports
    assert "app.algorithms.iapf.runner" in module_imports
    assert "app.algorithms.rbp.runner" in module_imports
    assert "app.algorithms.theta_beta.runner" in module_imports


def test_builtin_dynamic_window_maximum_is_declared_by_each_algorithm() -> None:
    from app.algorithms.builtin_modules import build_builtin_modules

    for module in build_builtin_modules():
        parameter = next(
            (item for item in module.parameter_schema().parameters if item.key == "window_s"),
            None,
        )
        if parameter is not None and parameter.maximum is not None:
            options = module.manifest.dynamic_policy.window_options_s
            assert options and parameter.maximum == max(options), module.manifest.algorithm_id


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
    for path in services.rglob("*.py"):
        text = path.read_text(encoding="utf-8")
        assert "from app.services." not in "\n".join(
            line for line in text.splitlines() if "repository" in line.lower()
        ), path


def test_services_do_not_import_eeg_core_directly() -> None:
    services = ROOT / "app" / "services"
    for path in services.rglob("*.py"):
        imported = _imported_modules(path)
        assert not any(module == "app.eeg_core" or module.startswith("app.eeg_core.") for module in imported), path


def test_services_use_scientific_package_facades_not_concrete_modules() -> None:
    services = ROOT / "app" / "services"
    forbidden = ("app.scientific.primitives.", "app.scientific.quality.")
    for path in services.rglob("*.py"):
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
    executor = (ROOT / "app" / "services" / "analysis" / "runs" / "run_analysis_executor.py").read_text(encoding="utf-8")
    assert "_execute_definition_metric" not in executor
    assert "_execute_dynamic_definition_metric" not in executor


def test_retired_metric_executor_implementations_are_removed() -> None:
    assert not (ROOT / "app" / "legacy" / "definition_metric_runner.py").exists()
    assert not (ROOT / "app" / "legacy" / "user_definition.py").exists()
    assert not (ROOT / "app" / "algorithms" / "user_definition" / "runner.py").exists()


def test_definition_validation_has_current_ownership_without_retired_execution() -> None:
    implementation = ROOT / "app" / "definitions" / "validation.py"
    assert implementation.exists()
    source = implementation.read_text(encoding="utf-8")
    assert "def execute_graph" not in source
    assert "def evaluate_formula" not in source
    assert not (ROOT / "app" / "legacy" / "definition_engine.py").exists()
    assert not (ROOT / "app" / "eeg_core" / "definition_engine.py").exists()


def test_product_runtime_does_not_import_or_name_switch_to_retired_official_paths() -> None:
    main = (ROOT / "app" / "main.py").read_text(encoding="utf-8")
    runs = (ROOT / "app" / "services" / "analysis" / "runs" / "runs.py").read_text(encoding="utf-8")
    executor = (ROOT / "app" / "services" / "analysis" / "runs" / "run_analysis_executor.py").read_text(encoding="utf-8")

    assert "official_definitions" not in main
    assert '"Official IAPF"' not in runs
    assert '"Official THETA_BETA"' not in runs
    assert "def _official_spectrum" not in executor
    assert "def _execute(self, analysis_type" not in runs


def test_run_services_have_no_opt_in_retired_user_execution_path() -> None:
    runs = (ROOT / "app" / "services" / "analysis" / "runs" / "runs.py").read_text(encoding="utf-8")
    queue = (ROOT / "app" / "services" / "analysis" / "runs" / "run_queue.py").read_text(encoding="utf-8")
    executor = (ROOT / "app" / "services" / "analysis" / "runs" / "run_analysis_executor.py").read_text(encoding="utf-8")
    for source in (runs, queue, executor):
        assert "enable_legacy_definition_execution" not in source
        assert "DefinitionMetricRunner" not in source
        assert "UserDefinitionAlgorithm" not in source


def test_retired_metric_playback_has_no_product_execution_path() -> None:
    for path in (
        ROOT / "app" / "services" / "playback.py",
        ROOT / "app" / "services" / "playback_processor.py",
        ROOT / "app" / "legacy" / "playback_processor.py",
        ROOT / "app" / "eeg_core" / "processor.py",
    ):
        assert not path.exists(), path
    main = (ROOT / "app" / "main.py").read_text(encoding="utf-8")
    routes = (ROOT / "app" / "api" / "playback.py").read_text(encoding="utf-8")
    assert "app.state.playback_service" not in main
    assert "@router.post(\"/api/recordings/{recording_id}/playback\"" not in routes
    assert "@router.post(\"/api/playback/{session_id}/control\"" not in routes
    assert "@router.websocket(\"/api/playback/{session_id}/events\"" not in routes
    assert "@router.post(\"/api/recordings/{recording_id}/waveform-playback\"" in routes


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
    for name in ("spectral.py", "faa.py", "offline_metrics.py", "definition_engine.py"):
        assert not (eeg_core / name).exists()
    for name in ("faa.py", "iapf.py", "rbp.py", "theta_beta.py"):
        assert not (eeg_core / "official_algorithms" / name).exists()


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


def test_retired_official_reference_calculators_are_validation_only() -> None:
    app_root = ROOT / "app"
    validation_root = app_root / "eeg_core" / "official_algorithms" / "validation"
    for path in app_root.rglob("*.py"):
        if validation_root in path.parents:
            continue
        imported = _imported_modules(path)
        assert not any(
            module.startswith("app.eeg_core.official_algorithms.validation")
            or module.endswith("legacy_reference")
            for module in imported
        ), path.relative_to(app_root)


def test_retired_calculators_are_not_imported_by_product_code() -> None:
    product_files = [
        ROOT / "app" / "main.py",
        ROOT / "app" / "services" / "analysis" / "runs" / "runs.py",
        ROOT / "app" / "services" / "analysis" / "runs" / "run_analysis_executor.py",
        ROOT / "app" / "api" / "algorithms.py",
    ]
    for path in product_files:
        assert "offline_analysis" not in path.read_text(encoding="utf-8"), path


def test_generic_run_services_do_not_import_algorithm_science_implementations() -> None:
    services = ROOT / "app" / "services"
    for path in services.rglob("*.py"):
        imported = _imported_modules(path)
        assert not any(
            module.startswith("app.algorithms.")
            and module.rsplit(".", 1)[-1] in {"compute", "official"}
            for module in imported
        ), path.name


def test_official_algorithm_computations_remain_in_backend_algorithm_packages() -> None:
    algorithms = ROOT / "app" / "algorithms"
    assert len(list(algorithms.glob("*/compute.py"))) >= 4
    assert len(list(algorithms.glob("*/runner.py"))) == 9
    for path in (ROOT.parent / "desktop-client" / "BrainPlatform.Desktop" / "Modules" / "Algorithms").rglob("*.cs"):
        source = path.read_text(encoding="utf-8")
        assert "MathNet.Numerics" not in source, path
        assert "FftSharp" not in source, path


def test_api_does_not_import_concrete_algorithm_modules() -> None:
    api_root = ROOT / "app" / "api"
    for path in api_root.rglob("*.py"):
        imported = _imported_modules(path)
        assert not any(
            module == "app.algorithms" or module.startswith("app.algorithms.")
            for module in imported
        ), path.relative_to(ROOT)


def test_services_do_not_import_composition_root() -> None:
    services = ROOT / "app" / "services"
    for path in services.rglob("*.py"):
        imported = _imported_modules(path)
        assert not any(
            module in {"app.bootstrap", "app.main"}
            or module.startswith("app.bootstrap.")
            or module.startswith("app.main.")
            for module in imported
        ), path.relative_to(ROOT)


def _service_package_of_path(path: Path, services_root: Path) -> str | None:
    parts = path.relative_to(services_root).parts
    if parts == ("__init__.py",):
        return None
    if parts[0] == "analysis" and len(parts) >= 2 and parts[1] in {"windows", "runs", "results"}:
        return f"analysis.{parts[1]}"
    return parts[0]


def _service_package_of_module(module: str) -> str | None:
    if not module.startswith("app.services."):
        return None
    rest = module[len("app.services.") :].split(".")
    if rest[0] == "analysis" and len(rest) >= 2 and rest[1] in {"windows", "runs", "results"}:
        return f"analysis.{rest[1]}"
    return rest[0]


def test_service_subpackages_respect_acyclic_dependency_boundaries() -> None:
    services = ROOT / "app" / "services"
    allowed_dependencies: dict[str, set[str]] = {
        "workspace": set(),
        "recordings": {"workspace"},
        "viewer": {"recordings"},
        "preprocessing": {"recordings"},
        "analysis": {"preprocessing", "recordings", "workspace"},
        "analysis.windows": {"analysis", "recordings", "workspace"},
        "analysis.runs": {"analysis", "analysis.windows", "preprocessing", "recordings", "workspace"},
        "analysis.results": {"analysis.runs", "recordings", "workspace"},
        "live_analysis": {"analysis.results", "recordings"},
    }
    graph: dict[str, set[str]] = {pkg: set() for pkg in allowed_dependencies}
    for path in services.rglob("*.py"):
        src_pkg = _service_package_of_path(path, services)
        if src_pkg is None:
            imported = _imported_modules(path)
            assert not any(m.startswith("app.services.") for m in imported), path.relative_to(ROOT)
            continue
        assert src_pkg in allowed_dependencies, path.relative_to(ROOT)
        for module in _imported_modules(path):
            dst_pkg = _service_package_of_module(module)
            if dst_pkg is None or dst_pkg == src_pkg:
                continue
            assert dst_pkg in allowed_dependencies[src_pkg], (
                f"{path.relative_to(ROOT)} ({src_pkg}) must not import {module} ({dst_pkg})"
            )
            graph[src_pkg].add(dst_pkg)

    visiting: set[str] = set()
    visited: set[str] = set()

    def _visit(node: str) -> None:
        assert node not in visiting, f"Cycle detected in service subpackages at {node}"
        if node in visited:
            return
        visiting.add(node)
        for dep in graph.get(node, ()):
            _visit(dep)
        visiting.remove(node)
        visited.add(node)

    for pkg in graph:
        _visit(pkg)


def test_services_root_contains_only_structured_subpackages() -> None:
    services = ROOT / "app" / "services"
    flat_modules = sorted(path.name for path in services.glob("*.py") if path.name != "__init__.py")
    assert flat_modules == [], f"Unexpected flat service modules: {flat_modules}"
    subpackages = sorted(
        path.name for path in services.iterdir() if path.is_dir() and (path / "__init__.py").exists()
    )
    assert subpackages == ["analysis", "live_analysis", "preprocessing", "recordings", "viewer", "workspace"]
    assert not (ROOT / "app" / "processing").exists()


def test_service_modules_have_no_runtime_import_cycles() -> None:
    services = ROOT / "app" / "services"

    def _module_name(path: Path) -> str:
        parts = list(path.relative_to(ROOT).with_suffix("").parts)
        if parts[-1] == "__init__":
            parts.pop()
        return ".".join(parts)

    modules = {_module_name(path): path for path in services.rglob("*.py")}
    graph: dict[str, set[str]] = {name: set() for name in modules}
    for name, path in modules.items():
        for imp in _imported_modules(path, include_type_checking=False):
            if imp in modules and imp != name:
                graph[name].add(imp)

    visiting: set[str] = set()
    visited: set[str] = set()

    def _dfs(node: str, trail: list[str]) -> None:
        assert node not in visiting, f"Runtime import cycle: {' -> '.join([*trail, node])}"
        if node in visited:
            return
        visiting.add(node)
        for nxt in sorted(graph[node]):
            _dfs(nxt, [*trail, node])
        visiting.remove(node)
        visited.add(node)

    for mod in sorted(graph):
        _dfs(mod, [])


@pytest.mark.parametrize("submodule", [
    "from . import TOKEN\nVALUE = TOKEN\n",
    "from app.services.workspace import TOKEN\nVALUE = TOKEN\n",
])
def test_runtime_cycle_gate_checks_package_initializers(tmp_path, monkeypatch, submodule) -> None:
    package = tmp_path / "app" / "services" / "workspace"
    package.mkdir(parents=True)
    (package / "__init__.py").write_text("from .a import VALUE\nTOKEN = 1\n", encoding="utf-8")
    (package / "a.py").write_text(submodule, encoding="utf-8")
    monkeypatch.setitem(globals(), "ROOT", tmp_path)

    with pytest.raises(AssertionError, match="Runtime import cycle"):
        test_service_modules_have_no_runtime_import_cycles()


def test_runtime_cycle_gate_allows_type_only_package_imports(tmp_path, monkeypatch) -> None:
    package = tmp_path / "app" / "services" / "workspace"
    package.mkdir(parents=True)
    (package / "__init__.py").write_text("from .a import VALUE\n", encoding="utf-8")
    (package / "a.py").write_text(
        "from typing import TYPE_CHECKING\nif TYPE_CHECKING:\n    from . import TOKEN\nVALUE = 1\n",
        encoding="utf-8",
    )
    monkeypatch.setitem(globals(), "ROOT", tmp_path)

    test_service_modules_have_no_runtime_import_cycles()


def test_import_parser_resolves_package_submodules_and_relative_imports() -> None:
    assert "app.services.recordings" in _imported_modules_from_source(
        "from app.services import recordings", ("app", "services", "workspace")
    )
    assert "app.services.analysis.results" in _imported_modules_from_source(
        "from app.services.analysis import results", ("app", "services", "analysis", "windows")
    )
    assert "app.services.recordings.recording_access" in _imported_modules_from_source(
        "from ..recordings import recording_access", ("app", "services", "workspace")
    )
