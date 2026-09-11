from pathlib import Path

import numpy as np

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


def test_spectrum_reuses_continuous_preprocessed_recording(tmp_path, monkeypatch):
    service, recording = _service(tmp_path)
    sfreq = 100.0
    data = np.zeros((30 * int(sfreq), 2))
    monkeypatch.setattr(service, "load_data", lambda _recording: (data, sfreq, ["Fz", "Pz"], []))
    calls = {"count": 0}

    from app.eeg_core import spectral
    original = spectral.preprocess_offline

    def counted(values, rate):
        calls["count"] += 1
        return original(values, rate)

    monkeypatch.setattr(spectral, "preprocess_offline", counted)
    service.load_spectrum(recording, channels=["Fz"], window_s=10)
    service.load_spectrum(recording, channels=["Pz"], window_s=10)

    assert calls["count"] == 1
