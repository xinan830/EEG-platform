from __future__ import annotations

import threading
import time
from concurrent.futures import ThreadPoolExecutor
from types import SimpleNamespace

import numpy as np
import pytest
from pydantic import ValidationError

from app.algorithms.builtin_modules import build_builtin_modules
from app.algorithms.tnk.group import TnkGroupConfig
from app.algorithms.tnk.runtime import build_tnk_child_adapters
from app.algorithm_runtime.executor import AlgorithmRuntime
from app.algorithm_runtime.errors import UnsupportedAlgorithmModeError
from app.algorithm_runtime.on_demand import DynamicSessionIdentity, DynamicWindowIdentity
from app.algorithm_runtime.on_demand_result_validation import validate_window_result
from app.algorithm_runtime.registry import AlgorithmRegistry
from app.algorithm_runtime.result_validation import validate_algorithm_result
from app.scientific.quality import SpectralQualityGateError


ROLE_MAPPING = {role: f"{role}-AVG" for role in ("Fz", "Pz", "Oz", "F3", "F4")}


class SyntheticRecording:
    id = "synthetic-tnk-group"
    sfreq_hz = 128.0
    duration_s = 30.0
    channel_names = list(ROLE_MAPPING.values())

    def __init__(self):
        self.spectrum_calls = []

    def load_spectrum(self, *, start_s, window_s, channels, **_):
        self.spectrum_calls.append((start_s, window_s, tuple(channels)))
        frequencies = np.arange(0.0, 32.0, 0.25)
        spectra = []
        for index, _channel in enumerate(channels):
            peak = 1.0 + index * 0.15
            psd = 1e-8 / np.maximum(frequencies, 0.25)
            psd += 1e-7 * np.exp(-0.5 * ((frequencies - peak - 8.0) / 0.8) ** 2)
            spectra.append(psd)
        return SimpleNamespace(
            freqs=frequencies,
            psd=np.asarray(spectra),
            signal_quality=1.0,
            gate_failed=False,
        )


def _group():
    return next(item for item in build_builtin_modules() if item.manifest.algorithm_id == "tnk_biofeedback")


def _config(mode="dynamic"):
    return TnkGroupConfig.model_validate({
        "channel": ROLE_MAPPING["Fz"],
        "role_mapping": ROLE_MAPPING,
        "mode": mode,
        "start_s": 0.0,
        "end_s": 12.0,
        "window_s": 4.0,
        "step_s": 1.0,
    })


def test_public_catalog_has_one_group_and_children_are_internal_only():
    public_ids = [item.manifest.algorithm_id for item in build_builtin_modules()]
    child_ids = {item.manifest.algorithm_id for item in build_tnk_child_adapters()}
    assert [item for item in public_ids if item.startswith("tnk_")] == ["tnk_biofeedback"]
    assert child_ids == {
        "tnk_iapf", "tnk_relaxation", "tnk_spatial_distribution", "tnk_rhythm_stability",
        "tnk_composite_meditation", "tnk_brainbeat", "tnk_brainbeat_flat", "tnk_fatigue",
        "tnk_hai", "tnk_faa", "tnk_rbp", "tnk_aperiodic",
    }


def test_public_group_catalog_explains_internal_child_roles():
    group = _group()
    children = group.manifest.output_schema["children"]
    by_id = {item["algorithm_id"]: item for item in children}

    assert by_id["tnk_faa"]["required_roles"] == ["F3", "F4"]
    assert by_id["tnk_rbp"]["required_roles"] == list(ROLE_MAPPING)
    assert by_id["tnk_composite_meditation"]["input_source"] == "child_results"
    assert all(item["input_source"] == "mapped_recording_channels" for item in children if item["required_roles"])
    assert group.manifest.dynamic_capabilities.on_demand.implementation == "implemented"
    assert group.on_demand_default_enabled is True


def test_group_mapping_rejects_missing_duplicate_and_unknown_roles():
    with pytest.raises(ValidationError, match="必须显式映射"):
        TnkGroupConfig.model_validate({"channel": "Fz", "mode": "static", "start_s": 0, "end_s": 1,
                                       "role_mapping": {"Fz": "Fz"}})
    duplicate = dict(ROLE_MAPPING, Pz=ROLE_MAPPING["Fz"])
    with pytest.raises(ValidationError, match="不同来源通道"):
        TnkGroupConfig.model_validate({"channel": "Fz", "mode": "static", "start_s": 0, "end_s": 1,
                                       "role_mapping": duplicate})
    unknown = dict(ROLE_MAPPING, O1="O1")
    with pytest.raises(ValidationError, match="必须显式映射"):
        TnkGroupConfig.model_validate({"channel": "Fz", "mode": "static", "start_s": 0, "end_s": 1,
                                       "role_mapping": unknown})


