from app.bootstrap import build_builtin_registry


def test_builtin_registry_contains_the_current_executable_official_modules() -> None:
    registry = build_builtin_registry()
    assert [item.manifest.algorithm_id for item in registry.list()] == ["band_ratio", "faa", "iapf", "peak_frequency", "psd", "rbp", "stft", "theta_beta"]
    assert registry.get("theta_beta").manifest.scientific_version == "official-theta-beta-v2"
