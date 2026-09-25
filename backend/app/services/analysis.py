import json
import sqlite3
from pathlib import Path
from typing import Any

from app.core.config import DATABASE_PATH
from app.models.analysis import AnalysisSummary
from app.scientific.contracts.analysis import ANALYSIS_ALGORITHM_VERSION
from app.models.run import RunCreateRequest, RunStatus
from app.persistence import connect_database
from app.services.recordings import RecordingService
from app.services.runs import RunService


class AnalysisService:
    """Compatibility facade over the traceable AnalysisRun workflow.

    The legacy analysis URLs are kept for existing clients, but new executions
    are owned by RunService so provenance, artifacts, caching and status
    transitions have one source of truth.
    """

    def __init__(
        self,
        recordings: RecordingService,
        database_path: Path = DATABASE_PATH,
        run_service: Any | None = None,
    ):
        self.recordings = recordings
        self.database_path = Path(database_path)
        self.run_service = run_service or RunService(recordings, self.database_path)

    def _connect(self) -> sqlite3.Connection:
        return connect_database(self.database_path)

    def create_analysis(self, recording_id: str) -> AnalysisSummary:
        self.recordings.require_recording(recording_id)
        raise RuntimeError("LEGACY_ANALYSIS_RETIRED")

    def get_result(self, analysis_id: str) -> dict:
        # New legacy-compatible analyses are AnalysisRuns.  Keep the old table
        # as a read-only fallback for records created before this migration.
        try:
            run = self.run_service.get(analysis_id)
        except KeyError:
            run = None
        if run is not None:
            if run.analysis_type != "legacy_analysis":
                raise KeyError("分析结果不存在")
            if run.result_summary is None:
                raise ValueError("分析尚未生成可读取结果")
            result = dict(run.result_summary)
            result.setdefault("algorithm_contract", {"algorithm_version": "legacy-unversioned"})
            result.setdefault("faa", {"faa": None, "reason": "legacy_result_unavailable", "scope": "unknown"})
            return {"analysis_id": analysis_id, "recording_id": run.recording_id, "status": run.status.value, **result}

        return self._get_historical_result(analysis_id)

    def _get_historical_result(self, analysis_id: str) -> dict:
        """Read pre-Run records without allowing new writes to ``analyses``."""
        with self._connect() as connection:
            row = connection.execute("SELECT * FROM analyses WHERE analysis_id = ?", (analysis_id,)).fetchone()
        if row is None:
            raise KeyError("分析结果不存在")
        result = json.loads(row["result_json"])
        result.setdefault("algorithm_contract", {"algorithm_version": "legacy-unversioned"})
        result.setdefault("faa", {"faa": None, "reason": "legacy_result_unavailable", "scope": "unknown"})
        return {"analysis_id": analysis_id, "recording_id": row["recording_id"], "status": row["status"], **result}
