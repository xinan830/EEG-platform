"""Finite-context STFT reads independent of recording length, with scientific evidence."""

from dataclasses import replace
from types import SimpleNamespace

import numpy as np
import pytest
from scipy import signal

from app.algorithms.stft.runner import StftAlgorithm
from app.scientific.contracts.spectrogram import stft_context_s
from app.scientific.quality import SpectralQualityGateError
from app.services.recording_access import RecordingAccessSession, RecordingSourceIdentity
from app.services.tnk_window_preprocessing import prepare_tnk_window
from tests.test_spectrum_service import _service
from tests.test_stft_on_demand_capability import _config, _identity, _inputs, _payload, _session


class GeneratedReader:
    """Generate only requested samples; never allocate a whole long recording."""

    def __init__(self, gap_sample=None):
        self.reads = []
        self.closed = False
        self.gap_sample = gap_sample

    def read(self, start, stop, channels=None, check_cancelled=None):
        self.reads.append((start, stop, tuple(channels)))
        times = np.arange(start, stop) / 200
        values = np.column_stack([
            (index + 1) * 10e-6 * np.sin(2 * np.pi * (10 + index) * times)
            + 3e-6 * np.sin(2 * np.pi * 1.3 * times)
            for index in channels
        ])
        if self.gap_sample is not None and start <= self.gap_sample < stop:
            values[self.gap_sample - start] = np.nan
        return values

    def source_gaps(self, start, stop):
        return ()

    def close(self):
        self.closed = True


def _generated_service(tmp_path, monkeypatch, duration_s=86400, gap_sample=None):
    service, recording = _service(tmp_path)
    recording = replace(recording, sfreq=200, duration_s=duration_s, channels=("Fz", "Pz"))
    readers = []

    def open_session(_recording, *, verify_content_hash=True):
        assert not verify_content_hash
        reader = GeneratedReader(gap_sample)
        readers.append(reader)
        return RecordingAccessSession(
            RecordingSourceIdentity(recording.id, "synthetic", "test"), reader,
            200, round(duration_s * 200), recording.channels, (), lambda: None,
            serialize_reads=False,
        )

    monkeypatch.setattr(service, "open_access_session", open_session)
    monkeypatch.setattr(service._spectral_analysis.continuous_preprocessing, "load",
                        lambda *_a, **_kw: pytest.fail("STFT must not prepare whole recording"))
    return service, recording, readers


@pytest.mark.parametrize("start_s,low,expected", [
    (0, 1, (0, 1600)), (100, 1, (19400, 21600)),
    (86395, 1, (17278400, 17280000)), (100, .5, (18800, 22200)),
    (100, 8, (19600, 21400)),
])
def test_24_hour_source_reads_only_selected_window_and_context(tmp_path, monkeypatch, start_s, low, expected):
    service, recording, readers = _generated_service(tmp_path, monkeypatch)
    payload = service.load_spectrogram(recording, start_s, 5, ["pz", "fz"], low_hz=low, high_hz=30)
    assert readers[0].reads == [(*expected, (1, 0))]
    assert readers[0].closed
    assert not service._analysis_preprocess_cache._entries
    evidence = payload["preprocessing"]
    assert evidence["context_s"] == max(2, 3 / low)
    assert evidence["read_range_samples"] == dict(zip(("start", "end"), expected))
    assert evidence["target_range_samples"] == {"start": round(start_s * 200), "end": round((start_s + 5) * 200)}
    assert payload["channels"] == ["Pz", "Fz"]
    assert payload["spectrogram_contract_version"] == "spectrogram-v4-bounded-notch"
    assert np.isfinite(payload["power_linear"]["Pz"]).all()


@pytest.mark.parametrize("gap_sample", [21000, 19500])
def test_target_and_context_gaps_are_rejected_and_session_closed(tmp_path, monkeypatch, gap_sample):
    service, recording, readers = _generated_service(tmp_path, monkeypatch, gap_sample=gap_sample)
    with pytest.raises(SpectralQualityGateError) as error:
        service.load_spectrogram(recording, 100, 5, ["Fz"], high_hz=30)
    assert error.value.quality["gate_failed"] == "bounded_source_gap"
    assert error.value.quality["evidence"]["preprocessing"]["read_range_samples"] == {"start": 19400, "end": 21600}
    assert readers[0].closed
    assert not service._analysis_preprocess_cache._entries


