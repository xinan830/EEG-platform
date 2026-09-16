from app.algorithm_runtime.builtins import build_builtin_registry


def test_builtin_registry_contains_canonical_iapf_and_theta_beta_v2() -> None:
    registry = build_builtin_registry()
    assert [item.manifest.algorithm_id for item in registry.list()] == ["iapf", "theta_beta"]
    assert registry.get("theta_beta").manifest.scientific_version == "official-theta-beta-v2"
