from types import SimpleNamespace

import numpy as np
import pytest
import json
from pathlib import Path

from app.algorithm_runtime.executor import AlgorithmRuntime
from app.algorithm_runtime.registry import AlgorithmRegistry
from app.algorithms.band_ratio.runner import BandRatioAlgorithm
from app.algorithms.peak_frequency.runner import PeakFrequencyAlgorithm
from app.models.official_algorithm_run import OfficialAlgorithmRunConfig


def _recording(psd: np.ndarray, freqs: np.ndarray | None = None):
    frequencies = np.asarray(freqs if freqs is not None else [2.0, 4.0, 6.0, 8.0, 10.0, 12.0], dtype=float)
    spectrum = SimpleNamespace(
        gate_failed=None, freqs=frequencies, psd=np.asarray(psd, dtype=float),
        clean_epochs=4, total_epochs=4, signal_quality=1.0, rejected_reasons=(), evidence={"units": "V^2/Hz"},
    )

    def load_spectrum(*, start_s, window_s, channels):
        return spectrum

    return SimpleNamespace(id="r1", channel_names=["Fz"], sfreq_hz=100.0, duration_s=30.0, load_spectrum=load_spectrum)


def _runtime():
    registry = AlgorithmRegistry()
    registry.register(PeakFrequencyAlgorithm())
    registry.register(BandRatioAlgorithm())
    return AlgorithmRuntime(registry)


def test_peak_frequency_selects_lowest_frequency_on_tie_and_preserves_evidence():
    result = _runtime().execute(algorithm_id="peak_frequency", recording=_recording([[1, 2, 5, 5, 3, 1]]), config={
        "channel": "Fz", "mode": "static", "start_s": 0, "end_s": 10, "low_hz": 6, "high_hz": 10,
    })
    assert result.value == 6.0
    assert result.unit == "Hz"
    assert result.evidence["extensions"]["peak_frequency_evidence"]["tie_policy"] == "lowest_frequency_grid_point"


def test_peak_frequency_records_sample_coordinate_when_sfreq_is_available():
    recording = _recording([[1, 2, 5, 5, 3, 1]])
    recording.load_spectrum = lambda **kwargs: SimpleNamespace(
        gate_failed=None, freqs=np.array([2., 4., 6., 8., 10., 12.]), psd=np.array([[1., 2., 5., 5., 3., 1.]]),
        clean_epochs=4, total_epochs=4, signal_quality=1.0, rejected_reasons=(), evidence={"sfreq_hz": 100.0},
    )
    result = _runtime().execute(algorithm_id="peak_frequency", recording=recording, config={
        "channel": "Fz", "mode": "static", "start_s": 1.25, "end_s": 3.75, "low_hz": 6, "high_hz": 10,
    })
    assert result.evidence["extensions"]["sample_coordinate"]["sample_range"] == {"start_sample": 125, "end_sample": 375, "half_open": True}


def test_peak_frequency_dynamic_uses_shared_frames():
    result = _runtime().execute(algorithm_id="peak_frequency", recording=_recording([[1, 2, 5, 5, 3, 1]]), config={
        "channel": "Fz", "mode": "dynamic", "start_s": 0, "end_s": 6, "low_hz": 4, "high_hz": 10, "window_s": 5, "step_s": 1,
    })
    assert result.time_s == [4.0, 5.0, 6.0]
    assert result.values == [6.0, 6.0, 6.0]


def test_static_and_dynamic_endpoint_use_the_same_scientific_module_result():
    recording = _recording([[1, 2, 5, 5, 3, 1]])
    runtime = _runtime()
    static = runtime.execute(algorithm_id="peak_frequency", recording=recording, config={
        "channel": "Fz", "mode": "static", "start_s": 0, "end_s": 10, "low_hz": 4, "high_hz": 10,
    })
    dynamic = runtime.execute(algorithm_id="peak_frequency", recording=recording, config={
        "channel": "Fz", "mode": "dynamic", "start_s": 0, "end_s": 10, "low_hz": 4, "high_hz": 10, "window_s": 10, "step_s": 1,
    })
    assert dynamic.time_s[-1] == 10.0
    assert dynamic.windows[-1] == {"start_s": 0.0, "end_s": 10.0}
    assert dynamic.values[-1] == static.value
    # A review box resolves to the same static request, not a separate formula.
    boxed = runtime.execute(algorithm_id="peak_frequency", recording=recording, config={
        "channel": "Fz", "mode": "static", "start_s": 0, "end_s": 10, "low_hz": 4, "high_hz": 10,
    })
    assert boxed.value == static.value


