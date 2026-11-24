import io
import json
import sqlite3
import zipfile

import numpy as np
import pytest

from app.services.artifacts import ArtifactIntegrityError
from app.models.run import RunCreateRequest, RunStatus, ValidationCreateRequest
from app.services.results import ResultService
from app.services.results import ResultExportUnavailable
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


def test_result_export_rejects_incomplete_run_even_when_a_run_exists(tmp_path):
    queue, recording_id = _queue(tmp_path)
    queued = queue.enqueue(_request(recording_id))
    service = ResultService(queue, ValidationService(queue.repository.database_path))

    with pytest.raises(ResultExportUnavailable, match="完整结果尚未就绪"):
        service.export(queued.run_id)


def test_result_export_keeps_gate_failed_result_explicit(tmp_path):
    queue, recording_id = _queue(tmp_path)
    run = queue.enqueue(_request(recording_id))
    queue.repository.update_status(run.run_id, RunStatus.RUNNING)
    queue.repository.update_status(
        run.run_id,
        RunStatus.GATE_FAILED,
        result_summary=None,
    )

    package = ResultService(queue, ValidationService(queue.repository.database_path)).export(run.run_id)

    assert package.startswith(b"PK")


def test_result_export_rejects_new_dynamic_run_with_incomplete_coverage(tmp_path):
    queue, recording_id = _queue(tmp_path)
    run = queue.enqueue(RunCreateRequest(
        recording_id=recording_id,
        analysis_type="official_algorithm",
        config={
            "algorithm_id": "peak_frequency",
            "channel": "F3",
            "mode": "dynamic",
            "time": {"start_s": 5, "end_s": 10},
            "dynamic_window_s": 5,
            "refresh_step_s": 1,
            "low_hz": 1.0,
            "high_hz": 30.0,
        },
    ))
    completed = queue.process_next()
    assert completed.status is RunStatus.COMPLETED
    summary = completed.result_summary
    summary["execution_provenance"]["coverage"] = {
        "state": "partial", "planned_windows": 1, "terminal_windows": 0,
    }
    # Corrupt only coverage in an otherwise valid current result. Terminal Runs
    # cannot be rewritten through the lifecycle API.
    with sqlite3.connect(queue.repository.database_path) as connection:
        connection.execute("UPDATE analysis_runs SET result_summary_json = ? WHERE run_id = ?",
                           (json.dumps(summary), run.run_id))

    with pytest.raises(ResultExportUnavailable, match="覆盖校验"):
        ResultService(queue, ValidationService(queue.repository.database_path)).export(run.run_id)


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


def test_structured_preview_attaches_per_window_channel_failure_evidence(tmp_path):
    queue, recording_id = _queue(tmp_path)
    run = queue.enqueue(_request(recording_id))
    window_evidence = [{"extensions": {"channel_failures": {"by_channel": {
        "F4-Ref": {"code": "PSD_QUALITY_GATE_FAILED", "message": "quality gate"},
    }}}}]
    queue.base.artifacts.write_npz(
        run.run_id,
        "official_algorithm",
        {"axis_time_s": np.array([4.0]), "window_evidence_json": np.asarray(json.dumps(window_evidence))},
        "ratio",
    )
    queue.repository.update_status(run.run_id, RunStatus.RUNNING)
    queue.repository.update_status(run.run_id, RunStatus.COMPLETED,
        actual_range={"start_s": 0.0, "end_s": 4.0}, result_summary={"structured": {
            "output": {"id": "rbp", "label": "RBP", "kind": "algorithm_group", "mode": "dynamic"},
            "channel_order": ["F3-Ref", "F4-Ref"],
            "requested_range": {"start_s": 0.0, "end_s": 4.0},
            "actual_range": {"start_s": 0.0, "end_s": 4.0},
            "axes": {"time_s": {"array_key": "axis_time_s", "unit": "s", "length": 1}},
            "arrays": {"window_evidence_json": {"unit": "json", "shape": []}},
            "windows": [{"start_sample": 0, "end_sample": 400, "start_s": 0.0, "end_s": 4.0,
                         "state": "Partial", "quality": "partial"}],
            "window_state_counts": {"Partial": 1}, "quality": "partial",
        }})

    preview = ResultService(queue, ValidationService(queue.repository.database_path)).structured_preview(run.run_id)

    assert preview["windows"][0]["evidence"] == window_evidence[0]


