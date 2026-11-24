"""Bounded EDF/BDF staging and atomic publication into the recording catalog."""

from __future__ import annotations

import hashlib
import json
import math
import os
import sqlite3
from collections.abc import AsyncIterable, Awaitable, Callable
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from uuid import uuid4

from app.models.recording import RecordingSummary
from app.persistence import connect_database
from app.services.recording_identity import RECORDING_IMPORT_VERSION, canonical_channel_label
from app.services.recordings import RecordingService

CHUNK_BYTES = 1024 * 1024


class RecordingImportError(ValueError):
    def __init__(self, code: str, message: str, status_code: int = 422):
        super().__init__(message)
        self.code = code
        self.status_code = status_code


@dataclass(frozen=True)
class StagedRecordingImport:
    path: Path
    original_name: str
    extension: str
    sha256: str
    byte_length: int


@dataclass(frozen=True)
class ImportedProjectRecording:
    recording: RecordingSummary
    session_id: str
    subject_id: str
    project_id: str


class EdfBdfImportService:
    def __init__(self, recordings: RecordingService):
        self.recordings = recordings
        self.storage_dir = recordings.storage_dir
        self.database_path = recordings.database_path

    async def stage_upload(
        self,
        filename: str,
        chunks: AsyncIterable[bytes],
        check_cancelled: Callable[[], Awaitable[None]],
    ) -> StagedRecordingImport:
        original_name = (filename or "").replace("\\", "/").rsplit("/", 1)[-1]
        extension = Path(original_name).suffix.lower()
        if extension not in {".edf", ".bdf"}:
            raise RecordingImportError("RECORDING_IMPORT_FORMAT_UNSUPPORTED", "仅支持 EDF 或 BDF 文件。")
        stage_path = self.storage_dir / f".import-{uuid4().hex}.part{extension}"
        digest = hashlib.sha256()
        byte_length = 0
        try:
            with stage_path.open("xb") as target:
                async for chunk in chunks:
                    await check_cancelled()
                    if not isinstance(chunk, bytes):
                        raise RecordingImportError("RECORDING_IMPORT_CHUNK_INVALID", "上传数据块格式无效。")
                    if chunk:
                        target.write(chunk)
                        digest.update(chunk)
                        byte_length += len(chunk)
                await check_cancelled()
                target.flush()
                os.fsync(target.fileno())
            if byte_length == 0:
                raise RecordingImportError("RECORDING_IMPORT_EMPTY", "录制文件不能为空。")
            return StagedRecordingImport(stage_path, original_name, extension, digest.hexdigest(), byte_length)
        except BaseException:
            stage_path.unlink(missing_ok=True)
            raise

    def publish_standalone(self, staged: StagedRecordingImport, check_cancelled: Callable[[], None] = lambda: None) -> RecordingSummary:
        return self._publish(staged, None, check_cancelled)

    def publish_project(
        self,
        staged: StagedRecordingImport,
        project_id: str,
        *,
        subject_id: str | None = None,
        new_subject_local_code: str | None = None,
        check_cancelled: Callable[[], None] = lambda: None,
    ) -> ImportedProjectRecording:
        if (subject_id is None) == (new_subject_local_code is None):
            raise RecordingImportError("PROJECT_SUBJECT_REQUIRED", "请选择已有受试者或输入新的受试者编号。", 422)
        metadata = self._inspect(staged.path)
        check_cancelled()
        recording_id, stored_name, created_at, session_id = uuid4().hex, f"{uuid4().hex}{staged.extension}", _utc_now(), uuid4().hex
        destination = self.storage_dir / stored_name
        os.replace(staged.path, destination)
        subject_code = new_subject_local_code.strip() if new_subject_local_code is not None else None
        try:
            with connect_database(self.database_path, foreign_keys=True) as connection:
                if connection.execute("SELECT 1 FROM projects WHERE project_id = ?", (project_id,)).fetchone() is None:
                    raise RecordingImportError("PROJECT_NOT_FOUND", "目标项目不存在。", 404)
                if subject_id is not None:
                    subject = connection.execute(
                        "SELECT subject_id FROM subjects WHERE subject_id = ? AND project_id = ?", (subject_id, project_id)
                    ).fetchone()
                    if subject is None:
                        raise RecordingImportError("SUBJECT_PROJECT_MISMATCH", "受试者不属于当前项目。", 409)
                else:
                    if not subject_code:
                        raise RecordingImportError("SUBJECT_CODE_INVALID", "受试者编号不能为空。", 422)
                    subject_id = uuid4().hex
                    connection.execute("INSERT INTO subjects VALUES (?, ?, ?, ?)",
                                       (subject_id, project_id, subject_code, created_at))
                self._insert_recording(connection, recording_id, stored_name, staged, created_at, metadata)
                connection.execute("INSERT INTO sessions VALUES (?, ?, ?, ?, NULL, '', ?)",
                                   (session_id, project_id, subject_id, recording_id, created_at))
                connection.execute("UPDATE projects SET updated_at = ? WHERE project_id = ?", (created_at, project_id))
                check_cancelled()
        except sqlite3.IntegrityError as exc:
            destination.unlink(missing_ok=True)
            raise RecordingImportError("SUBJECT_CODE_CONFLICT", "受试者编号已在当前项目中使用。", 409) from exc
        except BaseException:
            destination.unlink(missing_ok=True)
            raise
        if subject_id is None:
            raise AssertionError("validated project import must have a subject")
        return ImportedProjectRecording(self.recordings.require_recording(recording_id), session_id, subject_id, project_id)

    def _publish(self, staged: StagedRecordingImport, project_id: str | None,
                 check_cancelled: Callable[[], None]) -> RecordingSummary:
        if project_id is not None:
            raise AssertionError("project imports must create their Session atomically")
        metadata = self._inspect(staged.path)
        check_cancelled()
        recording_id, stored_name, created_at = uuid4().hex, f"{uuid4().hex}{staged.extension}", _utc_now()
        destination = self.storage_dir / stored_name
        os.replace(staged.path, destination)
        try:
            with connect_database(self.database_path, foreign_keys=True) as connection:
                self._insert_recording(connection, recording_id, stored_name, staged, created_at, metadata)
                check_cancelled()
        except BaseException:
            destination.unlink(missing_ok=True)
            raise
        return self.recordings.require_recording(recording_id)

    def _inspect(self, path: Path) -> tuple[float, float, list[str], list[str], list[str], list[str]]:
        try:
            sfreq, duration_s, channels, channel_types, channel_units = self.recordings._read_metadata(path)
        except Exception as exc:
            raise RecordingImportError("RECORDING_IMPORT_INVALID", "无法读取 BDF/EDF 脑电文件。") from exc
        canonical = [canonical_channel_label(name) for name in channels]
        if (not math.isfinite(sfreq) or sfreq <= 0 or not math.isfinite(duration_s) or duration_s <= 0
                or not channels or len(channels) != len(channel_types) or len(channels) != len(channel_units)
                or any(not name for name in canonical) or len(set(name.casefold() for name in canonical)) != len(canonical)):
            raise RecordingImportError("RECORDING_CHANNEL_METADATA_INVALID", "EDF/BDF 通道顺序或元数据无效。")
        return sfreq, duration_s, channels, channel_types, channel_units, canonical

    @staticmethod
    def _insert_recording(connection, recording_id, stored_name, staged, created_at, metadata) -> None:
        sfreq, duration_s, channels, channel_types, channel_units, canonical = metadata
        connection.execute(
            """INSERT INTO recordings (id, original_name, stored_name, extension, created_at, sfreq, duration_s,
               channels_json, source_sha256, file_size_bytes, raw_channel_labels_json, canonical_channel_labels_json,
               channel_types_json, channel_units_json, import_version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
            (recording_id, staged.original_name, stored_name, staged.extension, created_at, sfreq, duration_s,
             json.dumps(channels, ensure_ascii=False), staged.sha256, staged.byte_length,
             json.dumps(channels, ensure_ascii=False), json.dumps(canonical, ensure_ascii=False),
             json.dumps(channel_types, ensure_ascii=False), json.dumps(channel_units, ensure_ascii=False), RECORDING_IMPORT_VERSION),
        )


def _utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()