def test_public_group_is_dynamic_only_and_rejects_static_execution_before_loading_input():
    group = _group()
    assert group.manifest.supported_modes == ["dynamic"]
    registry = AlgorithmRegistry()
    registry.register(group)
    recording = SyntheticRecording()

    with pytest.raises(UnsupportedAlgorithmModeError) as error:
        AlgorithmRuntime(registry).execute(
            algorithm_id="tnk_biofeedback", recording=recording,
            config=_config("static").model_dump(),
        )

    assert error.value.code == "ALGORITHM_MODE_UNSUPPORTED"
    assert error.value.detail == {"algorithm_id": "tnk_biofeedback", "mode": "static"}
    assert recording.spectrum_calls == []


def test_dynamic_dispatch_preserves_each_child_per_window_and_metric_units():
    group = _group()
    config = _config("dynamic")
    inputs = group.resolve_inputs(SyntheticRecording(), config)
    result = group.execute_dynamic(inputs, config)
    validate_algorithm_result(result, group.manifest, "dynamic", group.requested_channels(config))

    assert result.windows
    assert all(window.end_s - window.start_s >= 4.0 for window in result.windows)
    assert set(result.arrays) == set(group._metric_units)
    assert all(array.shape == (len(result.windows), 1) for array in result.arrays.values())
    point = result.evidence["extensions"]["tnk_evidence"]["points"][0]
    assert set(point["extensions"]["tnk_evidence"]["children"]) == set(group._child_by_id)
    assert set(result.array_units) == set(result.arrays)


def test_iapf_uses_its_own_30_second_warmup_and_five_second_cadence():
    group = _group()
    config = _config("dynamic")

    assert group._dynamic_child_range("tnk_iapf", config, 0.0, 4.0) is None
    assert group._dynamic_child_range("tnk_iapf", config, 25.0, 29.0) is None
    assert group._dynamic_child_range("tnk_iapf", config, 26.0, 30.0) == (0.0, 30.0)
    assert group._dynamic_child_range("tnk_iapf", config, 27.0, 31.0) is None
    assert group._dynamic_child_range("tnk_iapf", config, 31.0, 35.0) == (5.0, 35.0)


def test_brainbeat_flat_reuses_latest_valid_fit_between_aperiodic_updates():
    group = _group()
    config = _config("dynamic").model_copy(update={"end_s": 40.0})
    recording = SyntheticRecording()
    recording.duration_s = 40.0
    result = group.execute_dynamic(group.resolve_inputs(recording, config), config)
    points = result.evidence["extensions"]["tnk_evidence"]["points"]
    flat_results = [point["extensions"]["tnk_evidence"]["children"]["tnk_brainbeat_flat"][0]
                    for point in points]
    computed = [row for row in flat_results if row["evidence"].get("extensions", {})
                .get("tnk_evidence", {}).get("state") in {"warmup", "ready"}]

    assert computed
    ready = [row for row in computed if row["value"] is not None]
    assert ready
    fit_provenance = ready[-1]["evidence"]["extensions"]["tnk_evidence"]["provenance"]["aperiodic_fit_by_role"]
    assert set(fit_provenance) == {"Fz", "Pz"}
    assert all(fit["fit_end_s"] <= ready[-1]["actual_range"]["end_s"] for fit in fit_provenance.values())
    assert any(fit["fit_end_s"] < ready[-1]["actual_range"]["end_s"] for fit in fit_provenance.values())


def _session(group):
    return DynamicSessionIdentity(
        recording_id="synthetic-tnk-group", source_sha256="source-sha256",
        algorithm_id=group.manifest.algorithm_id, definition_id="definition",
        definition_version="1.0.0", scientific_version=group.manifest.scientific_version,
        implementation_identity=group.manifest.implementation_identity,
        configuration_sha256="config-sha256", window_policy_version=group.window_policy_version,
        context_identity="context-sha256",
    )


