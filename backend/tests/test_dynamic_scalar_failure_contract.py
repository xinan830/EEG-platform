from pathlib import Path

import numpy as np

from app.algorithms.catalog import ensure_official_definitions
from app.models.run import RunCreateRequest, RunStatus
from app.services.recordings import RecordingService
from app.services.runs import RunService


class HighAmplitudeRecordingService(RecordingService):
    def load_data(self, _recording):
        sfreq = 100.0
        time_s = np.arange(int(30 * sfreq)) / sfreq
        signal = 400e-6 * np.sin(2 * np.pi * 10 * time_s)
        return signal[:, None], sfreq, ["Fz"], []


def test_dynamic_peak_frequency_preserves_backend_failure_per_window(tmp_path: Path):
    recordings = HighAmplitudeRecordingService(tmp_path / "recordings", tmp_path / "official.sqlite3")
    recording = recordings.create_recording("quality.edf", ".edf", b"source")
    with recordings._connect() as connection:
        connection.execute(
            "UPDATE recordings SET sfreq = 100, duration_s = 30, channels_json = '[\"Fz\"]' WHERE id = ?",
            (recording.id,),
        )
    service = RunService(recordings, recordings.database_path, tmp_path / "artifacts")
    ensure_official_definitions(service.definition_service)
    completed = service.create(RunCreateRequest(
        recording_id=recording.id,
        analysis_type="official_algorithm",
        config={
            "algorithm_id": "peak_frequency", "channel": "Fz", "mode": "dynamic",
            "time": {"start_s": 0, "end_s": 30},
            "dynamic_window_s": 10, "refresh_step_s": 1,
            "low_hz": 8, "high_hz": 13,
        },
    ))

    assert completed.status is RunStatus.COMPLETED
    series = completed.result_summary["metric"]["series"]
    rejected = [point for point in series if point["analysis_state"] == "Rejected"]
    assert rejected
    assert all(point["value"] is None for point in rejected)
    assert all(point["failure"]["code"] == "PSD_QUALITY_GATE_FAILED" for point in rejected)
    assert all(point["failure"]["message"] for point in rejected)