def test_band_ratio_matches_independent_trapezoid_reference():
    frequencies = np.array([2.0, 4.0, 6.0, 8.0, 10.0, 12.0])
    psd = np.array([[1.0, 2.0, 4.0, 2.0, 1.0, 1.0]])
    result = _runtime().execute(algorithm_id="band_ratio", recording=_recording(psd, frequencies), config={
        "channel": "Fz", "mode": "static", "start_s": 0, "end_s": 10,
        "numerator_low_hz": 4, "numerator_high_hz": 8,
        "denominator_low_hz": 8, "denominator_high_hz": 12,
    })
    numerator = np.trapezoid([2.0, 4.0, 2.0], [4.0, 6.0, 8.0])
    denominator = np.trapezoid([2.0, 1.0, 1.0], [8.0, 10.0, 12.0])
    np.testing.assert_allclose(result.value, numerator / denominator, rtol=0, atol=1e-15)
    assert result.unit == "ratio"
    assert result.evidence["extensions"]["band_ratio_evidence"]["numerator_power_v2"] == numerator


def test_new_module_values_match_frozen_golden_fixture():
    fixture = json.loads((Path(__file__).parent / "fixtures" / "algorithm_runtime_baseline.json").read_text(encoding="utf-8"))
    assert fixture["peak_frequency"]["synthetic_peak_hz"] == 6.0
    assert fixture["band_ratio"]["synthetic_ratio"] == 3.0


def test_band_ratio_rejects_nonpositive_denominator():
    result = _runtime().execute(algorithm_id="band_ratio", recording=_recording([[1, 2, 3, 0, 0, 0]]), config={
        "channel": "Fz", "mode": "static", "start_s": 0, "end_s": 10,
        "numerator_low_hz": 2, "numerator_high_hz": 6,
        "denominator_low_hz": 8, "denominator_high_hz": 12,
    })
    assert result.value is None
    assert result.failure.code == "BAND_RATIO_DENOMINATOR_INVALID"


@pytest.mark.parametrize("algorithm_id,config", [
    ("peak_frequency", {"low_hz": 10, "high_hz": 4}),
    ("band_ratio", {"numerator_low_hz": 8, "numerator_high_hz": 4, "denominator_low_hz": 2, "denominator_high_hz": 6}),
])
def test_invalid_frequency_order_is_rejected_before_loading(algorithm_id, config):
    base = {"channel": "Fz", "mode": "static", "start_s": 0, "end_s": 10}
    base.update(config)
    with pytest.raises(ValueError):
        _runtime().execute(algorithm_id=algorithm_id, recording=_recording([[1, 2, 3, 4, 5, 6]]), config=base)


def test_official_run_config_preserves_module_specific_parameters():
    peak = OfficialAlgorithmRunConfig.model_validate({
        "algorithm_id": "peak_frequency", "channel": "Fz", "time": {"start_s": 0, "end_s": 10},
        "low_hz": 8, "high_hz": 13,
    })
    assert peak.runtime_config()["high_hz"] == 13.0
    ratio = OfficialAlgorithmRunConfig.model_validate({
        "algorithm_id": "band_ratio", "channel": "Fz", "time": {"start_s": 0, "end_s": 10},
        "numerator_low_hz": 4, "numerator_high_hz": 8,
        "denominator_low_hz": 13, "denominator_high_hz": 30,
    })
    assert ratio.runtime_config()["denominator_high_hz"] == 30.0
