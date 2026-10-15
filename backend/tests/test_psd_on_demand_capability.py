from types import SimpleNamespace

import numpy as np
import pytest

from app.algorithm_runtime.contracts import AlgorithmInputs
from app.algorithm_runtime.on_demand import DynamicSessionIdentity, DynamicWindowIdentity
from app.algorithms.psd.config import PsdConfig
from app.algorithms.psd.runner import PsdAlgorithm
from app.scientific.primitives.spectral import estimate_welch_psd, preprocess_offline


def _session() -> DynamicSessionIdentity:
    return DynamicSessionIdentity(
        recording_id="recording", source_sha256="source", algorithm_id="psd",
        definition_id="definition", definition_version="1", scientific_version="science",
        implementation_identity="implementation", configuration_sha256="config",
        window_policy_version="psd-dynamic-window-v1",
    )


def _config() -> PsdConfig:
    return PsdConfig(
        channel="F3", mode="dynamic", start_s=0, end_s=20,
        window_s=5, step_s=1, low_hz=1, high_hz=30, notch_hz=None,
    )


def _inputs(recording) -> AlgorithmInputs:
    return AlgorithmInputs(
        recording_id="recording", channel="F3", sfreq_hz=200,
        duration_s=20, payload=recording,
    )


def _identity(plan) -> DynamicWindowIdentity:
    return DynamicWindowIdentity(
        **_session().model_dump(), start_sample=plan.start_sample, end_sample=plan.end_sample,
    )


def test_psd_capability_plans_endpoint_aligned_window_from_cursor():
    algorithm = PsdAlgorithm()
    plan = algorithm.plan_window(_inputs(SimpleNamespace()), _config(), 0, _session())
    assert plan.state == "insufficient_data"
    assert "no policy-eligible" in (plan.reason or "")
    first = algorithm.plan_window(_inputs(SimpleNamespace()), _config(), 800, _session())
    assert first.state == "planned"
    assert (first.start_s, first.end_s) == pytest.approx((0, 4))
    assert first.start_sample == 0 and first.end_sample == 800
    later = algorithm.plan_window(_inputs(SimpleNamespace()), _config(), 1200, _session())
    assert later.start_s == pytest.approx(1.0)
    assert later.end_s == pytest.approx(6.0)


def test_psd_capability_executes_one_window_with_explicit_provenance():
    frequencies = PsdAlgorithm._frequency_axis(200, 1, 50)
    recording = SimpleNamespace(
        load_spectrum=lambda **_: SimpleNamespace(
            freqs=frequencies, psd=np.ones((1, frequencies.size)), clean_epochs=1,
            total_epochs=1, signal_quality=1.0, gate_failed=None,
            rejected_reasons=(), evidence={},
        )
    )
    algorithm = PsdAlgorithm()
    plan = algorithm.plan_window(_inputs(recording), _config(), 800, _session())
    result = algorithm.execute_window(_inputs(recording), _config(), plan, _identity(plan))
    assert result.state == "ready"
    assert result.array_units == {"psd": "V^2/Hz", "band_share": "ratio"}
    assert result.provenance["preprocessing_scope"] == "whole_recording_before_window_slice"
    assert result.arrays["psd"].shape == frequencies.shape


def test_psd_capability_preserves_quality_rejection_as_a_window_state():
    recording = SimpleNamespace(
        load_spectrum=lambda **_: SimpleNamespace(
            freqs=np.array([]), psd=np.empty((1, 0)), clean_epochs=0,
            total_epochs=1, signal_quality=0.0, gate_failed=True,
            rejected_reasons=("amplitude_threshold",), evidence={},
        )
    )
    algorithm = PsdAlgorithm()
    plan = algorithm.plan_window(_inputs(recording), _config(), 800, _session())
    result = algorithm.execute_window(_inputs(recording), _config(), plan, _identity(plan))
    assert result.state == "rejected"
    assert result.failure["code"] == "PSD_WINDOW_QUALITY_GATE_FAILED"
    assert result.axes["frequency_hz"].size == PsdAlgorithm._frequency_axis(200, 1, 50).size
    assert result.evidence["source_quality"]["clean_segments"] == 0


