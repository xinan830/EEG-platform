from pathlib import Path

import numpy as np
import pytest

from app.models.analysis_config import AnalysisConfigRequest
from app.eeg_core.spectral import estimate_spectrogram, estimate_spectrogram_with_quality
from app.services.recordings import RecordingService


def _service(tmp_path: Path) -> tuple[RecordingService, object]:
    service = RecordingService(tmp_path / "recordings", tmp_path / "catalog.sqlite3")
    recording = service.create_recording("sample.bdf", ".bdf", b"raw")
    return service, recording


def test_spectrum_preserves_requested_channel_order_and_units(tmp_path, monkeypatch):
    service, recording = _service(tmp_path)
    sfreq = 100.0
    times = np.arange(30 * int(sfreq)) / sfreq
    data = np.column_stack([
        10e-6 * np.sin(2 * np.pi * 10 * times),
        20e-6 * np.sin(2 * np.pi * 10 * times),
        30e-6 * np.sin(2 * np.pi * 10 * times),
    ])
    monkeypatch.setattr(service, "load_data", lambda _recording: (data, sfreq, ["Fz", "Pz", "Oz"], []))

    payload = service.load_spectrum(recording, channels=["Oz", "Fz"])

    assert payload["channels"] == ["Oz", "Fz"]
    assert payload["units"] == {"psd": "uV^2/Hz", "absolute_power": "uV^2", "relative_power": "ratio"}
    assert payload["algorithm_version"] == "offline-spectral-v3"
    np.testing.assert_allclose(
        sum(payload["relative_band_power"]["Oz"].values()), 1.0, rtol=1e-5
    )


def test_spectrum_exposes_backend_declared_welch_step(tmp_path, monkeypatch):
    service, recording = _service(tmp_path)
    sfreq = 100.0
    times = np.arange(10 * int(sfreq)) / sfreq
    data = (10e-6 * np.sin(2 * np.pi * 10 * times))[:, None]
    monkeypatch.setattr(service, "load_data", lambda _recording: (data, sfreq, ["F3"], []))

    payload = service.load_spectrum(recording, 0.0, 10.0, ["F3"])

    assert payload["welch_contract"]["welch_segment_s"] == 4.0
    assert payload["welch_contract"]["welch_segment_overlap"] == 0.5
    assert payload["welch_contract"]["welch_step_s"] == 2.0


def test_spectrum_reuses_continuous_preprocessed_recording(tmp_path, monkeypatch):
    service, recording = _service(tmp_path)
    sfreq = 100.0
    times = np.arange(30 * int(sfreq)) / sfreq
    data = np.column_stack([
        10e-6 * np.sin(2 * np.pi * 10 * times),
        8e-6 * np.sin(2 * np.pi * 6 * times),
    ])
    monkeypatch.setattr(service, "load_data", lambda _recording: (data, sfreq, ["Fz", "Pz"], []))
    calls = {"count": 0}

    from app.services import spectral_analysis
    original = spectral_analysis.preprocess_offline

    def counted(values, rate):
        calls["count"] += 1
        return original(values, rate)

    monkeypatch.setattr(spectral_analysis, "preprocess_offline", counted)
    service.load_spectrum(recording, channels=["Fz"], window_s=10)
    service.load_spectrum(recording, channels=["Pz"], window_s=10)

    assert calls["count"] == 1


def test_configured_v4_defaults_equal_frozen_v3(tmp_path, monkeypatch):
    service, recording = _service(tmp_path)
    sfreq = 100.0
    times = np.arange(40 * int(sfreq)) / sfreq
    data = np.column_stack([10e-6 * np.sin(2 * np.pi * 10 * times), 8e-6 * np.sin(2 * np.pi * 6 * times)])
    monkeypatch.setattr(service, "load_data", lambda _recording: (data, sfreq, ["F3", "Fz"], []))
    frozen = service.load_spectrum(recording, 0, 30, ["F3", "Fz"])
    configured = service.load_configured_spectrum(recording, AnalysisConfigRequest(
        mode="static", channels=["F3", "Fz"], time={"start_s": 0, "end_s": 30}
    ))
    assert configured["algorithm_version"] == "offline-spectral-v4-configurable"
    assert configured["baseline_algorithm_version"] == "offline-spectral-v3"
    assert len(configured["analysis_config_hash"]) == 12
    np.testing.assert_allclose(configured["psd"]["F3"], frozen["psd"]["F3"])
    assert configured["band_power"] == frozen["band_power"]
    assert configured["relative_band_power"] == frozen["relative_band_power"]
    # Golden values for the deterministic 10 Hz / 6 Hz synthetic fixture.
    assert configured["band_power"]["F3"]["alpha"] == pytest.approx(49.99998294, rel=1e-7)
    assert configured["band_power"]["Fz"]["theta"] == pytest.approx(32.00000006, rel=1e-7)
    assert configured["relative_band_power"]["F3"]["alpha"] == pytest.approx(0.9999992453, rel=1e-7)


