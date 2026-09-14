from pathlib import Path

import numpy as np
import pytest

from app.models.batch_run import BatchRunCreateRequest
from app.models.research_project import ProjectCreateRequest, SessionCreateRequest, SubjectCreateRequest
from app.services.batch_runs import BatchProjectMembershipError, BatchRunService
from app.services.projects import ProjectService
from app.services.recordings import RecordingService
from app.services.run_queue import PersistentRunQueue
from app.main import app
from fastapi.testclient import TestClient
from app.models.run import RunStatus, StructuredRunError


class SyntheticRecordings(RecordingService):
    def load_data(self, recording):
        time_s = np.arange(int((recording.duration_s or 10) * 100)) / 100
        return (10e-6 * np.sin(2 * np.pi * 10 * time_s))[:, None], 100.0, ["F3"], []


def _recording(service: RecordingService, name: str, channels: str, duration: float) -> str:
    item = service.create_recording(name, ".edf", name.encode())
    with service._connect() as connection:
        connection.execute("UPDATE recordings SET sfreq = 100, duration_s = ?, channels_json = ? WHERE id = ?", (duration, channels, item.id))
    return item.id


def _service(tmp_path: Path):
    database = tmp_path / "platform.sqlite3"
    recordings = SyntheticRecordings(tmp_path / "recordings", database)
    projects = ProjectService(database)
    queue = PersistentRunQueue(recordings, database, tmp_path / "artifacts")
    return recordings, projects, queue, BatchRunService(projects, queue, database)


def _project_with_sessions(recordings, projects, ids):
    project = projects.create(ProjectCreateRequest(name="Project"))
    subject = projects.create_subject(project.project_id, SubjectCreateRequest(local_code="S-1"))
    for recording_id in ids:
        projects.create_session(project.project_id, SessionCreateRequest(subject_id=subject.subject_id, recording_id=recording_id))
    return project


def test_batch_preflight_distinguishes_missing_channel_and_short_recording(tmp_path: Path):
    recordings, projects, queue, batches = _service(tmp_path)
    complete = _recording(recordings, "complete.edf", '["F3"]', 10)
    missing = _recording(recordings, "missing.edf", '["Fz"]', 10)
    short = _recording(recordings, "short.edf", '["F3"]', 5)
    project = _project_with_sessions(recordings, projects, [complete, missing, short])

    batch = batches.create(BatchRunCreateRequest(project_id=project.project_id, recording_ids=[complete, missing, short],
        config={"channels": ["F3"], "time": {"start_s": 0, "end_s": 10}}))
    queue.process_next()
    outcomes = {item.recording_id: item.outcome for item in batches.items(batch.batch_run_id)}

    assert outcomes[complete] == "completed"
    assert outcomes[missing] == "missing_channel"
    assert outcomes[short] == "insufficient_duration"


def test_batch_requires_project_recording_and_can_cancel_child(tmp_path: Path):
    recordings, projects, _queue, batches = _service(tmp_path)
    included = _recording(recordings, "included.edf", '["F3"]', 10)
    outside = _recording(recordings, "outside.edf", '["F3"]', 10)
    project = _project_with_sessions(recordings, projects, [included])

    with pytest.raises(BatchProjectMembershipError):
        batches.create(BatchRunCreateRequest(project_id=project.project_id, recording_ids=[outside]))
    batch = batches.create(BatchRunCreateRequest(project_id=project.project_id, recording_ids=[included],
        config={"channels": ["F3"], "time": {"start_s": 0, "end_s": 10}}, idempotency_key="batch-key"))
    assert batches.create(BatchRunCreateRequest(project_id=project.project_id, recording_ids=[included],
        config={"channels": ["F3"], "time": {"start_s": 0, "end_s": 10}}, idempotency_key="batch-key")).batch_run_id == batch.batch_run_id
    assert batches.cancel(batch.batch_run_id).status == "cancelled"
    assert batches.items(batch.batch_run_id)[0].outcome == "cancelled"


def test_batch_api_returns_project_scoped_queue_items(tmp_path: Path):
    recordings, projects, queue, batches = _service(tmp_path)
    recording_id = _recording(recordings, "api.edf", '["F3"]', 10)
    project = _project_with_sessions(recordings, projects, [recording_id])

    class Worker:
        def wake(self):
            return None

    app.state.project_service, app.state.run_service = projects, queue
    app.state.batch_run_service, app.state.run_worker = batches, Worker()
    response = TestClient(app).post("/api/batch-runs", json={
        "project_id": project.project_id, "recording_ids": [recording_id],
        "config": {"channels": ["F3"], "time": {"start_s": 0, "end_s": 10}},
    })

    assert response.status_code == 202
    assert TestClient(app).get(f"/api/batch-runs/{response.json()['batch_run_id']}/items").json()[0]["outcome"] == "queued"


def test_batch_reports_gate_failed_and_failed_child_outcomes(tmp_path: Path):
    recordings, projects, queue, batches = _service(tmp_path)
    first = _recording(recordings, "gate.edf", '["F3"]', 10)
    second = _recording(recordings, "failed.edf", '["F3"]', 10)
    project = _project_with_sessions(recordings, projects, [first, second])
    batch = batches.create(BatchRunCreateRequest(project_id=project.project_id, recording_ids=[first, second],
        config={"channels": ["F3"], "time": {"start_s": 0, "end_s": 10}}))

    gate = queue.repository.claim_next_queued()
    assert gate is not None
    queue.repository.update_status(gate.run_id, RunStatus.GATE_FAILED,
        error=StructuredRunError(code="QUALITY_GATE_FAILED", message="quality", stage="analysis"))
    failed = queue.repository.claim_next_queued()
    assert failed is not None
    queue.repository.update_status(failed.run_id, RunStatus.FAILED,
        error=StructuredRunError(code="ANALYSIS_EXECUTION_FAILED", message="failed", stage="analysis"))

    outcomes = {item.recording_id: item.outcome for item in batches.items(batch.batch_run_id)}
    assert set(outcomes.values()) == {"gate_failed", "failed"}
