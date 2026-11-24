import asyncio
import hashlib
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from threading import Event

import numpy as np
import pyedflib
import pytest
from fastapi.testclient import TestClient

from app.main import app
from app.services.audit import AuditService
from app.services.projects import ProjectService
from app.services.recordings import RecordingService


def _edf_bytes(path: Path) -> bytes:
    writer = pyedflib.EdfWriter(str(path), 2, pyedflib.FILETYPE_EDFPLUS)
    writer.setSignalHeaders([
        {"label": "Fz", "dimension": "uV", "sample_frequency": 10, "physical_min": -100,
         "physical_max": 100, "digital_min": -32768, "digital_max": 32767, "transducer": "", "prefilter": ""},
        {"label": "Pz", "dimension": "uV", "sample_frequency": 10, "physical_min": -100,
         "physical_max": 100, "digital_min": -32768, "digital_max": 32767, "transducer": "", "prefilter": ""},
    ])
    writer.writeSamples([np.arange(20, dtype=np.float64), np.arange(20, dtype=np.float64) * 2])
    writer.close()
    return path.read_bytes()


def _client(tmp_path: Path) -> tuple[TestClient, RecordingService, ProjectService]:
    database = tmp_path / "platform.sqlite3"
    recordings = RecordingService(tmp_path / "recordings", database)
    projects = ProjectService(database)
    app.state.recording_service = recordings
    app.state.project_service = projects
    app.state.audit_service = AuditService(database)
    return TestClient(app), recordings, projects


def test_standalone_import_streams_hash_and_preserves_source_channel_order(tmp_path: Path):
    client, recordings, _ = _client(tmp_path)
    source = _edf_bytes(tmp_path / "source.edf")

    response = client.post("/api/recordings/import", files={"file": ("source.edf", source)})

    assert response.status_code == 201, response.json()
    payload = response.json()
    assert payload["source_sha256"] == hashlib.sha256(source).hexdigest()
    assert payload["file_size_bytes"] == len(source)
    assert payload["channels"] == ["Fz", "Pz"]
    assert payload["channel_types"] == ["eeg", "eeg"]
    assert (recordings.storage_dir / payload["stored_name"]).read_bytes() == source


def test_project_import_creates_recording_session_and_subject_atomically(tmp_path: Path):
    client, recordings, projects = _client(tmp_path)
    project_id = client.put("/api/projects/desktop/desktop-id", json={"name": "Study"}).json()["project_id"]
    source = _edf_bytes(tmp_path / "project.edf")

    response = client.post(f"/api/projects/{project_id}/recordings/import",
                           data={"new_subject_local_code": "S-01"}, files={"file": ("project.edf", source)})

    assert response.status_code == 201, response.json()
    payload = response.json()
    assert payload["project_id"] == project_id
    assert payload["channels"] == ["Fz", "Pz"]
    assert client.get(f"/api/projects/{project_id}/sessions").json()[0]["session_id"] == payload["session_id"]
    assert client.get(f"/api/projects/{project_id}/recordings").json()["recording_ids"] == [payload["recording_id"]]
    assert len(recordings.list_recordings()) == 1
    assert [subject.local_code for subject in projects.list_subjects(project_id)] == ["S-01"]


def test_cross_project_subject_and_malformed_file_leave_no_partial_records(tmp_path: Path):
    client, recordings, _ = _client(tmp_path)
    first = client.put("/api/projects/desktop/first", json={"name": "First"}).json()["project_id"]
    second = client.put("/api/projects/desktop/second", json={"name": "Second"}).json()["project_id"]
    subject = client.post(f"/api/projects/{first}/subjects", json={"local_code": "S-01"}).json()["subject_id"]

    crossed = client.post(f"/api/projects/{second}/recordings/import", data={"subject_id": subject},
                          files={"file": ("valid.edf", _edf_bytes(tmp_path / "valid.edf"))})
    malformed = client.post(f"/api/projects/{second}/recordings/import", data={"new_subject_local_code": "S-02"},
                            files={"file": ("bad.edf", b"not edf")})

    assert crossed.status_code == 409 and crossed.json()["code"] == "SUBJECT_PROJECT_MISMATCH"
    assert malformed.status_code == 422 and malformed.json()["code"] == "RECORDING_IMPORT_INVALID"
    assert recordings.list_recordings() == []
    assert client.get(f"/api/projects/{second}/sessions").json() == []
    assert [item["local_code"] for item in client.get(f"/api/projects/{second}/subjects").json()] == []
    assert not list(recordings.storage_dir.glob(".import-*.part"))


def test_duplicate_new_subject_code_rolls_back_recording_and_session(tmp_path: Path):
    client, recordings, _ = _client(tmp_path)
    project_id = client.put("/api/projects/desktop/duplicate", json={"name": "Study"}).json()["project_id"]
    source = _edf_bytes(tmp_path / "duplicate.edf")
    first = client.post(f"/api/projects/{project_id}/recordings/import",
                        data={"new_subject_local_code": "S-01"}, files={"file": ("first.edf", source)})
    duplicate = client.post(f"/api/projects/{project_id}/recordings/import",
                            data={"new_subject_local_code": "S-01"}, files={"file": ("second.edf", source)})

    assert first.status_code == 201
    assert duplicate.status_code == 409 and duplicate.json()["code"] == "SUBJECT_CODE_CONFLICT"
    assert len(recordings.list_recordings()) == 1
    assert len(client.get(f"/api/projects/{project_id}/sessions").json()) == 1
    assert len(client.get(f"/api/projects/{project_id}/subjects").json()) == 1
    assert len(list(recordings.storage_dir.glob("*.edf"))) == 1