def test_configured_static_range_rejects_file_overflow(tmp_path, monkeypatch):
    service, recording = _service(tmp_path)
    times = np.arange(1000) / 100.0
    values = (10e-6 * np.sin(2 * np.pi * 10 * times))[:, None]
    monkeypatch.setattr(service, "load_data", lambda _recording: (values, 100.0, ["F3"], []))
    config = AnalysisConfigRequest(mode="static", channels=["F3"], time={"start_s": 5, "end_s": 15})
    import pytest
    with pytest.raises(ValueError, match="超出文件范围"):
        service.load_configured_spectrum(recording, config)


def test_configured_static_range_accepts_one_sample_rounding_error(tmp_path, monkeypatch):
    service, recording = _service(tmp_path)
    times = np.arange(7634) / 1000.0
    values = (10e-6 * np.sin(2 * np.pi * 10 * times))[:, None]
    monkeypatch.setattr(service, "load_data", lambda _recording: (values, 1000.0, ["F3"], []))
    config = AnalysisConfigRequest(mode="static", channels=["F3"], time={"start_s": 1.022, "end_s": 7.635})
    payload = service.load_configured_spectrum(recording, config)
    assert payload["actual_end_s"] == 7.634


def test_configured_spectrogram_accepts_one_sample_rounding_error(tmp_path, monkeypatch):
    service, recording = _service(tmp_path)
    config = AnalysisConfigRequest(mode="spectrogram", channels=["F3"], time={"start_s": 1.022, "end_s": 7.635})
    monkeypatch.setattr(service, "load_spectrogram", lambda *_args, **_kwargs: {
        "recording_id": recording.id, "window_start_s": 1.022, "window_duration_s": 6.612,
        "sfreq_hz": 500.0, "channels": ["F3"], "times_s": [1.022], "frequencies_hz": [1.0],
        "power": {"F3": [[1.0]]}, "units": "uV^2/Hz", "algorithm_version": "spectrogram-v1",
        "band_power_timeseries": {"F3": {"delta": [1.0], "theta": [1.0], "alpha": [1.0], "beta": [1.0]}},
        "segment_s": 4.0, "step_s": 1.0,
    })
    payload = service.load_configured_spectrogram(recording, config)
    assert payload["algorithm_version"] == "spectrogram-v2-configurable"
    assert payload["actual_end_s"] == 7.634
    assert payload["matrix_shape"] == [1, 1]
    assert payload["first_center_s"] == 1.022
    assert "band_power_timeseries" in payload


def test_configured_spectrogram_returns_backend_custom_band_trend(tmp_path, monkeypatch):
    service, recording = _service(tmp_path)
    sfreq = 100.0
    times = np.arange(30 * int(sfreq)) / sfreq
    data = (10e-6 * np.sin(2 * np.pi * 10 * times))[:, None]
    monkeypatch.setattr(service, "load_data", lambda _recording: (data, sfreq, ["F3"], []))
    config = AnalysisConfigRequest(
        mode="spectrogram", channels=["F3"], time={"start_s": 0, "end_s": 30},
        custom_frequency_range={"low_hz": 8, "high_hz": 13},
    )

    payload = service.load_configured_spectrogram(recording, config)

    np.testing.assert_allclose(
        payload["custom_band_power_timeseries"]["F3"],
        payload["band_power_timeseries"]["F3"]["alpha"],
    )
    assert payload["custom_band"] == {
        "low_hz": 8.0,
        "high_hz": 13.0,
        "unit": "uV^2",
        "integration": "trapezoid_with_interpolated_boundaries",
        "frequency_resolution_hz": 0.25,
        "frequency_points_hz": list(np.arange(8.0, 13.25, 0.25)),
        "algorithm_version": "spectrogram-custom-band-v1",
    }


def test_spectrogram_times_are_window_centers_and_shape_is_stable():
    sfreq = 500.0
    times = np.arange(30 * int(sfreq)) / sfreq
    data = (10e-6 * np.sin(2 * np.pi * 10 * times))[:, None]
    centers, frequencies, power = estimate_spectrogram(data, sfreq)
    assert len(centers) == 27
    assert centers[0] == 2.0
    assert centers[-1] == 28.0
    assert len(frequencies) == 117
    assert power.shape == (27, 1, 117)


def test_spectrogram_quality_keeps_bad_window_position():
    sfreq = 100.0
    values = np.zeros((8 * int(sfreq), 1))
    values[4 * int(sfreq):5 * int(sfreq)] = 200e-6
    centers, _freqs, power, quality = estimate_spectrogram_with_quality(values, sfreq)
    assert centers.tolist() == [2.0, 3.0, 4.0, 5.0, 6.0]
    assert len(quality) == 5
    assert quality[1]["status"] == "bad"
    assert quality[1]["reason"] == "amplitude_threshold"
    assert np.isnan(power[1]).all()
    assert quality[2]["status"] == "bad"
