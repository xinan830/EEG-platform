from pathlib import Path

import numpy as np
import pytest
from scipy import signal

from app.algorithms.stft.config import StftConfig
from app.algorithms.stft.runner import StftAlgorithm
from app.scientific.quality import SpectralQualityGateError
from app.services.algorithm_recording_context import _RecordingAlgorithmContext
from app.services.run_queue import PersistentRunQueue
from app.models.run import RunStatus
from tests.test_official_algorithm_runs import _request, _service


def _range_service(tmp_path, sfreq_hz=200):
    service, recording_id = _service(tmp_path, sfreq_hz=sfreq_hz)
    load = service.recordings.load_data

    def with_high_frequency_signal(recording):
        raw, sfreq, names, events = load(recording)
        if sfreq > 80:
            times = np.arange(len(raw)) / sfreq
            raw = raw + (10e-6 * np.sin(2 * np.pi * 39 * times))[:, None]
        return raw, sfreq, names, events

    service.recordings.load_data = with_high_frequency_signal
    return service, recording_id


@pytest.mark.parametrize("dynamic", [False, True])
@pytest.mark.parametrize("low, high", [(8, 13), (35, 40), (38, 45), (8.1, 12.9)])
def test_official_narrow_stft_completes_and_preserves_requested_axis(tmp_path: Path, dynamic, low, high):
    service, recording_id = _range_service(tmp_path)
    queue = PersistentRunQueue(service.recordings, service.recordings.database_path, tmp_path / "artifacts")
    request = _request(recording_id, "stft", dynamic=dynamic)
    request.config.update(low_hz=low, high_hz=high, notch_hz=50)
    request.config["time"] = {"start_s": 5, "end_s": 15}
    queued = queue.enqueue(request)
    completed = queue.process_next()
    assert completed.run_id == queued.run_id
    assert completed.status is RunStatus.COMPLETED, completed.error
    artifact = queue.list_artifacts(completed.run_id)[0]
    arrays = queue.base.artifacts.read_npz(artifact)
    full = np.fft.rfftfreq(800, 1 / 200)
    expected_axis = full[(full >= low) & (full <= high)]
    axis_key = completed.result_summary["structured"]["axes"]["frequency_hz"]["array_key"]
    np.testing.assert_array_equal(arrays[axis_key], expected_axis)
    assert arrays["power_linear"].shape[-1] == len(expected_axis)
    assert np.isfinite(arrays["power_linear"]).all()
    assert queue.base.algorithm_runtime_registry.get("stft").manifest.implementation_identity == "stft-runtime-v3-explicit-filters"


def _independent_matrix(raw, sfreq, low, high, notch):
    filtered = raw.copy()
    if notch is not None:
        numerator, denominator = signal.iirnotch(notch, 30, sfreq)
        filtered = signal.filtfilt(numerator, denominator, filtered, axis=0)
    sos = signal.butter(4, [low, high], btype="bandpass", fs=sfreq, output="sos")
    filtered = signal.sosfiltfilt(sos, filtered, axis=0)
    rows = []
    for start in range(5 * int(sfreq), 11 * int(sfreq) + 1, int(sfreq)):
        frequencies, density = signal.periodogram(filtered[start:start + 4 * int(sfreq), 0],
            fs=sfreq, window="hann", detrend="constant", scaling="density")
        rows.append(density)
    return frequencies, np.asarray(rows)


@pytest.mark.parametrize("low, high, notch", [(8, 13, 50), (35, 40, None), (1, 50, 50)])
def test_crop_and_band_integrals_match_independent_full_grid(tmp_path, low, high, notch):
    service, recording_id = _range_service(tmp_path)
    recording = service.recordings.require_recording(recording_id)
    raw, sfreq, names, _ = service.recordings.load_data(recording)
    payload = service.recordings.load_spectrogram(recording, 5, 10, ["Fz"],
        low_hz=low, high_hz=high, notch_hz=notch)
    frequencies, matrix = _independent_matrix(raw, sfreq, low, high, notch)
    mask = (frequencies >= low) & (frequencies <= high)
    np.testing.assert_array_equal(payload["frequencies_hz"], frequencies[mask])
    np.testing.assert_allclose(np.asarray(payload["power_linear"]["Fz"]) * 1e-12,
        matrix[:, mask], rtol=1e-11, atol=1e-24)
    for band, edges in {"delta": (1, 4), "theta": (4, 8), "alpha": (8, 13), "beta": (13, 30)}.items():
        band_mask = (frequencies >= edges[0]) & (frequencies <= edges[1])
        expected = np.trapezoid(matrix[:, band_mask], frequencies[band_mask], axis=-1) * 1e12
        np.testing.assert_allclose(payload["band_power_timeseries"]["Fz"][band],
            expected, rtol=1e-11, atol=1e-12)
    assert payload["filter_frequency_range_hz"] == {"low_hz": low, "high_hz": high}
    assert payload["frequency_range_hz"] == {"low_hz": low, "high_hz": high}
    assert payload["times_s"] == list(range(7, 14))


def test_unsupported_auxiliary_band_is_null_without_failing_valid_request(tmp_path):
    service, recording_id = _service(tmp_path, sfreq_hz=40)
    recording = service.recordings.require_recording(recording_id)
    payload = service.recordings.load_spectrogram(recording, 5, 10, ["Fz"], low_hz=8, high_hz=13)
    assert payload["frequencies_hz"][0] == 8
    assert payload["frequencies_hz"][-1] == 13
    assert payload["band_power_timeseries"]["Fz"]["beta"] == [None] * 7
    assert np.isfinite(payload["power_linear"]["Fz"]).all()


@pytest.mark.parametrize("failure_kind", ["quality", "value"])
def test_dynamic_first_failure_does_not_change_axis_or_discard_later_clean_windows(tmp_path, failure_kind):
    service, recording_id = _service(tmp_path, sfreq_hz=200)
    recording = service.recordings.require_recording(recording_id)
    context = _RecordingAlgorithmContext(service.recordings, recording)
    load = context.load_spectrogram
    calls = 0

    def fail_first(**kwargs):
        nonlocal calls
        calls += 1
        if calls == 1:
            if failure_kind == "quality":
                raise SpectralQualityGateError({"total_segments": 1, "clean_segments": 0})
            raise ValueError("synthetic unavailable window")
        return load(**kwargs)

    context.load_spectrogram = fail_first
    algorithm = StftAlgorithm()
    config = StftConfig(channel="Fz", start_s=0, end_s=6, window_s=5, step_s=1,
        low_hz=8, high_hz=13, notch_hz=50)
    result = algorithm.execute_dynamic(algorithm.resolve_inputs(context, config), config)
    assert result.axes["frequency_hz"].tolist() == np.arange(8, 13.25, .25).tolist()
    assert result.windows[0].state == ("Rejected" if failure_kind == "quality" else "Unavailable")
    assert all(window.state == "Complete" for window in result.windows[1:])
    assert np.isnan(result.arrays["power_linear"][0]).all()
    assert np.isfinite(result.arrays["power_linear"][1:]).all()