def test_declared_source_gap_is_rejected_even_with_finite_samples(tmp_path, monkeypatch):
    service, recording, readers = _generated_service(tmp_path, monkeypatch)
    monkeypatch.setattr(GeneratedReader, "source_gaps", lambda *_: (range(19500, 19501),))
    with pytest.raises(SpectralQualityGateError) as error:
        service.load_spectrogram(recording, 100, 5, ["Fz"], high_hz=30)
    gap = error.value.quality["evidence"]["gap"]
    assert gap["unavailable_intervals"][0]["reason"] == "source_gap"
    assert not gap["imputed"]
    assert readers[0].closed


def test_filter_failure_closes_source_without_cache(tmp_path, monkeypatch):
    import app.services.bounded_window_preprocessing as preprocessing

    service, recording, readers = _generated_service(tmp_path, monkeypatch)

    def fail(*_args, **_kwargs):
        raise ValueError("filter failure")

    monkeypatch.setattr(preprocessing, "preprocess_offline", fail)
    with pytest.raises(ValueError, match="filter failure"):
        service.load_spectrogram(recording, 100, 5, ["Fz"], high_hz=30)
    assert readers[0].closed
    assert not service._analysis_preprocess_cache._entries


@pytest.mark.parametrize("kwargs", [
    {"low_hz": float("nan")}, {"low_hz": 0}, {"high_hz": 100},
    {"notch_hz": 60, "high_hz": 110}, {"notch_hz": 49},
    {"channels": ["missing"]}, {"channels": ["Fz", "fz"]},
    {"start_s": -1}, {"start_s": 86399},
])
def test_invalid_parameters_do_not_read_samples(tmp_path, monkeypatch, kwargs):
    service, recording, readers = _generated_service(tmp_path, monkeypatch)
    request = dict(start_s=100, window_s=5, channels=["Fz"], high_hz=30)
    request.update(kwargs)
    with pytest.raises(ValueError):
        service.load_spectrogram(recording, **request)
    assert not any(reader.reads for reader in readers)
    assert all(reader.closed for reader in readers)


@pytest.mark.parametrize("stage", ["before", "after_filter"])
def test_cancellation_does_not_publish_or_cache(tmp_path, monkeypatch, stage):
    import app.services.bounded_window_preprocessing as preprocessing

    service, recording, readers = _generated_service(tmp_path, monkeypatch)
    filtered = False
    original = preprocessing.preprocess_offline

    def filter_source(*args, **kwargs):
        nonlocal filtered
        result = original(*args, **kwargs)
        filtered = True
        return result

    def check():
        if stage == "before" or filtered:
            raise RuntimeError("cancelled")

    monkeypatch.setattr(preprocessing, "preprocess_offline", filter_source)
    with pytest.raises(RuntimeError, match="cancelled"):
        service.load_spectrogram(recording, 100, 5, ["Fz"], high_hz=30,
                                 execution_context=SimpleNamespace(check_cancelled=check))
    assert not service._analysis_preprocess_cache._entries
    assert all(reader.closed for reader in readers)
    assert bool(readers) == (stage == "after_filter")


def test_three_strategies_match_real_bounded_source(tmp_path, monkeypatch):
    from app.services.algorithm_recording_context import _RecordingAlgorithmContext

    service, recording, readers = _generated_service(tmp_path, monkeypatch)
    algorithm = StftAlgorithm()
    config = _config().model_copy(update={"channel": "Fz", "start_s": 100, "end_s": 105})
    inputs = algorithm.resolve_inputs(_RecordingAlgorithmContext(service, recording), config)
    static = algorithm.execute_static(inputs, config)
    full = algorithm.execute_dynamic(inputs, config)
    plan = algorithm.plan_window(inputs, config, 21000, _session())
    window = algorithm.execute_window(inputs, config, plan, _identity(plan))
    assert static.quality == "clean"
    assert window.state == "ready"
    for key in static.arrays:
        np.testing.assert_array_equal(static.arrays[key], full.arrays[key][0])
        np.testing.assert_array_equal(static.arrays[key], window.arrays[key])
    assert [reader.reads for reader in readers] == [[(19400, 21600, (0,))]] * 3


