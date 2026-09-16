from app.algorithm_runtime.builtins import build_builtin_registry


def test_builtin_registry_contains_the_current_executable_official_modules() -> None:
    registry = build_builtin_registry()
    assert [item.manifest.algorithm_id for item in registry.list()] == ["faa", "iapf", "rbp", "theta_beta"]
    assert registry.get("theta_beta").manifest.scientific_version == "official-theta-beta-v2"
