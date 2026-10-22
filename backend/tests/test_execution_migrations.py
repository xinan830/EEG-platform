from contextlib import closing
import json
import sqlite3

import pytest

from app.persistence import migrate_database
from app.persistence.migrations import CURRENT_SCHEMA_VERSION
from app.persistence.repositories.run import RunRepository
from tests.test_run_queue import _queue, _request


@pytest.mark.parametrize("previous_version", [10, 11])
def test_old_schema_upgrade_preserves_run_and_request_identity(tmp_path, previous_version):
    queue, recording_id = _queue(tmp_path)
    run = queue.enqueue(_request(recording_id, "old-request"))
    final = queue.process_next()
    with closing(sqlite3.connect(queue.repository.database_path)) as connection:
        # This table did not exist in the schema being reconstructed.
        connection.execute("DROP TABLE analysis_montage_snapshots")
        connection.execute("DROP TABLE run_request_keys")
        connection.execute("ALTER TABLE analysis_runs DROP COLUMN execution_lane")
        if previous_version == 10:
            for column in ("progress_completed", "progress_total", "progress_stage", "progress_message"):
                connection.execute(f"ALTER TABLE analysis_runs DROP COLUMN {column}")
        connection.execute("UPDATE schema_metadata SET value = ? WHERE key = 'schema_version'", (str(previous_version),))
        connection.commit()
    assert migrate_database(queue.repository.database_path) == CURRENT_SCHEMA_VERSION
    repository = RunRepository(queue.repository.database_path)
    restored = repository.get(run.run_id)
    assert restored.result_summary == final.result_summary
    assert restored.config_sha256 == final.config_sha256
    assert repository.find_idempotency_key("old-request").run_id == run.run_id
    assert len(repository.list_artifacts(run.run_id)) == 1


def test_retired_faa_result_is_rejected_without_reexecution_or_mutation(tmp_path, monkeypatch):
    from app.services.analysis_provenance import serialize_analysis_run
    from app.services.current_algorithm_result import InvalidCurrentResultError
    queue, recording_id = _queue(tmp_path)
    run = queue.enqueue(_request(recording_id))
    final = queue.process_next()
    legacy_filters = {"operation": "continuous_preprocess_then_per_epoch_mean_removal",
                      "software_bandpass": [1, 50], "notch_hz": 50}
    with closing(sqlite3.connect(queue.repository.database_path)) as connection:
        connection.execute("""UPDATE analysis_runs SET analysis_type = 'official_algorithm',
                           scientific_version = 'official-faa-v1', definition_version = '1.0.0',
                           config_json = ?, filter_json = ? WHERE run_id = ?""",
                           (json.dumps({"algorithm_id": "faa", "mode": "static"}), json.dumps(legacy_filters), run.run_id))
        connection.commit()
    historical = queue.get(run.run_id)
    assert historical.scientific_version == "official-faa-v1"
    assert historical.definition_version == "1.0.0"
    assert historical.result_summary == final.result_summary
    artifacts = queue.list_artifacts(run.run_id)
    saved_arrays = [(queue.base.artifacts.root / item.relative_path).read_bytes() for item in artifacts]
    saved_database = queue.repository.database_path.read_bytes()

    def unexpected_execution(*args, **kwargs):
        pytest.fail("Unsupported result reads must not execute science or decode arrays")

    monkeypatch.setattr(queue, "process_next", unexpected_execution)
    monkeypatch.setattr(queue.base.artifacts, "read_npz", unexpected_execution)
    with pytest.raises(InvalidCurrentResultError, match="current official algorithm contract"):
        serialize_analysis_run(historical)

    assert queue.repository.database_path.read_bytes() == saved_database
    assert queue.get(run.run_id).model_dump() == historical.model_dump()
    assert historical.filters == legacy_filters
    assert [(queue.base.artifacts.root / item.relative_path).read_bytes() for item in artifacts] == saved_arrays
