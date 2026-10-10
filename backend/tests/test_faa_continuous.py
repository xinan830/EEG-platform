from types import SimpleNamespace

import numpy as np
import pytest
from scipy import signal

from app.algorithms.faa.runner import FaaAlgorithm
from app.algorithms.faa.config import FaaConfig
from app.services.algorithm_recording_context import _RecordingAlgorithmContext
from app.services.recording_samples import ArraySampleReader


def independent_faa(pair, sfreq):
    """Independent Hann/rFFT density and inclusive trapezoid Alpha integration."""
    size = int(2 * sfreq)
    epochs = []
    for start in range(0, len(pair) - size + 1, int(sfreq)):
        epoch = pair[start:start + size] - pair[start:start + size].mean(axis=0)
        if np.isfinite(epoch).all() and np.max(np.abs(epoch)) <= 150e-6:
            epochs.append(epoch)
    if len(epochs) < 10:
        return None, len(epochs)
    window = signal.windows.hann(size, sym=False)
    fft = np.fft.rfft(np.stack(epochs) * window[None, :, None], axis=1)
    density = (np.abs(fft) ** 2 / (sfreq * np.sum(window ** 2))).mean(axis=0)
    density[1:-1] *= 2
    freq = np.fft.rfftfreq(size, 1 / sfreq)
    mask = (freq >= 8) & (freq <= 13)
    powers = np.trapezoid(density[mask], freq[mask], axis=0)
    return float(np.log(powers[1]) - np.log(powers[0])), len(epochs)


def reference_filter(pair, sfreq, notch):
    if notch is not None:
        b, a = signal.iirnotch(notch, 30, fs=sfreq)
        pair = signal.filtfilt(b, a, pair, axis=0)
    sos = signal.butter(4, [1, 50], btype="bandpass", fs=sfreq, output="sos")
    return signal.sosfiltfilt(sos, pair, axis=0)


def source_context(pair, sfreq):
    class Source:
        def open_sample_reader(self, _recording):
            return ArraySampleReader(pair, sfreq, ["F3", "F4"])
        def analysis_signal_provenance(self, _recording):
            return {"signal_mode": "raw", "analysis_reference": "original_recording_no_software_rereference"}
    recording = SimpleNamespace(id="synthetic", channels=["F3", "F4"],
                                sfreq=sfreq, duration_s=len(pair) / sfreq)
    return _RecordingAlgorithmContext(Source(), recording)


@pytest.mark.parametrize("notch", [None, 50, 60])
def test_faa_v2_static_dynamic_and_independent_whole_recording_agree(notch):
    sfreq = 250
    time = np.arange(60 * sfreq) / sfreq
    pair = np.column_stack([12e-6 * np.sin(2 * np.pi * 10 * time),
                            (18 + 3 * np.sin(time)) * 1e-6 * np.sin(2 * np.pi * 10 * time)])
    pair += np.random.default_rng(41).normal(0, 0.5e-6, pair.shape)
    expected = reference_filter(pair, sfreq, notch)
    context = source_context(pair, sfreq)
    module = FaaAlgorithm()
    config = FaaConfig(channel="F3", f4_channel="F4", mode="dynamic", start_s=0, end_s=60,
                       window_s=20, step_s=1, low_hz=1, high_hz=50, notch_hz=notch)
    try:
        inputs = module.resolve_inputs(context, config)
        dynamic = module.execute_dynamic(inputs, config)
        for index in (0, 1, 20, 40):
            bounds = dynamic.windows[index]
            static = module.execute_static(inputs, config.model_copy(update={"mode": "static", **bounds}))
            left, right = int(bounds["start_s"] * sfreq), int(bounds["end_s"] * sfreq)
            value, clean = independent_faa(expected[left:right], sfreq)
            assert dynamic.values[index] == pytest.approx(value, rel=1e-11, abs=1e-12)
            assert static.value == dynamic.values[index]
            assert static.evidence["source_quality"]["clean_segments"] == clean
            assert static.evidence["extensions"]["faa_evidence"]["preprocessing_scope"] == "whole_recording_before_window_slice"
    finally:
        context.close()


def test_faa_gap_is_unavailable_not_zero():
    pair = np.random.default_rng(1).normal(0, 2e-6, (7500, 2))
    pair[2500:2504, 0] = np.nan
    context = source_context(pair, 250)
    module = FaaAlgorithm()
    config = FaaConfig(channel="F3", f4_channel="F4", mode="static", start_s=0, end_s=30,
                       low_hz=1, high_hz=50, notch_hz=None)
    try:
        result = module.execute_static(module.resolve_inputs(context, config), config)
        assert result.value is None
        assert result.quality == "gate_failed"
        assert result.failure is not None
    finally:
        context.close()
