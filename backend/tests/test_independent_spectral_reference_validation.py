from pathlib import Path

import numpy as np
from fastapi.testclient import TestClient

from app.main import app
from app.models.spectral_validation import SpectralReferenceValidationRequest
from app.services.independent_spectral_reference import IndependentSpectralReferenceService
from app.services.recordings import RecordingService
from app.services.validations import ValidationService


class _Audit:
    def record(self, *args, **kwargs):
        return None


class SyntheticRecordingService(RecordingService):
    def __init__(self, *args, source: np.ndarray, sfreq: float, names: list[str], **kwargs):
        super().__init__(*args, **kwargs)
        self.source, self.sfreq, self.names = source, sfreq, names

    def load_data(self, _recording):
        return self.source, self.sfreq, self.names, []


def _source(sfreq: float = 100.0) -> tuple[np.ndarray, list[str]]:
    times = np.arange(int(30 * sfreq)) / sfreq
    return np.column_stack([
        11e-6 * np.sin(2 * np.pi * 10 * times) + 1.3e-6 * np.sin(2 * np.pi * 7.3 * times),
        8e-6 * np.sin(2 * np.pi * 20 * times) + 2.1e-6 * np.sin(2 * np.pi * 5.7 * times),
    ]), ["Fz", "F3"]


def _client(tmp_path: Path, source: np.ndarray | None = None) -> tuple[TestClient, object, ValidationService]:
    sfreq, names = 100.0, ["Fz", "F3"]
    values = source if source is not None else _source(sfreq)[0]
    recordings = SyntheticRecordingService(
        tmp_path / "recordings", tmp_path / "catalog.sqlite3", source=values, sfreq=sfreq, names=names,
    )
    recording = recordings.create_recording("synthetic.edf", ".edf", b"synthetic-reference-source")
    with recordings._connect() as connection:
        connection.execute(
            """UPDATE recordings SET sfreq = ?, duration_s = ?, channels_json = ?,
               raw_channel_labels_json = ?, canonical_channel_labels_json = ?,
               channel_types_json = ?, channel_units_json = ? WHERE id = ?""",
            (sfreq, len(values) / sfreq, '["Fz", "F3"]', '["Fz", "F3"]', '["Fz", "F3"]',
             '["eeg", "eeg"]', '["V", "V"]', recording.id),
        )
    recording = recordings.require_recording(recording.id)
    validations = ValidationService(recordings.database_path)
    app.state.recording_service = recordings
    app.state.validation_service = validations
    app.state.independent_spectral_reference_service = IndependentSpectralReferenceService(
        recordings, validations, source_reader=lambda _recording: (values, sfreq, names),
    )
    app.state.audit_service = _Audit()
    return TestClient(app), recording, validations


def test_reference_endpoint_persists_exportable_pointwise_psd_evidence_in_request_order(tmp_path):
    client, recording, _validations = _client(tmp_path)

    response = client.post(f"/api/recordings/{recording.id}/validations/spectral-reference", json={
        "start_s": 2.0, "end_s": 22.0, "channels": ["F3", "Fz"],
    })

    assert response.status_code == 201
    body = response.json()
    assert body["kind"] == "independent_scipy_spectral_psd"
    assert body["algorithm_version"] == "offline-spectral-v3"
    assert body["passed"] is True
    assert body["point_count"] == 234
    assert body["passed_point_count"] == 234
    assert body["evidence"]["channels"] == ["F3", "Fz"]
    assert len(body["evidence"]["frequencies_hz"]) == 117
    assert body["evidence"]["unit"] == "uV^2/Hz"
    assert body["evidence"]["actual_range"] == {"start_s": 2.0, "end_s": 22.0, "duration_s": 20.0}
    report = client.get(f"/api/validations/{body['validation_id']}/report")
    assert report.status_code == 200
    assert report.json()["evidence"]["reference_implementation"] == "independent-scipy-mne-spectral-reference-v1"
    assert report.json()["evidence"]["production_psd"]["F3"] == body["evidence"]["production_psd"]["F3"]


def test_reference_calculation_does_not_call_production_spectral_helpers(tmp_path, monkeypatch):
    _client_instance, recording, validations = _client(tmp_path)
    values, names = _source()
    reference = IndependentSpectralReferenceService(
        app.state.recording_service, validations, source_reader=lambda _recording: (values, 100.0, names),
    )
    import app.scientific.primitives.spectral as production_spectral

    monkeypatch.setattr(production_spectral, "preprocess_offline", lambda *_args: (_ for _ in ()).throw(AssertionError("production preprocessing called")))
    monkeypatch.setattr(production_spectral, "estimate_welch_psd", lambda *_args: (_ for _ in ()).throw(AssertionError("production Welch called")))
    result = reference._calculate_reference(
        recording,
        SpectralReferenceValidationRequest(start_s=0.0, end_s=10.0, channels=["Fz"]),
        ["Fz"],
    )

    assert result.psd_uv2_hz.shape == (1, 117)
    assert result.quality["clean_segments"] == 4


def test_reference_endpoint_returns_structured_unavailable_without_zero_evidence(tmp_path):
    values = np.full((3000, 2), 300e-6)
    client, recording, validations = _client(tmp_path, values)

    response = client.post(f"/api/recordings/{recording.id}/validations/spectral-reference", json={
        "start_s": 0.0, "end_s": 10.0, "channels": ["F3"],
    })

    assert response.status_code == 422
    assert response.json()["code"] == "SPECTRAL_REFERENCE_UNAVAILABLE"
    assert "production_psd_unavailable" in response.json()["message"]
    assert validations.list() == []
