"""Read-only result views and reproducible, artifact-verified exports."""

from __future__ import annotations

import csv
import io
import json
import zipfile
from pathlib import Path

import numpy as np

from app.models.run import AnalysisRun
from app.services.artifacts import ArtifactIntegrityError, WindowIndexUnavailable
from app.core.recording_time import cursor_sample_from_seconds
from app.models.result_window import SavedResultWindow, WindowSelection
from app.services.result_window_index import SavedWindowIndex
from app.services.analysis_provenance import serialize_analysis_run
from app.services.current_algorithm_result import require_current_result
from app.services.run_queue import PersistentRunQueue
from app.services.validations import ValidationService


class ResultExportUnavailable(ValueError):
    """Raised when a Run is not a complete, exportable scientific result."""


class ResultService:
    EXPORT_SCHEMA_VERSION = "research-result-export-v1"

    def __init__(self, runs: PersistentRunQueue, validations: ValidationService):
        self.runs, self.validations = runs, validations

    def view(self, run_id: str) -> dict[str, object]:
        run = self.runs.get(run_id)
        artifacts = self.runs.list_artifacts(run_id)
        return {
            "run": serialize_analysis_run(run),
            "artifacts": [item.model_dump(mode="json") for item in artifacts],
            "data_classification": {"measured_data": False, "algorithm_output": True, "clinical_interpretation": False},
        }

    def structured_preview(self, run_id: str, *, max_cells: int = 100_000) -> dict[str, object]:
        """Return a bounded, backend-produced preview of a structured artifact."""
        if max_cells <= 0 or max_cells > 1_000_000:
            raise ValueError("preview cell limit is outside the allowed range")
        run = self.runs.get(run_id)
        summary = run.result_summary or {}
        require_current_result(run)
        structured = summary.get("structured")
        if not isinstance(structured, dict):
            raise ValueError("run does not contain a structured result")
        artifacts = self.runs.list_artifacts(run_id)
        if not artifacts:
            raise KeyError(run_id)
        artifact = artifacts[0]
        arrays = self.runs.base.artifacts.read_npz(artifact)
        numeric_names = [name for name, value in arrays.items() if np.issubdtype(np.asarray(value).dtype, np.number)]
        numeric_cells = sum(int(np.asarray(arrays[name]).size) for name in numeric_names)
        if numeric_cells > max_cells:
            raise ValueError("structured preview exceeds the requested cell limit")

        preview_arrays = {
            name: self._json_values(arrays[name])
            for name in numeric_names
            if not name.startswith("axis_")
        }
        axes = {
            name: self._json_values(arrays[metadata["array_key"]])
            for name, metadata in (structured.get("axes") or {}).items()
            if metadata.get("array_key") in arrays
        }
        windows = structured.get("windows", [])
        window_evidence = arrays.get("window_evidence_json")
        if window_evidence is not None and isinstance(windows, list):
            raw = np.asarray(window_evidence).item()
            evidence_items = json.loads(str(raw))
            if isinstance(evidence_items, list) and len(evidence_items) == len(windows):
                windows = [
                    {**window, "evidence": evidence}
                    for window, evidence in zip(windows, evidence_items, strict=True)
                ]
        return {
            "run_id": run.run_id,
            "artifact": artifact.model_dump(mode="json"),
            "output": structured.get("output"),
            "channel_order": structured.get("channel_order", []),
            "requested_range": structured.get("requested_range"),
            "actual_range": structured.get("actual_range"),
            "axes": axes,
            "axis_metadata": structured.get("axes", {}),
            "arrays": preview_arrays,
            "array_metadata": structured.get("arrays", {}),
            "windows": windows,
            "window_state_counts": structured.get("window_state_counts", {}),
            "quality": structured.get("quality"),
            "spectral_evidence": structured.get("spectral_evidence", {}),
            "calculation_trace": structured.get("calculation_trace", {}),
            "failure": structured.get("failure"),
            "scientific_version": run.scientific_version,
            "implementation_version": run.implementation_version,
        }

    def window_preview(
        self,
        run_id: str,
        *,
        cursor_sample: int | None = None,
        cursor_s: float | None = None,
    ) -> dict[str, object]:
        """Return one saved dynamic result window; never recompute or read EEG."""
        if (cursor_sample is None) == (cursor_s is None):
            raise ValueError("provide exactly one cursor coordinate")
        run = self.runs.get(run_id)
        structured = (run.result_summary or {}).get("structured")
        require_current_result(run)
        if not isinstance(structured, dict) or not isinstance(structured.get("windows"), list):
            raise ValueError("run does not contain saved dynamic windows")
        recording = self.runs.recordings.require_recording(run.recording_id)
        duration_samples = int(round(float(recording.duration_s) * float(recording.sfreq)))
        resolved_sample = cursor_sample
        if resolved_sample is None:
            resolved_sample = cursor_sample_from_seconds(float(cursor_s), int(round(recording.sfreq)), duration_samples)
        windows = [self._saved_window(item, index) for index, item in enumerate(structured["windows"])]
        selection = SavedWindowIndex(duration_samples, windows).select(resolved_sample)
        response = self._window_selection_response(run, selection, structured, float(recording.sfreq))
        if selection.window is None:
            return response
        artifacts = self.runs.list_artifacts(run_id)
        if not artifacts:
            raise KeyError(run_id)
        artifact = artifacts[0]
        arrays = self.runs.base.artifacts.read_window(artifact, selection.window.window_index)
        axes = self.runs.base.artifacts.read_window_axes(artifact)
        response["artifact"] = artifact.model_dump(mode="json")
        axis_keys = {
            metadata["array_key"]
            for metadata in (structured.get("axes") or {}).values()
        }
        response["axes"] = {
            name: self._json_values(value) for name, value in axes.items()
            if name in axis_keys
        }
        response["arrays"] = {name: self._json_values(value) for name, value in arrays.items()}
        response["array_metadata"] = structured.get("arrays", {})
        response["axis_metadata"] = structured.get("axes", {})
        return response

    @staticmethod
    def _saved_window(raw: object, index: int) -> SavedResultWindow:
        if not isinstance(raw, dict):
            raise ValueError("saved dynamic window metadata is malformed")
        failure = raw.get("failure") or {}
        state = str(raw.get("state", "Unavailable"))
        quality = str(raw.get("quality", "unavailable")).lower()
        usable = state not in {"Rejected", "Unavailable"} and quality not in {"rejected", "unavailable", "failed"}
        reason = str(failure.get("code") or quality or "WINDOW_UNAVAILABLE")
        return SavedResultWindow(
            window_id=str(raw.get("window_id") or f"window-{index}"),
            window_index=index,
            start_sample=int(raw["start_sample"]),
            end_sample=int(raw["end_sample"]),
            window_state="Partial" if state == "Partial" else "Complete",
            quality_status="valid" if usable else ("rejected" if state == "Rejected" else "unavailable"),
            reason_codes=() if usable else (reason,),
            provenance=raw.get("provenance") if isinstance(raw.get("provenance"), dict) else None,
        )

    @staticmethod
    def _window_selection_response(
        run: AnalysisRun,
        selection: WindowSelection,
        structured: dict[str, object],
        sampling_rate_hz: float,
    ) -> dict[str, object]:
        window = selection.window
        return {
            "run_id": run.run_id,
            "recording_id": run.recording_id,
            "cursor_sample": selection.cursor_sample,
            "cursor_s": selection.cursor_sample / sampling_rate_hz,
            "status": selection.status,
            "reason_codes": list(selection.reason_codes),
            "window": window.model_dump(mode="json") if window else None,
            "has_uncovered_tail": selection.has_uncovered_tail,
            "output": structured.get("output"),
            "quality": structured.get("quality"),
            "scientific_version": run.scientific_version,
            "implementation_version": run.implementation_version,
        }

    @staticmethod
    def _json_values(value: np.ndarray) -> object:
        try:
            array = np.asarray(value, dtype=float)
        except (TypeError, ValueError) as exc:
            raise ValueError("result artifact contains non-numeric values") from exc
        if array.ndim == 0:
            scalar = array.item()
            return float(scalar) if np.isfinite(scalar) else None
        finite = np.isfinite(array)
        if array.ndim == 1:
            return [float(item) if finite[index] else None for index, item in enumerate(array)]
        return [ResultService._json_values(item) for item in array]

    def export(self, run_id: str, validation_id: str | None = None) -> bytes:
        run = self.runs.get(run_id)
        require_current_result(run)
        # A queued/running Run is not stable enough to export. Terminal
        # failures and quality-gated results remain exportable as historical
        # evidence with their structured null/error state.
        if run.status.value in {"queued", "running"}:
            raise ResultExportUnavailable(
                f"完整结果尚未就绪：运行状态为 {run.status.value}。"
            )
        if run.status.value == "completed" and run.analysis_type == "official_algorithm" and run.config.get("mode") == "dynamic":
            provenance = (run.result_summary or {}).get("execution_provenance")
            if not isinstance(provenance, dict):
                raise ResultExportUnavailable("动态结果的溯源结构不可验证，不能导出。")
            coverage = provenance.get("coverage")
            if not isinstance(coverage, dict) or coverage.get("state") != "complete":
                raise ResultExportUnavailable("完整结果尚未通过动态窗口覆盖校验。")
            try:
                planned = int(coverage["planned_windows"])
                terminal = int(coverage["terminal_windows"])
            except (KeyError, TypeError, ValueError) as exc:
                raise ResultExportUnavailable("动态结果缺少可验证的窗口覆盖信息。") from exc
            if planned < 0 or terminal != planned:
                raise ResultExportUnavailable("动态结果的窗口覆盖不完整，不能导出。")
        artifacts = self.runs.list_artifacts(run_id)
        validation = self.validations.report(validation_id) if validation_id else None
        manifest = self._manifest(run, artifacts, validation_id)
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            archive.writestr("manifest.json", self._json(manifest))
            archive.writestr("result.json", self._json(self.view(run_id)))
            archive.writestr("summary.csv", self._csv(run))
            if validation is not None:
                archive.writestr("validation-report.json", self._json(validation))
            for artifact in artifacts:
                arrays = self.runs.base.artifacts.read_npz(artifact)
                target = io.BytesIO()
                np.savez_compressed(target, **arrays)
                archive.writestr(f"artifacts/{artifact.artifact_id}.npz", target.getvalue())
        return stream.getvalue()

    def _manifest(self, run: AnalysisRun, artifacts, validation_id: str | None) -> dict[str, object]:
        return {
            "schema_version": self.EXPORT_SCHEMA_VERSION,
            "scope": "research_output_not_clinical_conclusion",
            "run_id": run.run_id,
            "recording_id": run.recording_id,
            "analysis_type": run.analysis_type,
            "execution_mode": run.execution_mode,
            "status": run.status.value,
            "scientific_version": run.scientific_version,
            "implementation_version": run.implementation_version,
            "definition_id": run.definition_id,
            "definition_version": run.definition_version,
            "config_sha256": run.config_sha256,
            "cache_key": run.cache_key,
            "requested_range": run.requested_range,
            "actual_range": run.actual_range,
            "reference": run.reference,
            "filters": run.filters,
            "window": run.window,
            "quality_rules": run.quality_rules,
            "quality_or_error": run.error.model_dump(mode="json") if run.error else None,
            "validation_id": validation_id,
            "artifacts": [{"artifact_id": item.artifact_id, "sha256": item.sha256, "unit": item.unit, "shape": item.shape} for item in artifacts],
        }

    @staticmethod
    def _json(value: object) -> str:
        return json.dumps(value, ensure_ascii=False, sort_keys=True, indent=2, allow_nan=False)

    @staticmethod
    def _csv(run: AnalysisRun) -> str:
        buffer = io.StringIO()
        writer = csv.writer(buffer)
        writer.writerow(["field", "value"])
        writer.writerow(["run_id", run.run_id])
        writer.writerow(["status", run.status.value])
        for key, value in (run.result_summary or {}).items():
            if isinstance(value, (str, int, float, bool)) or value is None:
                writer.writerow([key, "" if value is None else value])
        return buffer.getvalue()
