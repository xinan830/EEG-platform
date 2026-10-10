"""Synthetic checks for the real-recording audit; no real EEG test fixtures."""

import json
import struct
import sys
from pathlib import Path

import numpy as np
import pytest
from scipy import signal

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "backend"))

from real_recording_audit import Comparisons, _bands, _check_windows
from real_recording_reference import faa, iapf, quality, read_acquisition_sources, shares, welch


def _tone(duration=12, sfreq=100):
    time = np.arange(duration * sfreq) / sfreq
    return 10e-6 * np.sin(2 * np.pi * 10 * time)


def test_independent_welch_discards_bad_epoch_instead_of_averaging_it():
    sfreq = 100
    values = _tone()[:, None]
    values[0] = 300e-6
    freqs, actual, evidence = welch(values, sfreq)
    expected = np.mean([signal.periodogram(values[start:start + 400], fs=sfreq, window="hann",
                                          scaling="density", axis=0)[1].T
                        for start in (200, 400, 600, 800)], axis=0)
    full_freqs = np.fft.rfftfreq(400, 1 / sfreq)
    expected = np.maximum(expected[:, (full_freqs >= 1) & (full_freqs <= 30)], 1e-20)
    np.testing.assert_allclose(actual, expected, atol=0, rtol=0)
    assert len(freqs) == 117
    assert evidence["clean_segments"] == 4 and evidence["total_segments"] == 5
    assert evidence["rejected_reasons"] == ["amplitude_threshold"]


def test_insufficient_clean_epochs_produce_unavailable_not_zero():
    values = _tone(duration=8)[:, None]
    values[0] = 300e-6
    _, density, evidence = welch(values, 100)
    assert density is None and evidence["clean_ratio"] == 2 / 3


@pytest.mark.parametrize("values,reason", [
    (np.zeros((400, 1)), "flatline"),
    (np.full((400, 1), np.nan), "non_finite"),
    (np.r_[np.linspace(-10e-6, 10e-6, 300), np.full(100, 10e-6)][:, None], "clipping"),
])
def test_quality_reasons_are_explicit(values, reason):
    assert reason in quality(values, 400)["reasons"]


def test_paired_faa_matches_known_amplitude_ratio():
    left = _tone(duration=20)
    value, evidence = faa(np.column_stack((left, 2 * left)), 100)
    assert value == pytest.approx(np.log(4), rel=1e-12)
    assert evidence["clean_segments"] == 19


def test_iapf_and_five_band_shares_match_analytic_input():
    freqs = np.arange(1, 50.25, 0.25)
    density = (1e-12 / freqs + 8e-12 * np.exp(-0.5 * ((freqs - 10) / 0.5) ** 2))[None, :]
    assert iapf(freqs, density) == 10.0
    actual = shares(freqs, np.ones_like(density))
    for name, width in {"delta": 3, "theta": 4, "alpha": 5, "beta": 17, "gamma": 20}.items():
        assert actual[name] == pytest.approx(width / 49)
    assert sum(actual.values()) == pytest.approx(1)


def test_source_reader_handles_unlabeled_counter_and_independent_bipolar_projection(tmp_path):
    montage = {"DerivedChannels": [
        {"Name": "F3-F7", "PositiveLabel": "F3", "NegativeLabels": ["F7"], "DisplayOrder": 0},
        {"Name": "F7-REF", "PositiveLabel": "F7", "NegativeLabels": [], "DisplayOrder": 1},
    ]}
    manifest = {"EegSignalUnit": "V", "SamplingRateHz": 100, "Channels": [
        {"Label": "F3", "StreamIndex": 0}, {"Label": "F7", "StreamIndex": 1},
        {"Label": None, "StreamIndex": 2},
    ], "HardwareConfiguration": {"montage_configuration_snapshot_json": json.dumps(montage)}}
    (tmp_path / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    matrix = np.array([[1e-6, 3e-6, 7], [2e-6, 5e-6, 8]], dtype="<f8")
    (tmp_path / "samples-000001.bin").write_bytes(struct.pack("<qiiq", 7, 2, 3, 0) + matrix.tobytes())
    data, sfreq, names = read_acquisition_sources(tmp_path)
    np.testing.assert_allclose(data, np.column_stack((matrix[:, 0] - matrix[:, 1], matrix[:, 1])), rtol=0, atol=0)
    assert sfreq == 100 and names == ["F3-F7", "F7-REF"]


def test_comparison_rejects_fake_zero_for_unavailable():
    with pytest.raises(AssertionError, match="unavailable mask"):
        Comparisons().check("value", [0], [np.nan], unit="ratio")


def test_band_audit_uses_five_outputs_not_empty_scalar():
    values = {name: 0.2 for name in ("delta", "theta", "alpha", "beta", "gamma")}
    checks = Comparisons()
    _bands(values, values, checks)
    assert checks.outputs["band_values"]["finite_points"] == 5


def test_dynamic_schedule_preserves_absolute_endpoints_and_warmup():
    points = [{"window_start_s": max(0, end - 5), "window_end_s": end,
               "time_s": end, "warmup": end < 5} for end in range(4, 8)]
    config = {"time": {"start_s": 0, "end_s": 7.982}, "dynamic_window_s": 5, "refresh_step_s": 1}
    _check_windows(points, config, 100, 4, True, False)
    points[0]["time_s"] = 2
    with pytest.raises(AssertionError):
        _check_windows(points, config, 100, 4, True, False)