def test_psd_capability_checks_cancellation_before_window_compute():
    recording = SimpleNamespace(load_spectrum=lambda **_: pytest.fail("cancelled work must not load data"))
    algorithm = PsdAlgorithm()
    config = _config()
    inputs = _inputs(recording)
    plan = algorithm.plan_window(inputs, config, 800, _session())
    with pytest.raises(RuntimeError, match="cancelled"):
        algorithm.execute_window(
            inputs, config, plan, _identity(plan),
            check_cancelled=lambda: (_ for _ in ()).throw(RuntimeError("cancelled")),
        )


def test_psd_window_matches_the_existing_dynamic_runner_for_each_frame():
    frequencies = PsdAlgorithm._frequency_axis(200, 1, 50)

    def spectrum(**kwargs):
        start = float(kwargs["start_s"])
        duration = float(kwargs["window_s"])
        values = np.full((1, frequencies.size), 1.0 + start + duration / 100.0)
        return SimpleNamespace(
            freqs=frequencies, psd=values, clean_epochs=1, total_epochs=1,
            signal_quality=1.0, gate_failed=None, rejected_reasons=(), evidence={},
        )

    recording = SimpleNamespace(load_spectrum=spectrum)
    algorithm = PsdAlgorithm()
    config = _config()
    inputs = _inputs(recording)
    full = algorithm.execute_dynamic(inputs, config)
    session = _session()
    for index, frame in enumerate(full.windows):
        cursor_sample = int(round(frame.end_s * inputs.sfreq_hz))
        plan = algorithm.plan_window(inputs, config, cursor_sample, session)
        identity = _identity(plan)
        single = algorithm.execute_window(inputs, config, plan, identity)
        assert plan.start_sample == frame.start_sample
        assert plan.end_sample == frame.end_sample
        assert single.identity == identity
        assert single.state == "ready"
        np.testing.assert_array_equal(single.axes["frequency_hz"], full.axes["frequency_hz"])
        np.testing.assert_allclose(single.arrays["psd"], full.arrays["psd"][index], rtol=0, atol=0)
        np.testing.assert_allclose(single.arrays["band_share"], full.arrays["band_share"][index], rtol=0, atol=0)
        assert single.axis_units == {"frequency_hz": "Hz"}
        assert single.array_units == {"psd": "V^2/Hz", "band_share": "ratio"}
        assert single.quality == "clean"
        assert single.evidence["source_quality"] == full.windows[index].evidence["source_quality"]
        assert single.provenance["preprocessing_scope"] == "whole_recording_before_window_slice"


def test_psd_equivalence_with_continuously_preprocessed_source_fixture():
    sfreq = 200.0
    samples = int(20 * sfreq)
    time_axis = np.arange(samples) / sfreq
    raw = (1.5e-6 * np.sin(2 * np.pi * 10 * time_axis) +
           0.5e-6 * np.sin(2 * np.pi * 6 * time_axis))[:, None]
    prepared = preprocess_offline(raw, sfreq, low_hz=1, high_hz=30, notch_hz=None)
    frequencies = PsdAlgorithm._frequency_axis(sfreq, 1, 50)

    def spectrum(**kwargs):
        start = int(round(float(kwargs["start_s"]) * sfreq))
        length = int(round(float(kwargs["window_s"]) * sfreq))
        estimate = estimate_welch_psd(prepared[start:start + length], sfreq, low_hz=1, high_hz=50)
        return SimpleNamespace(
            freqs=estimate.freqs, psd=estimate.psd, clean_epochs=estimate.clean_epochs,
            total_epochs=estimate.total_epochs, signal_quality=estimate.signal_quality,
            gate_failed=estimate.gate_failed, rejected_reasons=estimate.rejected_reasons,
            evidence=estimate.evidence,
        )

    algorithm = PsdAlgorithm()
    config = _config()
    inputs = _inputs(SimpleNamespace(load_spectrum=spectrum))
    full = algorithm.execute_dynamic(inputs, config)
    session = _session()
    for index, frame in enumerate(full.windows):
        plan = algorithm.plan_window(inputs, config, int(round(frame.end_s * sfreq)), session)
        single = algorithm.execute_window(inputs, config, plan, _identity(plan))
        np.testing.assert_array_equal(single.axes["frequency_hz"], full.axes["frequency_hz"])
        np.testing.assert_allclose(single.arrays["psd"], full.arrays["psd"][index], rtol=1e-12, atol=1e-24)
        np.testing.assert_allclose(single.arrays["band_share"], full.arrays["band_share"][index], rtol=1e-12, atol=1e-12)
        assert single.provenance["preprocessing_scope"] == "whole_recording_before_window_slice"