def test_group_on_demand_replays_full_run_target_and_preserves_partial_children():
    group = _group()
    config = _config("dynamic")
    inputs = group.resolve_inputs(SyntheticRecording(), config)
    full = group.execute_dynamic(inputs, config)
    target_index = min(5, len(full.windows) - 1)
    target = full.windows[target_index]
    session = _session(group)
    plan = group.plan_window(inputs, config, int(round(target.end_s * inputs.sfreq_hz)), session)
    identity = DynamicWindowIdentity(
        **session.model_dump(), start_sample=plan.start_sample, end_sample=plan.end_sample,
    )

    result = group.execute_window(inputs, config, plan, identity)
    validate_window_result(result, group.manifest)

    assert result.state == "ready"
    assert result.quality == target.quality
    assert result.provenance["replayed_window_count"] == target_index + 1
    assert plan.context_start_sample == 0
    assert plan.context_end_sample == plan.end_sample
    assert plan.replay_window_count == target_index + 1
    for name, values in full.arrays.items():
        np.testing.assert_allclose(result.arrays[name], values[target_index], rtol=0, atol=0, equal_nan=True)
    full_children = target.evidence["extensions"]["tnk_evidence"]["children"]
    replay_children = result.evidence["extensions"]["tnk_evidence"]["children"]
    assert replay_children == full_children
    assert np.isnan(result.arrays["tnk_faa"]).all()
    assert replay_children["tnk_faa"][0]["failure"]["code"] == "TNK_SEGMENT_REQUIRED"


def _run_group_target(group, recording, config, session, target_index):
    inputs = group.resolve_inputs(recording, config)
    target_end = config.start_s + (config.window_s or 10.0) + target_index * (config.step_s or 1.0)
    plan = group.plan_window(inputs, config, int(round(target_end * inputs.sfreq_hz)), session)
    identity = DynamicWindowIdentity(
        **session.model_dump(), start_sample=plan.start_sample, end_sample=plan.end_sample,
    )
    return group.execute_window(inputs, config, plan, identity), plan


def test_sequential_group_targets_restore_latest_checkpoint_and_share_frame_spectra(caplog):
    caplog.set_level("INFO", logger="app.algorithms.tnk.group_window")
    group = _group()
    config = _config("dynamic")
    recording = SyntheticRecording()
    session = _session(group)
    full = group.execute_dynamic(group.resolve_inputs(recording, config), config)

    first, _ = _run_group_target(group, recording, config, session, 5)
    calls_after_first = len(recording.spectrum_calls)
    second, plan = _run_group_target(group, recording, config, session, 6)

    assert len(recording.spectrum_calls) - calls_after_first == 9
    assert plan.replay_window_count == 7  # The scientific context remains reset-origin anchored.
    log = [record.message for record in caplog.records
           if record.message.startswith("tnk_on_demand_execution")][-1]
    assert "checkpoint_frame=5" in log
    assert "replayed_frames=1" in log
    assert "spectrum_cache_hits=7" in log
    for name, values in full.arrays.items():
        np.testing.assert_allclose(second.arrays[name], values[6], rtol=0, atol=0, equal_nan=True)
    assert first.state == second.state == "ready"


def test_group_random_seek_uses_nearest_compatible_checkpoint_and_identity_miss_replays_cold(caplog):
    caplog.set_level("INFO", logger="app.algorithms.tnk.group_window")
    group = _group()
    config = _config("dynamic")
    recording = SyntheticRecording()
    session = _session(group)
    full = group.execute_dynamic(group.resolve_inputs(recording, config), config)

    _run_group_target(group, recording, config, session, 6)
    calls_before_backward_seek = len(recording.spectrum_calls)
    backward, _ = _run_group_target(group, recording, config, session, 3)
    assert len(recording.spectrum_calls) - calls_before_backward_seek == 9
    backward_log = next(record.message for record in caplog.records if "target_frame=3" in record.message)
    assert "checkpoint_frame=2" in backward_log
    assert "replayed_frames=1" in backward_log
    for name, values in full.arrays.items():
        np.testing.assert_allclose(backward.arrays[name], values[3], rtol=0, atol=0, equal_nan=True)

    other_identity = session.model_copy(update={"configuration_sha256": "different-config"})
    calls_before_cold_seek = len(recording.spectrum_calls)
    cold, _ = _run_group_target(group, recording, config, other_identity, 6)
    assert len(recording.spectrum_calls) - calls_before_cold_seek == 63
    cold_log = next(record.message for record in caplog.records if "target_frame=6" in record.message
                    and "checkpoint_frame=None" in record.message)
    assert "replayed_frames=7" in cold_log
    for name, values in full.arrays.items():
        np.testing.assert_allclose(cold.arrays[name], values[6], rtol=0, atol=0, equal_nan=True)


