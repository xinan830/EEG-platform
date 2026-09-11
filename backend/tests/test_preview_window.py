from pathlib import Path

import numpy as np

from app.services.recordings import RecordingService


def test_preview_returns_a_high_resolution_requested_time_window(tmp_path: Path, monkeypatch):
    service = RecordingService(
        storage_dir=tmp_path / "recordings",
        database_path=tmp_path / "catalog.sqlite3",
    )
    recording = service.create_recording("sample.bdf", ".bdf", b"raw")

    def preview_data(_recording, start_s, window_s):
        assert start_s == 12.0
        assert window_s == 2.0
        return (
            np.array([[0.000001], [0.000002], [0.000003], [0.000004]]),
            2.0,
            ["Fz"],
            [{"elapsed_s": 12.5, "label": "开始"}],
            20.0,
        )

    monkeypatch.setattr(service, "load_preview_data", preview_data)

    payload = service.load_preview(recording, start_s=12.0, window_s=2.0, max_points=10)

    assert payload["duration_s"] == 20.0
    assert payload["window_start_s"] == 12.0
    assert payload["elapsed_s"] == [12.0, 12.5, 13.0, 13.5]
    assert payload["channels"] == {"Fz": [-1.5, -0.5, 0.5, 1.5]}
    assert payload["events"] == [{"elapsed_s": 12.5, "label": "开始"}]
