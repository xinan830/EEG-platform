import json
import sqlite3
from pathlib import Path
from uuid import uuid4

from app.core.config import DATABASE_PATH
from app.models.analysis import AnalysisSummary
from app.processing.offline_analysis import analyze_recording
from app.services.recordings import RecordingService


class AnalysisService:
    def __init__(self, recordings: RecordingService, database_path: Path = DATABASE_PATH):
        self.recordings = recordings
        self.database_path = Path(database_path)
        self._initialize_database()

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self.database_path)
        connection.row_factory = sqlite3.Row
        return connection

    def _initialize_database(self) -> None:
        with self._connect() as connection:
            connection.execute(
                """
                CREATE TABLE IF NOT EXISTS analyses (
                    analysis_id TEXT PRIMARY KEY,
                    recording_id TEXT NOT NULL,
                    status TEXT NOT NULL,
                    result_json TEXT NOT NULL
                )
                """
            )

    def create_analysis(self, recording_id: str) -> AnalysisSummary:
        recording = self.recordings.require_recording(recording_id)
        if recording.mapping is None:
            raise RuntimeError("请先保存 Fz、Pz、Oz 通道映射")
        data, sfreq, channel_names, events = self.recordings.load_data(recording)
        result = analyze_recording(data, sfreq, recording.mapping, channel_names, events)
        analysis_id = uuid4().hex
        result_json = json.dumps(result.to_dict(), ensure_ascii=False, allow_nan=False)
        with self._connect() as connection:
            connection.execute(
                "INSERT INTO analyses (analysis_id, recording_id, status, result_json) VALUES (?, ?, ?, ?)",
                (analysis_id, recording_id, "completed", result_json),
            )
        return AnalysisSummary(
            analysis_id=analysis_id,
            recording_id=recording_id,
            status="completed",
            result_url=f"/api/analyses/{analysis_id}",
            locked_iapf=result.locked_iapf,
        )

    def get_result(self, analysis_id: str) -> dict:
        with self._connect() as connection:
            row = connection.execute("SELECT * FROM analyses WHERE analysis_id = ?", (analysis_id,)).fetchone()
        if row is None:
            raise KeyError("分析结果不存在")
        result = json.loads(row["result_json"])
        return {"analysis_id": analysis_id, "recording_id": row["recording_id"], "status": row["status"], **result}
