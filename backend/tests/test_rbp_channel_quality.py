"""Real Welch/service regressions for standalone RBP's annotated quality policy."""

import json
from types import SimpleNamespace

import numpy as np
import pytest

from app.algorithms.rbp.config import RbpConfig
from app.algorithms.rbp.runner import RbpAlgorithm
from app.scientific.primitives.annotated_spectral import estimate_annotated_channel_psd
from app.scientific.primitives.spectral import estimate_welch_psd
from app.scientific.quality import SpectralQualityGateError
from app.services.algorithm_recording_context import _RecordingAlgorithmContext
from app.services.analysis_preprocess_cache import AnalysisPreprocessCache, PreprocessedRecording
from app.services.spectral_analysis import SpectralAnalysisService
from tests.test_rbp_execution_parity import window


def signal_fixture(kind):
    time = np.arange(1000) / 200.0
    healthy = 20e-6 * np.sin(2 * np.pi * 10 * time) + np.random.default_rng(42).normal(0, 1e-6, len(time))
    other = 400e-6 * np.sin(2 * np.pi * 6 * time)
    if kind == "clipping":
        other = np.where(np.sin(2 * np.pi * 6 * time) > 0, 50e-6, -50e-6)
    elif kind == "zero":
        other = np.zeros_like(time)
    elif kind == "low_amplitude":
        other = .1e-6 * np.sin(2 * np.pi * 6 * time)
    elif kind == "gap":
        other[500] = np.nan
    return np.column_stack([other, healthy])


def context(values, monkeypatch):
    record = SimpleNamespace(id="quality-test", channels=["Fp1-REF", "Fp2-REF"], sfreq=200., duration_s=5.)
    recordings = SimpleNamespace(analysis_signal_provenance=lambda _: {"analysis_reference": "as_recorded"})
    service = SpectralAnalysisService(recordings, AnalysisPreprocessCache())
    # Supply a prepared source to exercise actual service slicing, Welch and transport.
    monkeypatch.setattr(service, "_preprocessed_recording", lambda *a, **k:
                        PreprocessedRecording(values, 200., tuple(record.channels)))
    recordings.load_spectrum = service.load_spectrum
    return _RecordingAlgorithmContext(recordings, record), record, service


@pytest.mark.parametrize("kind", ["amplitude", "clipping", "low_amplitude", "zero", "gap"])
def test_healthy_channel_is_independent_and_strict_default_is_unchanged(kind, monkeypatch):
    values = signal_fixture(kind)
    source, record, service = context(values, monkeypatch)
    algorithm = RbpAlgorithm()
    config = RbpConfig(channel="Fp1-REF", channels=record.channels, mode="static", start_s=0, end_s=5)
    result = algorithm.execute_static(algorithm.resolve_inputs(source, config), config)
    healthy_config = RbpConfig(channel="Fp2-REF", mode="static", start_s=0, end_s=5)
    healthy = algorithm.execute_static(algorithm.resolve_inputs(source, healthy_config), healthy_config)
    assert result.quality == "partial"
    assert healthy.quality == "clean"
    for band in result.arrays:
        assert result.arrays[band][1] == healthy.arrays[band][0]
    extensions = result.evidence["extensions"]
    if kind in {"zero", "gap"}:
        assert all(np.isnan(array[0]) for array in result.arrays.values())
        assert set(extensions["channel_failures"]["by_channel"]) == {"Fp1-REF"}
    else:
        assert all(np.isfinite(array[0]) for array in result.arrays.values())
        assert sum(result.arrays[band][0] for band in ("delta", "theta", "alpha", "beta", "gamma")) == pytest.approx(1.)
        for band in ("delta", "theta", "alpha", "beta", "gamma"):
            assert result.array_units[f"{band}_absolute_power"] == "uV^2"
            assert result.arrays[f"{band}_absolute_power"][0] == pytest.approx(
                result.evidence["calculation_trace"]["band_powers_uv2"]["Fp1-REF"][band])
        assert set(extensions["channel_warnings"]["by_channel"]) == {"Fp1-REF"}
        assert not extensions["channel_failures"]["by_channel"]
    with pytest.raises(SpectralQualityGateError):
        service.load_spectrum(record, 0, 5, record.channels, filter_high_hz=50, output_high_hz=50)
    assert estimate_welch_psd(values, 200, high_hz=50).gate_failed


def test_artifact_values_and_warnings_match_static_full_and_on_demand(monkeypatch):
    source, record, _ = context(signal_fixture("amplitude"), monkeypatch)
    algorithm = RbpAlgorithm()
    config = RbpConfig(channel="Fp1-REF", channels=record.channels, mode="dynamic", start_s=0,
                       end_s=5, window_s=5, step_s=1)
    inputs = algorithm.resolve_inputs(source, config)
    static = algorithm.execute_static(inputs, config)
    full = algorithm.execute_dynamic(inputs, config)
    target = window(algorithm, inputs, config)
    assert static.quality == full.quality == target.quality == "partial"
    for band in static.arrays:
        np.testing.assert_array_equal(static.arrays[band], full.arrays[band][-1])
        np.testing.assert_array_equal(static.arrays[band], target.arrays[band])
    assert static.evidence == full.windows[-1].evidence == target.evidence
    assert target.state == "ready"
    snapshot = algorithm.execution_snapshot(config)
    assert snapshot.quality_rules["policy"] == "per_channel_annotation"
    assert "minimum_clean_ratio" not in snapshot.quality_rules


def test_missing_channel_psd_transport_is_json_safe(monkeypatch):
    _, record, service = context(signal_fixture("gap"), monkeypatch)
    payload = service.load_spectrum(record, 0, 5, record.channels,
        filter_high_hz=50, output_high_hz=50, quality_policy="per_channel_annotation")
    json.dumps(payload, allow_nan=False)
    assert all(value is None for value in payload["psd"]["Fp1-REF"])
    assert payload["quality"]["by_channel"]["Fp2-REF"]["status"] == "clean"


def test_all_hard_invalid_channels_remain_unavailable(monkeypatch):
    values = np.zeros((1000, 2))
    values[:, 1] = np.nan
    source, record, _ = context(values, monkeypatch)
    algorithm = RbpAlgorithm()
    config = RbpConfig(channel="Fp1-REF", channels=record.channels, mode="static", start_s=0, end_s=5)
    result = algorithm.execute_static(algorithm.resolve_inputs(source, config), config)
    assert result.quality == "unavailable"
    assert all(np.isnan(array).all() for array in result.arrays.values())
    assert result.failure.code == "RBP_ALL_CHANNELS_UNAVAILABLE"


def test_annotation_requires_complete_segment():
    with pytest.raises(ValueError, match="complete Welch"):
        estimate_annotated_channel_psd(np.ones((10, 2)), 200., low_hz=1, high_hz=50)