def test_window_preview_selects_saved_window_and_returns_derived_chunk(tmp_path):
    queue, recording_id = _queue(tmp_path)
    run = queue.enqueue(_request(recording_id))
    artifact = queue.base.artifacts.write_npz(
        run.run_id,
        "official_algorithm",
        {"axis_frequency_hz": np.array([1.0, 2.0]), "psd": np.array([[1.0, 2.0], [3.0, 4.0]])},
        "V^2/Hz",
        window_ranges=[
            {"start_sample": 0, "end_sample": 400},
            {"start_sample": 200, "end_sample": 600},
        ],
    )
    queue.repository.update_status(run.run_id, RunStatus.RUNNING)
    queue.repository.update_status(run.run_id, RunStatus.COMPLETED, actual_range={"start_s": 0.0, "end_s": 8.0}, result_summary={
        "structured": {
            "output": {"id": "psd", "kind": "frequency_series"},
            "windows": [
                {"start_sample": 0, "end_sample": 400, "start_s": 0, "end_s": 4, "state": "Complete", "quality": "clean"},
                {"start_sample": 200, "end_sample": 600, "start_s": 2, "end_s": 6, "state": "Complete", "quality": "clean",
                 "provenance": {"preprocessing_scope": "whole_recording_before_window_slice", "evidence": "backend-preserved"}},
            ],
            "axes": {"frequency_hz": {"array_key": "axis_frequency_hz", "unit": "Hz"}},
            "arrays": {"psd": {"unit": "V^2/Hz"}},
        },
    })
    service = ResultService(queue, ValidationService(queue.repository.database_path))

    preview = service.window_preview(run.run_id, cursor_sample=600)

    assert preview["status"] == "available"
    assert preview["window"]["window_index"] == 1
    assert preview["arrays"]["psd"] == [3.0, 4.0]
    assert preview["axes"]["axis_frequency_hz"] == [1.0, 2.0]
    assert preview["artifact"]["artifact_id"] == artifact.artifact_id
    assert preview["window"]["provenance"] == {
        "preprocessing_scope": "whole_recording_before_window_slice", "evidence": "backend-preserved",
    }


def test_window_preview_serializes_object_dtype_numeric_axes(tmp_path, monkeypatch):
    queue, recording_id = _queue(tmp_path)
    run = queue.enqueue(_request(recording_id))
    artifact = queue.base.artifacts.write_npz(
        run.run_id,
        "official_algorithm",
        {"axis_frequency_hz": np.array([1.0, 2.0]), "psd": np.array([[1.0, np.nan]])},
        "V^2/Hz",
        window_ranges=[{"start_sample": 0, "end_sample": 400}],
    )
    queue.repository.update_status(run.run_id, RunStatus.RUNNING)
    queue.repository.update_status(run.run_id, RunStatus.COMPLETED, result_summary={
        "structured": {
            "output": {"id": "psd", "kind": "frequency_series"},
            "windows": [{"start_sample": 0, "end_sample": 400, "state": "Complete", "quality": "clean"}],
            "axes": {"frequency_hz": {"array_key": "axis_frequency_hz", "unit": "Hz"}},
            "arrays": {"psd": {"unit": "V^2/Hz"}},
        },
    })

    monkeypatch.setattr(
        queue.base.artifacts,
        "read_window",
        lambda _artifact, _index: {"psd": np.array([1.0, np.nan], dtype=object)},
    )
    monkeypatch.setattr(
        queue.base.artifacts,
        "read_window_axes",
        lambda _artifact: {"axis_frequency_hz": np.array([1.0, 2.0], dtype=object)},
    )

    preview = ResultService(queue, ValidationService(queue.repository.database_path)).window_preview(run.run_id, cursor_sample=400)

    assert preview["axes"]["axis_frequency_hz"] == [1.0, 2.0]
    assert preview["arrays"]["psd"] == [1.0, None]


def test_window_preview_excludes_static_json_evidence(tmp_path):
    queue, recording_id = _queue(tmp_path)
    run = queue.enqueue(_request(recording_id))
    queue.base.artifacts.write_npz(
        run.run_id, "official_algorithm",
        {
            "axis_frequency_hz": np.array([1.0, 2.0]),
            "psd": np.array([[1.0, 2.0]]),
            "window_evidence_json": np.asarray('[{"quality": "clean"}]'),
        },
        "V^2/Hz", window_ranges=[{"start_sample": 0, "end_sample": 400}],
    )
    queue.repository.update_status(run.run_id, RunStatus.RUNNING)
    queue.repository.update_status(run.run_id, RunStatus.COMPLETED, result_summary={
        "structured": {
            "output": {"id": "psd", "kind": "frequency_series"},
            "windows": [{"start_sample": 0, "end_sample": 400, "state": "Complete", "quality": "clean"}],
            "axes": {"frequency_hz": {"array_key": "axis_frequency_hz", "unit": "Hz"}},
            "arrays": {"psd": {"unit": "V^2/Hz"}, "window_evidence_json": {"unit": "json"}},
        },
    })

    preview = ResultService(queue, ValidationService(queue.repository.database_path)).window_preview(
        run.run_id, cursor_sample=400,
    )

    assert preview["axes"] == {"axis_frequency_hz": [1.0, 2.0]}
    assert preview["arrays"]["psd"] == [1.0, 2.0]


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
