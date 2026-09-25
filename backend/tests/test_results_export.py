import io
import json
import zipfile

import numpy as np
import pytest

from app.services.artifacts import ArtifactIntegrityError
from app.models.run import RunStatus, ValidationCreateRequest
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


def test_result_view_uses_same_analysis_provenance_projection(tmp_path):
    queue, recording_id = _queue(tmp_path)
    run = queue.enqueue(_request(recording_id))
    queue.process_next()

    view = ResultService(queue, ValidationService(queue.repository.database_path)).view(run.run_id)

    assert view["run"]["analysis_provenance"]["contract_version"] == "analysis-provenance-v1"
    assert view["run"]["analysis_provenance"]["config_sha256"] == run.config_sha256


def test_structured_preview_verifies_artifact_and_converts_nan_to_null(tmp_path):
    queue, recording_id = _queue(tmp_path)
    run = queue.enqueue(_request(recording_id))
    artifact = queue.base.artifacts.write_npz(
        run.run_id,
        "official_algorithm",
        {"axis_frequency_hz": np.array([1.0, 2.0]), "psd": np.array([[1.0, np.nan]])},
        "V^2/Hz",
    )
    queue.repository.update_status(run.run_id, RunStatus.RUNNING)
    queue.repository.update_status(run.run_id, RunStatus.COMPLETED, actual_range={"start_s": 0.0, "end_s": 4.0}, result_summary={
        "structured": {
            "output": {"id": "psd", "label": "功率谱密度", "kind": "frequency_series", "mode": "dynamic"},
            "channel_order": ["Fz"], "requested_range": {"start_s": 0.0, "end_s": 4.0},
            "actual_range": {"start_s": 0.0, "end_s": 4.0},
            "axes": {"frequency_hz": {"array_key": "axis_frequency_hz", "unit": "Hz", "length": 2}},
            "arrays": {"psd": {"unit": "V^2/Hz", "shape": [1, 2]}},
            "windows": [], "window_state_counts": {"Complete": 1}, "quality": "clean",
        },
    })
    service = ResultService(queue, ValidationService(queue.repository.database_path))

    preview = service.structured_preview(run.run_id)

    assert preview["artifact"]["artifact_id"] == artifact.artifact_id
    assert preview["axes"]["frequency_hz"] == [1.0, 2.0]
    assert preview["arrays"]["psd"] == [[1.0, None]]
    (queue.base.artifacts.root / artifact.relative_path).write_bytes(b"corrupt")
    with pytest.raises(ArtifactIntegrityError):
        service.structured_preview(run.run_id)


def test_result_export_includes_persisted_independent_reference_evidence(tmp_path):
    queue, recording_id = _queue(tmp_path)
    run = queue.enqueue(_request(recording_id))
    queue.process_next()
    validations = ValidationService(queue.repository.database_path)
    validation = validations.create(
        ValidationCreateRequest(
            kind="independent_scipy_spectral_psd", algorithm_version="offline-spectral-v3",
            dataset_identity={"recording_id": recording_id, "source_sha256": "redacted"},
            config_sha256="reference-config", tolerances={"rtol": 1e-7, "atol": 1e-9},
            expected=[1.0, 2.0], actual=[1.0, 2.0],
        ),
        evidence={
            "schema_version": "independent-spectral-reference-evidence-v1",
            "unit": "uV^2/Hz", "frequencies_hz": [1.0, 1.25],
            "reference_psd": {"F3": [1.0, 2.0]}, "production_psd": {"F3": [1.0, 2.0]},
        },
    )
    package = ResultService(queue, validations).export(run.run_id, validation.validation_id)

    with zipfile.ZipFile(io.BytesIO(package)) as archive:
        report = json.loads(archive.read("validation-report.json"))
    assert report["evidence"]["schema_version"] == "independent-spectral-reference-evidence-v1"
    assert report["evidence"]["reference_psd"]["F3"] == [1.0, 2.0]
