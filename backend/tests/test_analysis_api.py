from pathlib import Path

import numpy as np
from fastapi.testclient import TestClient

from app.main import app
from app.models.recording import ChannelMapping
from app.services.recordings import RecordingService
from app.services.runs import RunService


class SyntheticLegacyRecordingService(RecordingService):
    def load_data(self, _recording):
        sfreq = 100.0
        times = np.arange(45 * int(sfreq)) / sfreq
        alpha = 12e-6 * np.sin(2 * np.pi * 10 * times)
        values = np.column_stack([alpha * 0.8, alpha, alpha * 1.2])
        return values, sfreq, ["Fz", "Pz", "Oz"], []


class NoopAuditService:
    def record(self, *_args, **_kwargs) -> None:
        return None


def test_analysis_requires_a_saved_channel_mapping(tmp_path, monkeypatch):
    service = RecordingService(
        storage_dir=tmp_path / "recordings",
        database_path=tmp_path / "catalog.sqlite3",
    )
    recording = service.create_recording("sample.bdf", ".bdf", b"raw")
    monkeypatch.setattr(app.state, "recording_service", service)
    monkeypatch.setattr(app.state, "run_service", RunService(service, service.database_path, tmp_path / "artifacts"))
    monkeypatch.setattr(app.state, "audit_service", NoopAuditService())

    response = TestClient(app).post(f"/api/recordings/{recording.id}/analysis")

    assert response.status_code == 409
    assert response.json()["code"] == "ANALYSIS_MAPPING_REQUIRED"
    assert "映射" in response.json()["detail"]


def test_legacy_analysis_route_creates_a_traceable_run_and_artifact(tmp_path: Path, monkeypatch):
    service = SyntheticLegacyRecordingService(
        storage_dir=tmp_path / "recordings",
        database_path=tmp_path / "catalog.sqlite3",
    )
    recording = service.create_recording("synthetic.edf", ".edf", b"synthetic-source")
    with service._connect() as connection:
        connection.execute(
            """UPDATE recordings SET sfreq = 100, duration_s = 45,
               channels_json = '[\"Fz\", \"Pz\", \"Oz\"]',
               raw_channel_labels_json = '[\"Fz\", \"Pz\", \"Oz\"]',
               canonical_channel_labels_json = '[\"Fz\", \"Pz\", \"Oz\"]',
               channel_types_json = '[\"eeg\", \"eeg\", \"eeg\"]',
               channel_units_json = '[\"V\", \"V\", \"V\"]',
               mapping_json = ? WHERE id = ?""",
            ('{"fz":"Fz","pz":"Pz","oz":"Oz","f3":null,"f4":null}', recording.id),
        )
    run_service = RunService(service, service.database_path, tmp_path / "artifacts")
    monkeypatch.setattr(app.state, "recording_service", service)
    monkeypatch.setattr(app.state, "run_service", run_service)
    monkeypatch.setattr(app.state, "audit_service", NoopAuditService())

    client = TestClient(app)
    created = client.post(f"/api/recordings/{recording.id}/analysis")

    assert created.status_code == 201
    summary = created.json()
    run = run_service.get(summary["analysis_id"])
    assert run.analysis_type == "legacy_analysis"
    assert run.status.value == "completed"
    assert run.result_summary is not None
    assert len(run_service.list_artifacts(run.run_id)) == 1

    result = client.get(summary["result_url"])
    assert result.status_code == 200
    assert result.json()["analysis_id"] == run.run_id
    assert result.json()["algorithm_contract"]["algorithm_version"] == "offline-spectral-v3"
