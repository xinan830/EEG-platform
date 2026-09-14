from pathlib import Path

import numpy as np

from app.models.algorithm_definition import DefinitionCreateRequest, DefinitionVersionDraft
from app.models.run import RunCreateRequest, RunStatus
from app.services.recordings import RecordingService
from app.services.run_queue import PersistentRunQueue


class SyntheticRecordingService(RecordingService):
    def load_data(self, _recording):
        sfreq = 100.0
        time_s = np.arange(3000) / sfreq
        return np.column_stack([10e-6 * np.sin(2 * np.pi * 6 * time_s) + 5e-6 * np.sin(2 * np.pi * 18 * time_s)]), sfreq, ["F3"], []


def _queue_with_metric_definition(tmp_path: Path) -> tuple[PersistentRunQueue, str, str]:
    recordings = SyntheticRecordingService(tmp_path / "recordings", tmp_path / "metric.sqlite3")
    recording = recordings.create_recording("source.edf", ".edf", b"metric-source")
    with recordings._connect() as connection:
        connection.execute("UPDATE recordings SET sfreq = 100, duration_s = 30, channels_json = '[\"F3\"]' WHERE id = ?", (recording.id,))
    queue = PersistentRunQueue(recordings, recordings.database_path, tmp_path / "artifacts")
    definition = queue.base.definition_service.create(DefinitionCreateRequest(name="Theta/Beta 比值"))
    queue.base.definition_service.create_version(definition.definition_id, DefinitionVersionDraft.model_validate({
        "semver": "1.0.0",
        "graph": {"nodes": [
            {"id": "calculation", "type": "divide", "inputs": {"left": "$input.input_left", "right": "$input.input_right"}, "parameters": {}},
            {"id": "output", "type": "output", "inputs": {"source": "calculation"}, "parameters": {}},
        ], "outputs": ["output"]},
        "parameter_schema": {"type": "object", "additionalProperties": False},
        "inputs": {"input_left": {"feature": "theta_power", "unit": "uV^2"}, "input_right": {"feature": "beta_power", "unit": "uV^2"}},
        "outputs": {"output": {"label": "Theta/Beta 比值", "unit": "dimensionless"}},
    }))
    return queue, recording.id, definition.definition_id


def test_definition_metric_run_persists_ratio_and_provenance(tmp_path: Path):
    queue, recording_id, definition_id = _queue_with_metric_definition(tmp_path)
    request = RunCreateRequest.model_validate({
        "recording_id": recording_id,
        "analysis_type": "definition_metric",
        "definition_id": definition_id,
        "definition_version": "1.0.0",
        "config": {"channel": "F3", "time": {"start_s": 0, "end_s": 30}},
    })

    queued = queue.enqueue(request)
    completed = queue.process_next()

    assert queued.status is RunStatus.QUEUED
    assert completed is not None and completed.status is RunStatus.COMPLETED
    metric = completed.result_summary["metric"]
    assert metric["output"]["unit"] == "dimensionless"
    assert metric["output"]["value"] is not None
    assert metric["channel"] == "F3"
    assert metric["actual_range"] == {"start_s": 0.0, "end_s": 30.0}
    assert metric["inputs"]["input_left"]["feature"] == "theta_power"
    assert queue.list_artifacts(queued.run_id)


def test_definition_metric_run_rejects_missing_channel(tmp_path: Path):
    queue, recording_id, definition_id = _queue_with_metric_definition(tmp_path)
    request = RunCreateRequest.model_validate({
        "recording_id": recording_id, "analysis_type": "definition_metric", "definition_id": definition_id, "definition_version": "1.0.0",
        "config": {"channel": "Missing", "time": {"start_s": 0, "end_s": 30}},
    })

    queued = queue.enqueue(request)
    terminal = queue.process_next()

    assert terminal is not None and terminal.status is RunStatus.FAILED
    assert terminal.error is not None
    assert terminal.error.code == "ANALYSIS_INPUT_INVALID"
    assert queued.run_id == terminal.run_id
