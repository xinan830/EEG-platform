"""Release checks for official algorithm integration registrations."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from app.algorithms.builtin_modules import build_builtin_modules


@dataclass(frozen=True)
class CompletenessIssue:
    algorithm_id: str
    layer: str
    missing_item: str

    def render(self) -> str:
        return f"{self.algorithm_id}: {self.layer}: missing {self.missing_item}"


def find_registration_gaps(
    backend_ids: set[str],
    ui_ids: set[str],
    result_handler_ids: set[str],
    detail_module_ids: set[str],
) -> list[CompletenessIssue]:
    """Return deterministic, actionable missing cross-layer registrations."""
    issues: list[CompletenessIssue] = []
    for algorithm_id in sorted(backend_ids):
        for layer, ids, item in (
            ("WPF UI descriptor", ui_ids, "AlgorithmUiRegistry descriptor"),
            ("WPF result handling", result_handler_ids, "AlgorithmRunResultHandlerRegistry handler"),
            ("WPF detail module", detail_module_ids, "detail view module registration"),
        ):
            if algorithm_id not in ids:
                issues.append(CompletenessIssue(algorithm_id, layer, item))
    return issues


def find_directional_reuse_gap(algorithm_id: str, declared_implemented: bool, handler_source: str | None) -> CompletenessIssue | None:
    """Require a WPF full-result projection implementation when the backend declares it."""
    import re

    handler_implemented = bool(handler_source and re.search(
        r"SupportsFullRunWindowPreview\s*=>\s*true|FullRunMetricWindowSpec\s*=>", handler_source
    ))
    if declared_implemented != handler_implemented:
        expected = "full-result preview adapter" if declared_implemented else "no full-result preview adapter"
        observed = "adapter registered" if handler_implemented else "adapter missing"
        return CompletenessIssue(algorithm_id, "WPF full→preview", f"{expected} ({observed})")
    return None


def find_on_demand_to_full_gap(algorithm_id: str, declared_implemented: bool, has_validator: bool) -> CompletenessIssue | None:
    """Keep directional reuse claims aligned with the module-owned validator."""
    if declared_implemented != has_validator:
        expected = "can_reuse_offline_window" if declared_implemented else "unsupported declaration"
        observed = "validator registered" if has_validator else "validator missing"
        return CompletenessIssue(algorithm_id, "backend on-demand→full", f"{expected} ({observed})")
    return None


def official_registration_report(repository_root: Path) -> list[CompletenessIssue]:
    """Check built-in IDs against the process-local WPF registration source."""
    backend_ids = {module.manifest.algorithm_id for module in build_builtin_modules()}
    ui_file = repository_root / "desktop-client/BrainPlatform.Desktop/Modules/Algorithms/ViewModels/Shared/AlgorithmUiRegistry.cs"
    handlers_file = repository_root / "desktop-client/BrainPlatform.Desktop/Modules/Algorithms/ViewModels/Shared/AlgorithmRunResultHandlerRegistry.cs"
    ui_source = ui_file.read_text(encoding="utf-8")
    handler_source = handlers_file.read_text(encoding="utf-8")
    # IDs occur as dictionary keys in the UI registry; result handler class
    # names map deterministically to the backend's snake_case IDs.
    import re

    ui_ids = set(re.findall(r'\["([a-z][a-z0-9_]*)"\]\s*=\s*new\(', ui_source))
    handler_types = re.findall(r'new\s+([A-Za-z0-9]+)RunResultHandler\(\)', handler_source)
    def to_algorithm_id(name: str) -> str:
        return re.sub(r"(?<!^)(?=[A-Z])", "_", name).lower()
    # Known initialisms use the API algorithm IDs rather than acronym splits.
    aliases = {"psd": "psd", "stft": "stft", "rbp": "rbp", "faa": "faa", "iapf": "iapf"}
    handler_ids = {aliases.get(to_algorithm_id(name), to_algorithm_id(name)) for name in handler_types}
    grouped_handler_ids = re.findall(
        r'new\s+TnkShadowRunResultHandler\("([a-z][a-z0-9_]*)"\)', handler_source,
    )
    handler_ids.update(grouped_handler_ids)
    detail_ids = set(re.findall(r'\["([a-z][a-z0-9_]*)"\]\s*=\s*new\(', ui_source))
    issues = find_registration_gaps(backend_ids, ui_ids, handler_ids, detail_ids)
    handler_sources = {
        to_algorithm_id(name): list((repository_root / "desktop-client/BrainPlatform.Desktop/Modules/Algorithms/ViewModels").rglob(f"{name}RunResultHandler.cs"))
        for name in handler_types
    }
    grouped_handler_path = (
        repository_root / "desktop-client/BrainPlatform.Desktop/Modules/Algorithms/ViewModels/Shared/TnkShadowRunResultHandler.cs"
    )
    for algorithm_id in grouped_handler_ids:
        handler_sources[algorithm_id] = [grouped_handler_path]
    for module in build_builtin_modules():
        declaration = module.manifest.dynamic_capabilities
        if declaration is None:
            issues.append(CompletenessIssue(module.manifest.algorithm_id, "backend capability", "manifest.dynamic_capabilities"))
            continue
        sources = handler_sources.get(module.manifest.algorithm_id, [])
        source_texts = [path.read_text(encoding="utf-8") for path in sources]
        if declaration.on_demand.implementation == "implemented" and not any(
            re.search(r"SupportsOnDemandWindows\s*=>[\s\S]{0,160}enabledByDefault:\s*true", source)
            for source in source_texts
        ):
            issues.append(CompletenessIssue(module.manifest.algorithm_id, "WPF on-demand", "SupportsOnDemandWindows opt-in"))
        reuse_gap = find_directional_reuse_gap(
            module.manifest.algorithm_id,
            declaration.full_result_to_preview.implementation == "implemented",
            "\n".join(source_texts) if source_texts else None,
        )
        if reuse_gap is not None:
            issues.append(reuse_gap)
        inverse_gap = find_on_demand_to_full_gap(
            module.manifest.algorithm_id,
            declaration.on_demand_result_to_full.implementation == "implemented",
            callable(getattr(module, "can_reuse_offline_window", None)),
        )
        if inverse_gap is not None:
            issues.append(inverse_gap)
    return issues


def official_capability_report(repository_root: Path | None = None) -> dict[str, dict[str, object]]:
    """Expose implementation and validation independently for every path."""
    report: dict[str, dict[str, object]] = {}
    for module in build_builtin_modules():
        manifest = module.manifest
        declaration = manifest.dynamic_capabilities
        if declaration is None:
            report[manifest.algorithm_id] = {
                "release_ready": False,
                "missing": ["manifest.dynamic_capabilities"],
            }
            continue
        paths = declaration.model_dump(mode="json")
        if repository_root is not None:
            for value in paths.values():
                if value["validation"] == "validated":
                    missing = [ref for ref in value["evidence"] if not (repository_root / ref).is_file()]
                    if missing:
                        value["validation"] = "unvalidated"
                        value["missing_evidence"] = missing
        statuses = {
            name: {
                **value,
                "status": (
                    "unsupported" if value["implementation"] == "unsupported"
                    else value["validation"]
                ),
            }
            for name, value in paths.items()
        }
        release_ready = all(
            item["implementation"] == "unsupported"
            or item["validation"] == "validated"
            for item in paths.values()
        )
        report[manifest.algorithm_id] = {
            "release_ready": release_ready,
            "paths": statuses,
        }
    return report
