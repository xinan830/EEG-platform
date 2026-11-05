import pytest

from app.algorithm_runtime.errors import UnsupportedDynamicWindowCapabilityError
from app.algorithm_runtime.on_demand_registry import DynamicWindowCapabilityRegistry
from app.bootstrap import build_builtin_registry


def test_registry_only_exposes_declared_validated_capabilities():
    registry = DynamicWindowCapabilityRegistry.from_algorithm_registry(build_builtin_registry())
    assert [(item.manifest.algorithm_id, item.manifest.scientific_version) for item in registry.list()] == [
        ("band_ratio", "official-band-ratio-v2"),
        ("brainbeat", "official-brainbeat-v5"),
        ("faa", "official-faa-v2"),
        ("iapf", "official-iapf-v5"),
        ("peak_frequency", "official-peak-frequency-v2"),
        ("psd", "offline-spectral-v6-shared-1-50-notch"),
        ("rbp", "official-rbp-v4"),
        ("stft", "spectrogram-v4-bounded-notch"),
        ("theta_beta", "official-theta-beta-v5"),
        ("tnk_biofeedback", "tnk-biofeedback-group-v3"),
    ]
    assert registry.get("psd", "offline-spectral-v6-shared-1-50-notch").window_policy_version == "psd-dynamic-window-v1"
    assert registry.get("brainbeat", "official-brainbeat-v5").window_policy_version == "brainbeat-dynamic-window-v1"


def test_unregistered_algorithm_does_not_fall_back_to_full_run():
    registry = DynamicWindowCapabilityRegistry()
    with pytest.raises(UnsupportedDynamicWindowCapabilityError) as error:
        registry.get("unknown_algorithm", "unknown-version")
    assert error.value.code == "ON_DEMAND_CAPABILITY_UNSUPPORTED"
