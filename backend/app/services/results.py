"""Read-only result views and reproducible, artifact-verified exports."""

from __future__ import annotations

import csv
import io
import json
import zipfile
from pathlib import Path

import numpy as np

from app.models.run import AnalysisRun
from app.services.artifacts import ArtifactIntegrityError
from app.services.run_queue import PersistentRunQueue
from app.services.validations import ValidationService


class ResultService:
    EXPORT_SCHEMA_VERSION = "research-result-export-v1"

    def __init__(self, runs: PersistentRunQueue, validations: ValidationService):
        self.runs, self.validations = runs, validations

    def view(self, run_id: str) -> dict[str, object]:
        run = self.runs.get(run_id)
        artifacts = self.runs.list_artifacts(run_id)
        return {
            "run": run.model_dump(mode="json"),
            "artifacts": [item.model_dump(mode="json") for item in artifacts],
            "data_classification": {"measured_data": False, "algorithm_output": True, "clinical_interpretation": False},
        }

    def export(self, run_id: str, validation_id: str | None = None) -> bytes:
        run = self.runs.get(run_id)
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
