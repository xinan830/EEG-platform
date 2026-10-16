from types import SimpleNamespace

import pytest

from app.algorithm_runtime.on_demand_registry import DynamicWindowCapabilityRegistry
from app.bootstrap import build_builtin_registry
from app.core.config import on_demand_algorithm_enabled


ACCEPTED = [
    "band_ratio", "brainbeat", "faa", "iapf", "peak_frequency",
    "psd", "rbp", "stft", "theta_beta",
]


def test_accepted_modules_are_available_without_startup_variables(monkeypatch):
    registry = build_builtin_registry()
    for algorithm_id in ACCEPTED:
        monkeypatch.delenv(f"BRAIN_ENABLE_ON_DEMAND_{algorithm_id.upper()}", raising=False)
    enabled = {
        module.manifest.algorithm_id
        for module in registry.list()
        if on_demand_algorithm_enabled(
            module.manifest.algorithm_id,
            default_enabled=getattr(module, "on_demand_default_enabled", False),
        )
    }
    assert enabled == set(ACCEPTED)
    declared = DynamicWindowCapabilityRegistry.from_algorithm_registry(registry)
    assert {item.manifest.algorithm_id for item in declared.list()} == enabled


@pytest.mark.parametrize("algorithm_id", ACCEPTED)
@pytest.mark.parametrize("override,expected", [("0", False), ("1", True), ("", True)])
def test_module_default_obeys_maintenance_override(monkeypatch, algorithm_id, override, expected):
    module = build_builtin_registry().get(algorithm_id)
    monkeypatch.setenv(f"BRAIN_ENABLE_ON_DEMAND_{algorithm_id.upper()}", override)
    assert on_demand_algorithm_enabled(
        algorithm_id, default_enabled=module.on_demand_default_enabled,
    ) is expected


def test_unapproved_module_remains_disabled_without_an_override(monkeypatch):
    monkeypatch.delenv("BRAIN_ENABLE_ON_DEMAND_FUTURE", raising=False)
    assert not on_demand_algorithm_enabled("future")
    monkeypatch.setenv("BRAIN_ENABLE_ON_DEMAND_FUTURE", "1")
    assert on_demand_algorithm_enabled("future")


def test_rollout_declaration_cannot_enable_a_module_without_window_capability():
    module = SimpleNamespace(
        manifest=build_builtin_registry().get("psd").manifest,
        on_demand_default_enabled=True,
    )
    registry = SimpleNamespace(list=lambda: [module])
    assert DynamicWindowCapabilityRegistry.from_algorithm_registry(registry).list() == []
