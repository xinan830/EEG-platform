"""Persistent engineering validation calculations and report export."""

from __future__ import annotations

from pathlib import Path
from uuid import uuid4

import numpy as np

from app.core.config import DATABASE_PATH
from app.core.provenance import canonical_json, execution_environment
from app.models.run import ValidationCreateRequest, ValidationRun
from app.services.run_repository import ValidationRepository, utc_now


class ValidationService:
    REPORT_SCHEMA_VERSION = "engineering-validation-report-v1"

    def __init__(self, database_path: Path = DATABASE_PATH):
        self.repository = ValidationRepository(database_path)

    def create(self, request: ValidationCreateRequest, *, evidence: dict[str, object] | None = None) -> ValidationRun:
        expected = np.asarray(request.expected, dtype=float)
        actual = np.asarray(request.actual, dtype=float)
        if expected.ndim != 1 or actual.ndim != 1 or len(expected) == 0 or expected.shape != actual.shape:
            raise ValueError("expected and actual must be non-empty one-dimensional arrays of equal length")
        if not np.isfinite(expected).all() or not np.isfinite(actual).all():
            raise ValueError("validation arrays must contain only finite values")
        rtol = float(request.tolerances.get("rtol", 0.0))
        atol = float(request.tolerances.get("atol", 0.0))
        if rtol < 0 or atol < 0:
            raise ValueError("validation tolerances must be non-negative")
        if evidence is not None:
            if not isinstance(evidence, dict):
                raise ValueError("validation evidence must be an object")
            try:
                canonical_json(evidence)
            except (TypeError, ValueError) as exc:
                raise ValueError("validation evidence must be finite JSON") from exc
        absolute = np.abs(actual - expected)
        denominator = np.maximum(np.maximum(np.abs(actual), np.abs(expected)), np.finfo(float).tiny)
        relative = absolute / denominator
        passing = absolute <= atol + rtol * np.abs(expected)
        now = utc_now()
        validation = ValidationRun(
            validation_id=uuid4().hex,
            kind=request.kind,
            status="completed",
            subject_run_id=request.subject_run_id,
            algorithm_id=request.algorithm_id,
            algorithm_version=request.algorithm_version,
            dataset_identity=request.dataset_identity,
            config_sha256=request.config_sha256,
            tolerances={"rtol": rtol, "atol": atol},
            expected_summary=self._summary(expected),
            actual_summary=self._summary(actual),
            max_absolute_error=float(np.max(absolute)),
            max_relative_error=float(np.max(relative)),
            point_count=len(expected),
            passed_point_count=int(np.count_nonzero(passing)),
            pass_rate=float(np.mean(passing)),
            passed=bool(np.all(passing)),
            environment=execution_environment(),
            evidence=evidence,
            created_at=now,
            completed_at=now,
        )
        self.repository.create(validation)
        return validation

    def get(self, validation_id: str) -> ValidationRun:
        validation = self.repository.get(validation_id)
        if validation is None:
            raise KeyError("validation not found")
        return validation

    def list(self, limit: int = 100) -> list[ValidationRun]:
        return self.repository.list(limit)

    def report(self, validation_id: str) -> dict[str, object]:
        validation = self.get(validation_id)
        return {
            "report_schema_version": self.REPORT_SCHEMA_VERSION,
            "scope": "engineering_validation_only_not_clinical_validation",
            "validation": validation.model_dump(mode="json"),
            "evidence": validation.evidence,
            "interpretation": (
                "PASS means numerical agreement under the declared engineering tolerance; "
                "it is not evidence of clinical validity."
            ),
        }

    @staticmethod
    def _summary(values: np.ndarray) -> dict[str, float | int]:
        return {
            "count": int(len(values)),
            "minimum": float(np.min(values)),
            "maximum": float(np.max(values)),
            "mean": float(np.mean(values)),
        }