@pytest.mark.parametrize("damage", ["old_scope", "old_version", "read_range"])
def test_mismatched_preprocessing_is_not_current_science(damage):
    payload = _payload()
    if damage == "read_range":
        payload["preprocessing"]["read_range_samples"]["end"] += 1
    else:
        payload["preprocessing"]["scope" if damage == "old_scope" else "algorithm_version"] = "old"
    algorithm = StftAlgorithm()
    inputs = _inputs(SimpleNamespace(load_spectrogram=lambda **_: payload))
    plan = algorithm.plan_window(inputs, _config(), 1000, _session())
    result = algorithm.execute_window(inputs, _config(), plan, _identity(plan))
    assert result.state == "unavailable"
    assert result.failure["code"] == "STFT_WINDOW_UNAVAILABLE"


@pytest.mark.parametrize("field,old", [
    ("preprocessing_scope", "configured_continuous_before_slice"),
    ("spectrogram_contract_version", "spectrogram-v2"),
])
def test_old_science_cannot_seed_current_window_cache(field, old):
    algorithm = StftAlgorithm()
    inputs = _inputs(SimpleNamespace(load_spectrogram=lambda **_: _payload()))
    config = _config()
    plan = algorithm.plan_window(inputs, config, 1000, _session())
    result = algorithm.execute_window(inputs, config, plan, _identity(plan))
    assert algorithm.can_reuse_offline_window(result, sampling_rate_hz=200, config=config.model_dump())
    result.provenance[field] = old
    assert not algorithm.can_reuse_offline_window(result, sampling_rate_hz=200, config=config.model_dump())


@pytest.mark.parametrize("low", [.5, 1.0])
@pytest.mark.parametrize("start_s", [0, 100, 195])
def test_bounded_reference_and_whole_record_difference(tmp_path, monkeypatch, low, start_s):
    service, recording, _ = _generated_service(tmp_path, monkeypatch, duration_s=200)
    payload = service.load_spectrogram(recording, start_s, 5, ["Fz"],
                                       low_hz=low, high_hz=30, notch_hz=50)
    raw = GeneratedReader().read(0, 40000, [0])[:, 0]
    start, end = round(start_s * 200), round((start_s + 5) * 200)
    context = int(np.ceil(stft_context_s(low) * 200))
    read_start, read_end = max(0, start - context), min(raw.size, end + context)
    numerator, denominator = signal.iirnotch(50, 30, fs=200)
    sos = signal.butter(4, [low, 30], fs=200, btype="bandpass", output="sos")

    def independent_rows(source, offset):
        filtered = signal.sosfiltfilt(sos, signal.filtfilt(numerator, denominator, source))
        target = filtered[offset:offset + 1000]
        rows = [signal.periodogram(target[index:index + 800], fs=200, window="hann",
                                   detrend="constant", scaling="density")
                for index in (0, 200)]
        frequencies = rows[0][0]
        mask = (frequencies >= low) & (frequencies <= 30)
        return np.asarray([row[1][mask] for row in rows])

    bounded = independent_rows(raw[read_start:read_end], start - read_start)
    whole = independent_rows(raw, start)
    actual = np.asarray(payload["power_linear"]["Fz"]) * 1e-12
    np.testing.assert_allclose(actual, bounded, rtol=1e-11, atol=1e-24)
    difference = bounded - whole
    relative_l2 = np.linalg.norm(difference) / np.linalg.norm(whole)
    assert np.isfinite(relative_l2)
    print(f"bounded_vs_whole low={low} start_s={start_s} "
          f"max_abs_V2_per_Hz={np.max(np.abs(difference)):.9e} relative_l2={relative_l2:.9e}")


def test_tnk_shared_operation_preserves_two_second_numerical_contract(tmp_path, monkeypatch):
    service, recording, readers = _generated_service(tmp_path, monkeypatch, duration_s=30)
    prepared = prepare_tnk_window(service, recording, start_sample=2000, end_sample=3000,
                                 channels=["Pz"], low_hz=1, high_hz=30, notch_hz=50)
    raw = GeneratedReader().read(1600, 3400, [1])
    numerator, denominator = signal.iirnotch(50, 30, fs=200)
    notched = signal.filtfilt(numerator, denominator, raw, axis=0)
    sos = signal.butter(4, [1, 30], fs=200, btype="bandpass", output="sos")
    expected = signal.sosfiltfilt(sos, notched, axis=0)[400:1400]
    np.testing.assert_allclose(prepared.data, expected, rtol=1e-12, atol=1e-24)
    assert prepared.evidence["context_s"] == 2
    assert prepared.evidence["algorithm_version"] == "tnk-window-spectral-v1"
    assert readers[0].reads == [(1600, 3400, (1,))]
    assert readers[0].closed
