from app.algorithm_runtime.offline_execution import (
    scientific_cache_key,
    scientific_configuration_json,
    scientific_configuration_sha256,
    scientific_context_snapshot,
)


def _context(**overrides):
    values = {
        "sampling_rate_hz": 200.0,
        "signal_mapping": {"channels": ["F3-REF"], "signal_source": {"signal_mode": "raw"}},
        "reference": {"mode": "original_recording_no_software_rereference"},
        "filters": {"bandpass_hz": [1.0, 30.0], "notch_hz": 50.0},
        "quality_rules": {"minimum_clean_ratio": 0.5},
        "window": {"dynamic_window_s": 5.0, "refresh_step_s": 1.0},
        "window_policy_version": "psd-dynamic-window-v1",
    }
    values.update(overrides)
    return scientific_context_snapshot(**values)


def test_scientific_configuration_identity_includes_value_context():
    config = {"algorithm_id": "psd", "channel": "F3-REF", "mode": "dynamic"}
    baseline = scientific_configuration_sha256(config, _context())
    for key, changed in (
        ("sampling_rate_hz", 250.0),
        ("signal_mapping", {"channels": ["F7-REF"]}),
        ("filters", {"bandpass_hz": [2.0, 30.0]}),
        ("quality_rules", {"minimum_clean_ratio": 0.9}),
        ("window", {"dynamic_window_s": 10.0}),
        ("window_policy_version", "psd-dynamic-window-v2"),
    ):
        assert scientific_configuration_sha256(config, _context(**{key: changed})) != baseline


def test_display_montage_label_does_not_change_scientific_context_identity():
    signal_mapping = {
        "channels": ["F3-REF"],
        "signal_source": {"signal_mode": "acquisition_montage", "montage_label": "Operator label"},
    }

    normalized = _context(signal_mapping=signal_mapping)

    assert normalized["signal_mapping"]["signal_source"] == {"signal_mode": "acquisition_montage"}


def test_scientific_configuration_json_is_the_exact_hashed_input():
    config = {"algorithm_id": "psd", "channel": "F3-REF", "mode": "dynamic"}
    serialized = scientific_configuration_json(config, _context())
    import hashlib
    assert hashlib.sha256(serialized.encode("utf-8")).hexdigest() == scientific_configuration_sha256(config, _context())
    assert '"signal_mapping"' in serialized


def test_scientific_cache_key_changes_with_normalized_scientific_config():
    arguments = {
        "source_sha256": "source",
        "definition_sha256": "definition",
        "config_sha256": "config",
        "implementation_build": "build",
        "actual_range": {"start_s": 0.0, "end_s": 20.0},
    }
    baseline = scientific_cache_key(**arguments)
    assert scientific_cache_key(**{**arguments, "config_sha256": "different-config"}) != baseline
