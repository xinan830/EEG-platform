"""FAA windows keep continuous numerical semantics without repeated whole-recording work."""

from dataclasses import replace
from concurrent.futures import ThreadPoolExecutor
from threading import Event

import numpy as np
import pytest

from app.services.algorithm_recording_context import _RecordingAlgorithmContext
from tests.test_faa_continuous import reference_filter
from tests.test_spectrum_service import _service, _provide_data


def source(tmp_path, monkeypatch):
    service, recording = _service(tmp_path)
    sfreq = 250.0
    time = np.arange(int(42 * sfreq)) / sfreq
    values = np.column_stack([
        (12 + np.sin(time)) * 1e-6 * np.sin(2 * np.pi * 10 * time),
        18e-6 * np.sin(2 * np.pi * 10 * time),
        8e-6 * np.sin(2 * np.pi * 6 * time),
    ])
    values += np.random.default_rng(41).normal(0, 0.5e-6, values.shape)
    recording = replace(recording, sfreq=sfreq, duration_s=42,
                        channels=("F3", "F4", "Pz"), channel_units=("V",) * 3)
    _provide_data(monkeypatch, service, values, sfreq, recording.channels)
    return service, recording, values


def read(context, start=0, **changes):
    return context.load_faa_signals(start_s=start, end_s=start + 20,
        **{"f3_channel": "F3", "f4_channel": "F4", "low_hz": 1,
           "high_hz": 50, "notch_hz": None, **changes})


def count_preparation(monkeypatch):
    import app.services.mapped_preprocessing as module
    original = module.preprocess_blocks
    calls = []

    def counted(*args, **kwargs):
        calls.append(args[0].shape)
        return original(*args, **kwargs)

    monkeypatch.setattr(module, "preprocess_blocks", counted)
    return calls


def test_independent_window_contexts_filter_once_and_match_continuous_reference(tmp_path, monkeypatch):
    service, recording, values = source(tmp_path, monkeypatch)
    expected = reference_filter(values[:, :2], recording.sfreq, None)
    calls = count_preparation(monkeypatch)
    for start in [0, 1, 20]:
        context = _RecordingAlgorithmContext(service, recording)
        try:
            left, right, sfreq, evidence = read(context, start)
            actual = np.column_stack([left, right])
            np.testing.assert_allclose(actual, expected[int(start * sfreq):int((start + 20) * sfreq)],
                                       rtol=1e-11, atol=1e-18)
            assert evidence["channels"] == ["F3", "F4"]
            assert evidence["actual_range_s"] == {"start_s": start, "end_s": start + 20}
            assert evidence["preprocessing_scope"] == "whole_recording_before_window_slice"
        finally:
            context.close()
    assert calls == [(len(values), 2)]


@pytest.mark.parametrize("change", ["filter", "notch", "order", "source", "units", "montage", "build"])
def test_different_scientific_inputs_never_reuse_a_prepared_faa_pair(tmp_path, monkeypatch, change):
    service, recording, _ = source(tmp_path, monkeypatch)
    calls = count_preparation(monkeypatch)
    first = _RecordingAlgorithmContext(service, recording)
    read(first)
    first.close()
    options = {}
    if change == "filter":
        options["high_hz"] = 40
    elif change == "notch":
        options["notch_hz"] = 50
    elif change == "order":
        options.update(f3_channel="F4", f4_channel="F3")
    elif change == "source":
        recording = replace(recording, source_sha256="different-content")
    elif change == "units":
        recording = replace(recording, channel_units=("uV", "V", "V"))
    elif change == "montage":
        monkeypatch.setattr(service, "analysis_signal_provenance", lambda _: {
            "analysis_reference": "different-reference", "montage_fingerprint": "different"})
    else:
        monkeypatch.setenv("BRAIN_PLATFORM_BUILD_VERSION", "different-build")
    second = _RecordingAlgorithmContext(service, recording)
    try:
        read(second, 1, **options)
        assert len(calls) == 2
    finally:
        second.close()


def test_active_faa_window_keeps_its_mapped_samples_alive_after_lru_eviction(tmp_path, monkeypatch):
    service, recording, _ = source(tmp_path, monkeypatch)
    context = _RecordingAlgorithmContext(service, recording)
    try:
        left, right, _, _ = read(context)
        expected = np.column_stack([left, right]).copy()
        for high in [40, 30]:
            other = _RecordingAlgorithmContext(service, recording)
            try:
                read(other, high_hz=high)
            finally:
                other.close()
        np.testing.assert_array_equal(np.column_stack([left, right]), expected)
        assert context._prepared_faa_source is not None
        assert all(value is not context._prepared_faa_source
                   for value in service._analysis_preprocess_cache._entries.values())
    finally:
        context.close()


def test_concurrent_faa_contexts_share_one_cold_preparation(tmp_path, monkeypatch):
    import app.services.mapped_preprocessing as module
    service, recording, _ = source(tmp_path, monkeypatch)
    entered, release = Event(), Event()
    calls = count_preparation(monkeypatch)
    original = module.preprocess_blocks

    def controlled(*args, **kwargs):
        entered.set()
        assert release.wait(5)
        return original(*args, **kwargs)

    monkeypatch.setattr(module, "preprocess_blocks", controlled)

    def request(start):
        context = _RecordingAlgorithmContext(service, recording)
        try:
            return np.column_stack(read(context, start)[:2]).copy()
        finally:
            context.close()

    with ThreadPoolExecutor(max_workers=2) as executor:
        first = executor.submit(request, 0)
        try:
            assert entered.wait(5)
            second = executor.submit(request, 1)
        finally:
            release.set()
        assert first.result(timeout=5).shape == second.result(timeout=5).shape == (5000, 2)
    assert len(calls) == 1
