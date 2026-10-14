from types import SimpleNamespace

import numpy as np
import pytest

from app.algorithm_runtime.contracts import AlgorithmInputs
from app.algorithm_runtime.on_demand import DynamicSessionIdentity, DynamicWindowIdentity
from app.algorithms.stft.config import StftConfig
from app.algorithms.stft.runner import StftAlgorithm


def _session() -> DynamicSessionIdentity:
    return DynamicSessionIdentity(
        recording_id="recording", source_sha256="source", algorithm_id="stft",
        definition_id="definition", definition_version="1", scientific_version="spectrogram-v2",
        implementation_identity="implementation", configuration_sha256="config",
        window_policy_version="stft-dynamic-window-v1",
    )


def _config() -> StftConfig:
    return StftConfig(channel="F3", mode="dynamic", start_s=0, end_s=20,
                      window_s=5, step_s=1, low_hz=1, high_hz=30, notch_hz=None)


def _inputs(recording) -> AlgorithmInputs:
    return AlgorithmInputs(recording_id="recording", channel="F3", sfreq_hz=200,
                           duration_s=20, payload=recording)


def _identity(plan) -> DynamicWindowIdentity:
    return DynamicWindowIdentity(**_session().model_dump(),
                                 start_sample=plan.start_sample, end_sample=plan.end_sample)


def _payload(window_s=5.0, bad_windows=0):
    frequencies = np.array([1.0, 2.0, 3.0])
    count = 1 if window_s < 5 else 2
    times = 2.0 + np.arange(count)
    linear_uv = np.arange(count * frequencies.size, dtype=float).reshape(count, -1) + 1
    db = np.full_like(linear_uv, 10.0)
    return {
        "times_s": (times + 0.0).tolist(),
        "frequencies_hz": frequencies.tolist(),
        "power_linear": {"F3": linear_uv.tolist()},
        "power_db": {"F3": db.tolist()},
        "spectrogram_contract_version": "spectrogram-v2",
        "segment_s": 4.0,
        "step_s": 1.0,
        "frequency_range_hz": {"low_hz": 1.0, "high_hz": 30.0},
        "filter_frequency_range_hz": {"low_hz": 1.0, "high_hz": 30.0},
        "notch_hz": None,
        "quality": {"windows": [], "bad_windows": bad_windows, "total_windows": max(1, bad_windows), "evidence": {}},
    }


def test_stft_capability_plans_partial_then_complete_policy_windows():
    algorithm = StftAlgorithm()
    inputs = _inputs(SimpleNamespace())
    partial = algorithm.plan_window(inputs, _config(), 800, _session())
    complete = algorithm.plan_window(inputs, _config(), 1000, _session())
    assert partial.state == "planned"
    assert (partial.start_s, partial.end_s) == pytest.approx((0, 4))
    assert (partial.start_sample, partial.end_sample) == (0, 800)
    assert (complete.start_s, complete.end_s) == pytest.approx((0, 5))


def test_stft_window_returns_real_partial_matrix_and_provenance():
    recording = SimpleNamespace(load_spectrogram=lambda **kwargs: _payload(kwargs["window_s"]))
    algorithm = StftAlgorithm()
    config = _config()
    inputs = _inputs(recording)
    plan = algorithm.plan_window(inputs, config, 800, _session())
    result = algorithm.execute_window(inputs, config, plan, _identity(plan))
    assert result.state == "ready"
    assert result.output_kind == "time_frequency"
    assert result.axes["time_center_s"].shape == (1,)
    assert result.arrays["power_linear"].shape == (1, 3)
    assert result.array_units == {"power_linear": "V^2/Hz", "power_db": "dB re 1 uV^2/Hz"}
    assert result.provenance["preprocessing_scope"] == "whole_recording_before_window_slice"
    assert result.provenance["analysis_state"] == "Partial"


def test_stft_window_matches_full_dynamic_frame_for_complete_window():
    algorithm = StftAlgorithm()
    config = _config()

    def loader(**kwargs):
        return _payload(kwargs["window_s"])

    recording = SimpleNamespace(load_spectrogram=loader)
    inputs = _inputs(recording)
    full = algorithm.execute_dynamic(inputs, config)
    plan = algorithm.plan_window(inputs, config, 1000, _session())
    single = algorithm.execute_window(inputs, config, plan, _identity(plan))
    assert single.state == "ready"
    np.testing.assert_array_equal(single.axes["frequency_hz"], full.axes["frequency_hz"])
    np.testing.assert_allclose(single.axes["time_center_s"], [2.0, 3.0])
    np.testing.assert_allclose(single.arrays["power_linear"], full.arrays["power_linear"][1])
    np.testing.assert_allclose(single.arrays["power_db"], full.arrays["power_db"][1])


def test_stft_quality_rejection_has_failure_without_publishing_matrix():
    recording = SimpleNamespace(load_spectrogram=lambda **kwargs: _payload(kwargs["window_s"], bad_windows=1))
    algorithm = StftAlgorithm()
    config = _config()
    inputs = _inputs(recording)
    plan = algorithm.plan_window(inputs, config, 1000, _session())
    result = algorithm.execute_window(inputs, config, plan, _identity(plan))
    assert result.state == "rejected"
    assert result.failure["code"] == "STFT_WINDOW_QUALITY_GATE_FAILED"
    assert result.arrays == {}
    assert result.axes == {}


def test_stft_window_checks_cancellation_before_loading():
    recording = SimpleNamespace(load_spectrogram=lambda **_: pytest.fail("cancelled work must not load data"))
    algorithm = StftAlgorithm()
    config = _config()
    inputs = _inputs(recording)
    plan = algorithm.plan_window(inputs, config, 1000, _session())
    with pytest.raises(RuntimeError, match="cancelled"):
        algorithm.execute_window(inputs, config, plan, _identity(plan),
                                 check_cancelled=lambda: (_ for _ in ()).throw(RuntimeError("cancelled")))
