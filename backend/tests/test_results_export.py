import io
import json
import zipfile

import pytest

from app.services.artifacts import ArtifactIntegrityError
from app.services.results import ResultService
from app.services.validations import ValidationService
from test_run_queue import _queue, _request


def test_result_export_has_manifest_summary_and_verified_npz_without_filename(tmp_path):
    queue, recording_id = _queue(tmp_path)
    run = queue.enqueue(_request(recording_id))
    queue.process_next()
    service = ResultService(queue, ValidationService(queue.repository.database_path))

    package = service.export(run.run_id)
    with zipfile.ZipFile(io.BytesIO(package)) as archive:
        manifest = json.loads(archive.read("manifest.json"))
        assert manifest["run_id"] == run.run_id
        assert manifest["scope"] == "research_output_not_clinical_conclusion"
        assert "source.edf" not in archive.read("result.json").decode()
        assert any(name.startswith("artifacts/") and name.endswith(".npz") for name in archive.namelist())


def test_result_export_rejects_corrupt_artifact_and_keeps_cancelled_output_null(tmp_path):
    queue, recording_id = _queue(tmp_path)
    completed = queue.enqueue(_request(recording_id))
    queue.process_next()
    artifact = queue.list_artifacts(completed.run_id)[0]
    path = queue.base.artifacts.root / artifact.relative_path
    path.write_bytes(b"corrupt")
    service = ResultService(queue, ValidationService(queue.repository.database_path))
    with pytest.raises(ArtifactIntegrityError):
        service.export(completed.run_id)

    cancelled = queue.enqueue(_request(recording_id))
    queue.cancel(cancelled.run_id)
    view = service.view(cancelled.run_id)
    assert view["run"]["result_summary"] is None
