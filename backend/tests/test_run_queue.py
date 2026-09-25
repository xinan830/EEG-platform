from pathlib import Path

import numpy as np
import pytest
from fastapi.testclient import TestClient

from app.main import app
from app.models.run import RunCreateRequest, RunStatus
from app.services.recordings import RecordingService
from app.services.run_queue import PersistentRunQueue
from app.services.runs import RetiredUserAlgorithmError


class SyntheticRecordingService(RecordingService):
    def load_data(self, _recording):
        sfreq = 100.0
        time_s = np.arange(1000) / sfreq
        return np.column_stack([10e-6 * np.sin(2 * np.pi * 10 * time_s)]), sfreq, ["F3"], []


def _queue(tmp_path: Path) -> tuple[PersistentRunQueue, str]:
    recordings = SyntheticRecordingService(tmp_path / "recordings", tmp_path / "queue.sqlite3")
    recording = recordings.create_recording("source.edf", ".edf", b"queue-source")
    with recordings._connect() as connection:
        connection.execute("UPDATE recordings SET sfreq = 100, duration_s = 10, channels_json = '[\"F3\"]' WHERE id = ?", (recording.id,))
    return PersistentRunQueue(recordings, recordings.database_path, tmp_path / "artifacts"), recording.id


def _request(recording_id: str, key: str | None = None) -> RunCreateRequest:
    return RunCreateRequest(recording_id=recording_id, analysis_type="spectrum", idempotency_key=key,
                             config={"channels": ["F3"], "time": {"start_s": 0, "end_s": 10}})


def test_queue_idempotency_claim_and_completion(tmp_path: Path):
    queue, recording_id = _queue(tmp_path)
    first = queue.enqueue(_request(recording_id, "client-1"))
    repeated = queue.enqueue(_request(recording_id, "client-1"))

    assert first.run_id == repeated.run_id
    assert first.status is RunStatus.QUEUED
    completed = queue.process_next()
    assert completed is not None and completed.status is RunStatus.COMPLETED
    assert queue.list_artifacts(first.run_id)


def test_production_queue_rejects_new_user_defined_algorithm_runs(tmp_path: Path):
    queue, recording_id = _queue(tmp_path)
    request = RunCreateRequest(
        recording_id=recording_id,
        analysis_type="definition_metric",
        config={},
    )
    with pytest.raises(RetiredUserAlgorithmError):
        queue.enqueue(request)
    with pytest.raises(RetiredUserAlgorithmError):
        queue.base.create(request)


def test_recovered_legacy_user_job_fails_without_executing_or_rewriting_history(tmp_path: Path):
    queue, recording_id = _queue(tmp_path)
    queued = queue.enqueue(_request(recording_id))
    legacy = queued.model_copy(update={
        "run_id": "queued-legacy-user-definition",
        "analysis_type": "definition_metric",
        "definition_id": "historic-definition",
        "definition_version": "1.0.0",
        "cache_key": "historic-legacy-job",
    })
    queue.repository.create(legacy)
    queue.cancel(queued.run_id)

    result = queue.process_next()

    assert result is not None and result.run_id == legacy.run_id
    assert result.status is RunStatus.FAILED
    assert result.error is not None and result.error.code == "USER_DEFINED_ALGORITHM_RETIRED"
    assert queue.list_artifacts(legacy.run_id) == []


def test_queue_persists_resolved_official_definition_identity(tmp_path: Path):
    queue, recording_id = _queue(tmp_path)
    request = RunCreateRequest(
        recording_id=recording_id,
        analysis_type="official_algorithm",
        config={
            "algorithm_id": "iapf",
            "scientific_version": "official-iapf-v2",
            "channel": "F3",
            "time": {"start_s": 0, "end_s": 10},
            "mode": "static",
        },
    )

    queued = queue.enqueue(request)

    assert queued.definition_id is not None
    assert queued.definition_version == "1.0.0"
    assert queued.scientific_version == "official-iapf-v2"
    completed = queue.process_next()
    assert completed is not None and completed.status is RunStatus.COMPLETED
    assert completed.definition_id == queued.definition_id


def test_queue_cancel_recovery_and_retry_keep_provenance(tmp_path: Path):
    queue, recording_id = _queue(tmp_path)
    cancelled = queue.enqueue(_request(recording_id))
    assert queue.cancel(cancelled.run_id).status is RunStatus.CANCELLED
    retry = queue.retry(cancelled.run_id)
    assert retry.parent_run_id == cancelled.run_id

    stranded = queue.enqueue(_request(recording_id))
    claimed = queue.repository.claim_next_queued()
    assert claimed is not None and claimed.status is RunStatus.RUNNING
    recovered_queue = PersistentRunQueue(queue.recordings, queue.repository.database_path, tmp_path / "artifacts")
    assert recovered_queue.get(claimed.run_id).status is RunStatus.QUEUED


def test_run_api_enqueues_with_202_then_exposes_worker_result(tmp_path: Path):
    queue, recording_id = _queue(tmp_path)

    class Worker:
        def wake(self):
            return None

    app.state.run_service = queue
    app.state.run_worker = Worker()
    response = TestClient(app).post("/api/runs", json=_request(recording_id, "api-key").model_dump(mode="json"))

    assert response.status_code == 202
    assert response.json()["status"] == "queued"
    queue.process_next()
    assert TestClient(app).get(f"/api/runs/{response.json()['run_id']}").json()["status"] == "completed"


def test_queue_recovers_one_hundred_persisted_jobs_without_duplicate_ids(tmp_path: Path):
    queue, recording_id = _queue(tmp_path)
    run_ids = {queue.enqueue(_request(recording_id, f"bulk-{index}")).run_id for index in range(100)}
    assert len(run_ids) == 100
    assert queue.repository.claim_next_queued() is not None

    recovered = PersistentRunQueue(queue.recordings, queue.repository.database_path, tmp_path / "artifacts")
    states = [item.status for item in recovered.list(limit=1000)]
    assert len(states) == 100
    assert RunStatus.RUNNING not in states
    assert states.count(RunStatus.QUEUED) == 100