def test_concurrent_targets_for_one_session_serialize_checkpoint_replay():
    group = _group()
    config = _config("dynamic")
    recording = SyntheticRecording()
    full = group.execute_dynamic(group.resolve_inputs(recording, config), config)
    session = _session(group)
    original_load = recording.load_spectrum
    active_loads = 0
    maximum_active_loads = 0
    counter_lock = threading.Lock()

    def measured_load(**kwargs):
        nonlocal active_loads, maximum_active_loads
        with counter_lock:
            active_loads += 1
            maximum_active_loads = max(maximum_active_loads, active_loads)
        try:
            time.sleep(0.001)
            return original_load(**kwargs)
        finally:
            with counter_lock:
                active_loads -= 1

    recording.load_spectrum = measured_load
    with ThreadPoolExecutor(max_workers=2) as executor:
        futures = [executor.submit(_run_group_target, group, recording, config, session, index)
                   for index in (5, 6)]
        results = [future.result()[0] for future in futures]

    assert maximum_active_loads == 1
    for result, target_index in zip(results, (5, 6), strict=True):
        assert result.state == "ready"
        for name, values in full.arrays.items():
            np.testing.assert_allclose(result.arrays[name], values[target_index], rtol=0, atol=0, equal_nan=True)


def test_group_on_demand_before_first_eligible_window_returns_insufficient_data():
    group = _group()
    config = _config("dynamic")
    inputs = group.resolve_inputs(SyntheticRecording(), config)
    session = _session(group)
    plan = group.plan_window(inputs, config, 0, session)
    identity = DynamicWindowIdentity(
        **session.model_dump(), start_sample=plan.start_sample, end_sample=plan.end_sample,
    )

    result = group.execute_window(inputs, config, plan, identity)

    assert plan.state == "insufficient_data"
    assert result.state == "insufficient_data"
    assert result.failure["code"] == "TNK_WINDOW_INSUFFICIENT_DATA"
    assert not result.arrays


def test_group_on_demand_cancellation_stops_context_replay():
    group = _group()
    config = _config("dynamic")
    inputs = group.resolve_inputs(SyntheticRecording(), config)
    session = _session(group)
    plan = group.plan_window(inputs, config, 8 * int(inputs.sfreq_hz), session)
    identity = DynamicWindowIdentity(
        **session.model_dump(), start_sample=plan.start_sample, end_sample=plan.end_sample,
    )
    checks = 0

    def cancel_after_first_replay_window():
        nonlocal checks
        checks += 1
        if checks == 3:
            raise RuntimeError("cancel replay")

    with pytest.raises(RuntimeError, match="cancel replay"):
        group.execute_window(inputs, config, plan, identity, cancel_after_first_replay_window)


def test_dynamic_group_records_quality_rejected_child_and_continues_later_windows():
    class FirstSpectrumRejectedRecording(SyntheticRecording):
        def __init__(self):
            super().__init__()
            self.first_call = True

        def load_spectrum(self, *, start_s, window_s, channels, **kwargs):
            if self.first_call:
                self.first_call = False
                raise SpectralQualityGateError({
                    "clean_ratio": 0.0,
                    "clean_segments": 0,
                    "total_segments": 1,
                    "gate_failed": "low_quality",
                    "rejected_reasons": ["amplitude_threshold"],
                    "evidence": {"amplitude": {"peak_uv": 364.3, "threshold_uv": 150.0}},
                })
            return super().load_spectrum(start_s=start_s, window_s=window_s, channels=channels, **kwargs)

    group = _group()
    config = _config("dynamic")
    recording = FirstSpectrumRejectedRecording()
    result = group.execute_dynamic(group.resolve_inputs(recording, config), config)

    assert result.windows[0].state == "Partial"
    assert result.windows[0].quality == "partial"
    first_point = result.evidence["extensions"]["tnk_evidence"]["points"][0]
    rejected = first_point["extensions"]["tnk_evidence"]["children"]["tnk_iapf"][0]
    assert rejected["quality"] == "gate_failed"
    assert rejected["failure"]["code"] == "TNK_QUALITY_REJECTED"
    assert rejected["failure"]["detail"]["quality"]["evidence"]["amplitude"]["peak_uv"] == 364.3
    later_point = result.evidence["extensions"]["tnk_evidence"]["points"][-1]
    later_iapf = later_point["extensions"]["tnk_evidence"]["children"]["tnk_iapf"][0]
    assert later_iapf["quality"] != "gate_failed"
    assert np.isfinite(result.arrays["tnk_brainbeat"][1:, 0]).any()
    assert result.arrays["tnk_iapf"][0, 0] != result.arrays["tnk_iapf"][0, 0]