def test_concurrent_imports_for_one_project_publish_independent_sessions(tmp_path: Path):
    client, recordings, _ = _client(tmp_path)
    project_id = client.put("/api/projects/desktop/concurrent", json={"name": "Study"}).json()["project_id"]
    subject_id = client.post(f"/api/projects/{project_id}/subjects", json={"local_code": "S-01"}).json()["subject_id"]
    first_source = _edf_bytes(tmp_path / "first.edf")
    second_source = _edf_bytes(tmp_path / "second.edf")

    def import_file(name: str, source: bytes) -> dict:
        with TestClient(app) as concurrent_client:
            response = concurrent_client.post(
                f"/api/projects/{project_id}/recordings/import",
                data={"subject_id": subject_id},
                files={"file": (name, source)},
            )
        assert response.status_code == 201, response.json()
        return response.json()

    with ThreadPoolExecutor(max_workers=2) as executor:
        results = list(executor.map(import_file, ["first.edf", "second.edf"], [first_source, second_source]))

    sessions = client.get(f"/api/projects/{project_id}/sessions").json()
    assert len({item["recording_id"] for item in results}) == 2
    assert len({item["session_id"] for item in results}) == 2
    assert {item["session_id"] for item in sessions} == {item["session_id"] for item in results}
    assert len(recordings.list_recordings()) == 2
    assert len(list(recordings.storage_dir.glob("*.edf"))) == 2


def test_raw_sample_range_returns_bounded_file_relative_volts_in_source_order(tmp_path: Path):
    client, _, _ = _client(tmp_path)
    source = _edf_bytes(tmp_path / "range.edf")
    imported = client.post("/api/recordings/import", files={"file": ("range.edf", source)}).json()

    response = client.get(f"/api/recordings/{imported['id']}/raw-samples",
                          params={"start_sample": 3, "end_sample": 6})
    too_large = client.get(f"/api/recordings/{imported['id']}/raw-samples",
                           params={"start_sample": 0, "end_sample": 2000000})

    assert response.status_code == 200, response.json()
    payload = response.json()
    assert (payload["start_sample"], payload["end_sample"]) == (3, 6)
    assert payload["channels"] == ["Fz", "Pz"]
    assert payload["channel_units"] == ["V", "V"]
    assert len(payload["values_v"]) == 3
    assert payload["values_v"][0][0] == pytest.approx(3e-6, abs=1e-8)
    assert payload["unavailable_intervals"] == []
    assert too_large.status_code == 422 and too_large.json()["code"] == "SAMPLE_RANGE_INVALID"


def test_stage_cancellation_removes_temporary_file(tmp_path: Path):
    _, recordings, _ = _client(tmp_path)
    from app.services.recording_import import EdfBdfImportService
    importer = EdfBdfImportService(recordings)
    cancel = asyncio.Event()

    async def chunks():
        yield b"part"
        cancel.set()
        yield b"more"

    async def check_cancelled():
        if cancel.is_set():
            raise RuntimeError("cancelled")

    try:
        asyncio.run(importer.stage_upload("cancel.edf", chunks(), check_cancelled))
        assert False, "cancellation must abort the staging operation"
    except RuntimeError as exc:
        assert str(exc) == "cancelled"
    assert not list(recordings.storage_dir.glob(".import-*.part.*"))


def test_stage_disk_write_failure_removes_partial_file(tmp_path: Path, monkeypatch):
    _, recordings, _ = _client(tmp_path)
    from app.services.recording_import import EdfBdfImportService
    importer = EdfBdfImportService(recordings)
    original_open = Path.open

    class FailingWriter:
        def __init__(self, file): self.file = file
        def __enter__(self): return self
        def __exit__(self, *args): return self.file.__exit__(*args)
        def write(self, chunk):
            self.file.write(chunk[:4])
            raise OSError("disk full")
        def __getattr__(self, name): return getattr(self.file, name)

    def open_with_failure(path, *args, **kwargs):
        file = original_open(path, *args, **kwargs)
        return FailingWriter(file) if path.name.startswith(".import-") else file

    monkeypatch.setattr(Path, "open", open_with_failure)

    async def chunks():
        yield b"file-data"

    async def never_cancel(): return None

    try:
        asyncio.run(importer.stage_upload("broken.edf", chunks(), never_cancel))
        assert False, "disk write failure must abort staging"
    except OSError as exc:
        assert str(exc) == "disk full"
    assert not list(recordings.storage_dir.glob(".import-*.part.*"))


def test_disconnect_cancels_background_publication_before_database_commit():
    from app.api.recording_uploads import publish_upload
    from app.services.recording_import import RecordingImportError

    class Request:
        async def is_disconnected(self): return True

    def publish(check_cancelled):
        while True:
            check_cancelled()

    try:
        asyncio.run(publish_upload(Request(), publish))
        assert False, "disconnect must cancel publication"
    except RecordingImportError as exc:
        assert exc.code == "RECORDING_IMPORT_CANCELLED"
