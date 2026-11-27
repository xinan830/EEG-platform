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
from app.persistence.repositories.recording import RecordingRepository
from app.services.recordings.recording_identity import RECORDING_IMPORT_VERSION, canonical_channel_label
from app.services.recordings.recording_samples import read_edf_bdf_metadata
from app.services.recordings.recordings import RecordingService
from app.services.workspace.research_subjects import SubjectBindingError, resolve_subject, validate_recording_age

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
    subject_id: str | None
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

    def publish_standalone(
        self,
        staged: StagedRecordingImport,
        check_cancelled: Callable[[], None] = lambda: None,
        channel_configuration_snapshot_json: str | None = None,
        export_channel_identity_json: str | None = None,
    ) -> RecordingSummary:
        metadata = self._inspect(staged.path, channel_configuration_snapshot_json,
                                 export_channel_identity_json, staged.sha256)
        check_cancelled()
        recording_id, stored_name, created_at = uuid4().hex, f"{uuid4().hex}{staged.extension}", _utc_now()
        recording = self._imported_summary(recording_id, stored_name, staged, created_at, metadata)
        destination = self.storage_dir / stored_name
        os.replace(staged.path, destination)
        try:
            with connect_database(self.database_path, foreign_keys=True) as connection:
                RecordingRepository.insert_with_metadata(connection, recording)
                check_cancelled()
        except BaseException:
            destination.unlink(missing_ok=True)
            raise
        return self.recordings.require_recording(recording_id)

    def publish_project(
        self,
        staged: StagedRecordingImport,
        project_id: str,
        *,
        subject_id: str | None = None,
        new_subject_local_code: str | None = None,
        new_subject_name: str | None = None,
        new_subject_sex: str | None = None,
        age_at_recording_years: int | None = None,
        channel_configuration_snapshot_json: str | None = None,
        export_channel_identity_json: str | None = None,
        check_cancelled: Callable[[], None] = lambda: None,
    ) -> ImportedProjectRecording:
        metadata = self._inspect(staged.path, channel_configuration_snapshot_json,
                                 export_channel_identity_json, staged.sha256)
        check_cancelled()
        recording_id, stored_name, created_at, session_id = uuid4().hex, f"{uuid4().hex}{staged.extension}", _utc_now(), uuid4().hex
        recording = self._imported_summary(recording_id, stored_name, staged, created_at, metadata)
        destination = self.storage_dir / stored_name
        os.replace(staged.path, destination)
        subject_code = new_subject_local_code.strip() if new_subject_local_code is not None else None
        try:
            with connect_database(self.database_path, foreign_keys=True) as connection:
                validate_recording_age(age_at_recording_years)
                if connection.execute("SELECT 1 FROM projects WHERE project_id = ?", (project_id,)).fetchone() is None:
                    raise RecordingImportError("PROJECT_NOT_FOUND", "目标项目不存在。", 404)
                subject_id = resolve_subject(connection, project_id, created_at, subject_id=subject_id,
                                             code=subject_code, name=new_subject_name, sex=new_subject_sex)
                RecordingRepository.insert_with_metadata(connection, recording)
                connection.execute("INSERT INTO sessions(session_id, project_id, subject_id, recording_id, condition_id, label, created_at, age_at_recording_years) VALUES (?, ?, ?, ?, NULL, '', ?, ?)",
                                   (session_id, project_id, subject_id, recording_id, created_at, age_at_recording_years))
                connection.execute("UPDATE projects SET updated_at = ? WHERE project_id = ?", (created_at, project_id))
                check_cancelled()
        except SubjectBindingError as exc:
            destination.unlink(missing_ok=True)
            raise RecordingImportError(exc.code, str(exc), exc.status_code) from exc
        except sqlite3.IntegrityError as exc:
            destination.unlink(missing_ok=True)
            raise RecordingImportError("SUBJECT_CODE_CONFLICT", "受试者编号已在当前项目中使用。", 409) from exc
        except BaseException:
            destination.unlink(missing_ok=True)
            raise
        return ImportedProjectRecording(self.recordings.require_recording(recording_id), session_id, subject_id, project_id)

    def _inspect(self, path: Path, channel_configuration_snapshot_json: str | None = None,
                 export_channel_identity_json: str | None = None, source_sha256: str | None = None):
        try:
            sfreq, duration_s, channels, channel_types, channel_units = read_edf_bdf_metadata(path)
        except Exception as exc:
            raise RecordingImportError("RECORDING_IMPORT_INVALID", "无法读取 BDF/EDF 脑电文件。") from exc
        raw_channels = channels
        if (not math.isfinite(sfreq) or sfreq <= 0 or not math.isfinite(duration_s) or duration_s <= 0
                or not channels or len(channels) != len(channel_types) or len(channels) != len(channel_units)
                or any(not name for name in channels)):
            raise RecordingImportError("RECORDING_CHANNEL_METADATA_INVALID", "EDF/BDF 通道顺序或元数据无效。")
        acquisition_snapshot = montage_snapshot = montage_catalogue = None
        if export_channel_identity_json is not None:
            try:
                entries = json.loads(export_channel_identity_json)["channels"]
                if len(entries) != len(channels):
                    raise ValueError
                for column, entry in enumerate(entries):
                    if str(entry.get("kind", "")).casefold() not in {"1", "2", "reference", "bipolar"}:
                        role = entry.get("physiological_identity", "unconfirmed")
                        channel_types[column] = role if role in {"eog", "ecg"} else "misc"
            except (ValueError, KeyError, TypeError, AttributeError) as exc:
                raise RecordingImportError("RECORDING_EXPORT_METADATA_INVALID", "导出通道身份或数量无效。") from exc
            (snapshot, column_mapping, acquisition_snapshot, montage_snapshot,
             montage_catalogue) = _bind_export_channel_identity(
                export_channel_identity_json, source_sha256, channels, channel_types
            )
        else:
            snapshot, column_mapping = _bind_channel_configuration(
                channel_configuration_snapshot_json, channels, channel_types
            )
        display_channels = list(raw_channels)
        if snapshot is not None:
            from app.services.recordings.recording_channel_roles import validated_import_identities
            try:
                validated_import_identities(json.loads(snapshot), raw_channels, channel_units)
            except ValueError as exc:
                raise RecordingImportError("CHANNEL_IDENTITY_INVALID", str(exc)) from exc
            from app.scientific.electrode_geometry import geometry_from_channel_snapshot
            try:
                geometry_from_channel_snapshot(json.loads(snapshot))
            except ValueError as exc:
                raise RecordingImportError("ELECTRODE_GEOMETRY_INVALID", str(exc)) from exc
        if snapshot is not None and column_mapping is not None:
            configured = json.loads(snapshot)["Channels"]
            configured_by_index = {item["NativeChannelIndex"]: item["ElectrodeLabel"].strip() for item in configured}
            for item in json.loads(column_mapping):
                configured_label = configured_by_index[item["native_channel_index"]]
                if configured_label:
                    display_channels[item["source_column"]] = configured_label
        canonical = [canonical_channel_label(name) for name in display_channels]
        if any(not name for name in canonical) or len(set(name.casefold() for name in canonical)) != len(canonical):
            raise RecordingImportError("RECORDING_CHANNEL_METADATA_INVALID", "EDF/BDF 通道映射产生了重复或无效标签。")
        if montage_snapshot is not None:
            from types import SimpleNamespace
            from app.services.recordings.imported_analysis_signals import read_imported_acquisition_signal_view
            candidate = SimpleNamespace(
                import_channel_configuration_snapshot_json=snapshot,
                import_channel_column_mapping_json=column_mapping,
                import_acquisition_montage_snapshot_json=acquisition_snapshot,
                import_montage_configuration_snapshot_json=montage_snapshot,
                import_montage_catalogue_snapshot_json=montage_catalogue,
                raw_channel_labels=tuple(raw_channels), channels=tuple(display_channels),
                channel_types=tuple(channel_types),
            )
            try:
                read_imported_acquisition_signal_view(candidate)
                profiles = json.loads(montage_catalogue) if montage_catalogue else []
                for profile in profiles:
                    read_imported_acquisition_signal_view(candidate, str(profile["Id"]))
            except (ValueError, KeyError, TypeError, IndexError) as exc:
                raise RecordingImportError("RECORDING_EXPORT_MONTAGE_INVALID", "导出身份信息中的导联快照无法通过通道映射验证。") from exc
        return (sfreq, duration_s, display_channels, raw_channels, channel_types, channel_units,
                canonical, snapshot, column_mapping, acquisition_snapshot, montage_snapshot, montage_catalogue)

    @staticmethod
    def _imported_summary(recording_id: str, stored_name: str, staged: StagedRecordingImport,
                          created_at: str, metadata: tuple) -> RecordingSummary:
        (sfreq, duration_s, channels, raw_channels, channel_types, channel_units, canonical,
         snapshot, mapping, acquisition_snapshot, montage_snapshot, montage_catalogue) = metadata
        return RecordingSummary(
            id=recording_id, original_name=staged.original_name, stored_name=stored_name,
            extension=staged.extension, created_at=created_at, sfreq=sfreq, duration_s=duration_s,
            channels=tuple(channels), source_sha256=staged.sha256, file_size_bytes=staged.byte_length,
            raw_channel_labels=tuple(raw_channels), canonical_channel_labels=tuple(canonical),
            channel_types=tuple(channel_types), channel_units=tuple(channel_units),
            import_version=RECORDING_IMPORT_VERSION,
            import_channel_configuration_snapshot_json=snapshot,
            import_channel_column_mapping_json=mapping,
            import_acquisition_montage_snapshot_json=acquisition_snapshot,
            import_montage_configuration_snapshot_json=montage_snapshot,
            import_montage_catalogue_snapshot_json=montage_catalogue,
        )


def _bind_channel_configuration(snapshot_json: str | None, labels: list[str], channel_types: list[str]):
    if snapshot_json is None:
        return None, None
    if len(snapshot_json) > 65536:
        raise RecordingImportError("CHANNEL_CONFIGURATION_INVALID", "通道配置快照超过允许大小。")
    try:
        snapshot = json.loads(snapshot_json)
        configured = snapshot["Channels"] if isinstance(snapshot, dict) else None
    except (json.JSONDecodeError, KeyError, TypeError):
        raise RecordingImportError("CHANNEL_CONFIGURATION_INVALID", "通道配置快照格式无效。") from None
    if not isinstance(configured, list) or not configured:
        raise RecordingImportError("CHANNEL_CONFIGURATION_INVALID", "通道配置快照没有通道。")
    from app.scientific.electrode_geometry import geometry_from_channel_snapshot
    try:
        geometry = geometry_from_channel_snapshot(snapshot)
    except ValueError as exc:
        raise RecordingImportError("ELECTRODE_GEOMETRY_INVALID", str(exc)) from exc

    by_label: dict[str, dict] = {}
    native_indexes: set[int] = set()
    display_orders: set[int] = set()
    for channel in configured:
        if not isinstance(channel, dict):
            raise RecordingImportError("CHANNEL_CONFIGURATION_INVALID", "通道配置快照格式无效。")
        label = str(channel.get("ElectrodeLabel", "")).strip()
        native_index = channel.get("NativeChannelIndex")
        expected_kind = channel.get("ExpectedKind")
        display_order = channel.get("DisplayOrder")
        if (not label or not isinstance(native_index, int) or isinstance(native_index, bool) or native_index < 0
                or native_index in native_indexes or expected_kind not in (1, 2) or isinstance(expected_kind, bool)
                or not isinstance(display_order, int) or isinstance(display_order, bool) or display_order < 0
                or display_order in display_orders or not isinstance(channel.get("IsSelectedForDisplay"), bool)):
            raise RecordingImportError("CHANNEL_CONFIGURATION_INVALID", "通道配置快照包含无效 EEG 通道。")
        native_indexes.add(native_index)
        display_orders.add(display_order)
        key = " ".join(label.split()).casefold()
        by_label.setdefault(key, []).append(channel)

    mapping = []
    used_native_indexes: set[int] = set()
    for column, (label, channel_type) in enumerate(zip(labels, channel_types, strict=True)):
        if channel_type.casefold() != "eeg":
            continue
        key = " ".join(label.strip().split()).casefold()
        matches = by_label.get(key, [])
        if len(matches) != 1:
            if geometry is not None:
                raise RecordingImportError("ELECTRODE_GEOMETRY_INVALID", "坐标快照无法与文件原始通道绑定。")
            return None, None
        native_index = matches[0]["NativeChannelIndex"]
        if native_index in used_native_indexes:
            return None, None
        used_native_indexes.add(native_index)
        mapping.append({"source_column": column, "native_channel_index": native_index})
    if not mapping:
        if geometry is not None:
            raise RecordingImportError("ELECTRODE_GEOMETRY_INVALID", "坐标快照没有可绑定的原始 EEG 通道。")
        return None, None
    return snapshot_json, json.dumps(mapping, separators=(",", ":"))


def _bind_export_channel_identity(identity_json: str, source_sha256: str | None,
                                  labels: list[str], channel_types: list[str]):
    if len(identity_json) > 262144:
        raise RecordingImportError("RECORDING_EXPORT_METADATA_INVALID", "导出通道身份信息超过允许大小。")
    try:
        identity = json.loads(identity_json)
        snapshot_value = identity["channel_configuration_snapshot_json"]
        sidecar_hash = identity["exported_signal_sha256"]
        channels = identity["channels"]
        hardware = identity.get("hardware", {})
        if not isinstance(hardware, dict):
            raise TypeError("hardware snapshots must be an object")
        acquisition_snapshot = _normalize_snapshot(hardware, "acquisition_montage_snapshot_json", dict)
        montage_snapshot = _normalize_snapshot(hardware, "montage_configuration_snapshot_json", dict)
        montage_catalogue = _normalize_snapshot(hardware, "montage_catalogue_snapshot_json", list)
        snapshot = json.loads(snapshot_value) if isinstance(snapshot_value, str) else snapshot_value
        snapshot_json = json.dumps(snapshot, ensure_ascii=False, separators=(",", ":"))
        configured = snapshot.get("Channels") if isinstance(snapshot, dict) else None
    except (json.JSONDecodeError, KeyError, TypeError, ValueError):
        raise RecordingImportError("RECORDING_EXPORT_METADATA_INVALID", "导出通道身份信息格式无效。") from None
    if (not isinstance(source_sha256, str) or len(source_sha256) != 64 or
            not isinstance(sidecar_hash, str) or sidecar_hash.casefold() != source_sha256.casefold() or
            not isinstance(configured, list) or not configured or len(snapshot_json) > 65536 or
            not isinstance(channels, list)):
        raise RecordingImportError("RECORDING_EXPORT_METADATA_MISMATCH", "导出身份信息与上传文件不匹配。")

    eeg_columns = [index for index, channel_type in enumerate(channel_types)
                   if channel_type.casefold() == "eeg"]
    eeg_channels = [entry for entry in channels if isinstance(entry, dict) and
                    str(entry.get("kind", "")).casefold() in {"1", "2", "reference", "bipolar"}]
    if len(channels) != len(labels) or len(eeg_channels) != len(eeg_columns):
        raise RecordingImportError("RECORDING_EXPORT_METADATA_MISMATCH", "导出身份信息中的 EEG 通道数量不匹配。")
    configured_by_index: dict[int, dict] = {}
    configured_display_orders: set[int] = set()
    configured_labels: set[str] = set()
    for entry in configured:
        if not isinstance(entry, dict):
            raise RecordingImportError("RECORDING_EXPORT_METADATA_INVALID", "导出通道配置快照无效。")
        native_index = entry.get("NativeChannelIndex")
        expected_kind = entry.get("ExpectedKind")
        label = entry.get("ElectrodeLabel")
        selected = entry.get("IsSelectedForDisplay")
        display_order = entry.get("DisplayOrder")
        if (not isinstance(native_index, int) or isinstance(native_index, bool) or native_index < 0 or
                native_index in configured_by_index or expected_kind not in (1, 2) or
                isinstance(expected_kind, bool) or not isinstance(label, str) or
                (not label.strip() and selected is True) or
                not isinstance(display_order, int) or isinstance(display_order, bool) or display_order < 0 or
                display_order in configured_display_orders or
                (bool(label.strip()) and " ".join(label.split()).casefold() in configured_labels) or
                not isinstance(selected, bool)):
            raise RecordingImportError("RECORDING_EXPORT_METADATA_INVALID", "导出通道配置索引重复或无效。")
        configured_display_orders.add(display_order)
        if label.strip():
            configured_labels.add(" ".join(label.split()).casefold())
        configured_by_index[native_index] = entry

    mapping = []
    used_native_indexes = set()
    for column, item in zip(eeg_columns, eeg_channels, strict=True):
        if not isinstance(item, dict):
            raise RecordingImportError("RECORDING_EXPORT_METADATA_INVALID", "导出通道映射无效。")
        native_index = item.get("native_channel_index")
        source_label = item.get("source_label")
        export_label = item.get("export_label")
        kind_value = item.get("kind")
        kind = str(kind_value).casefold()
        configured_entry = configured_by_index.get(native_index)
        expected_kind = configured_entry.get("ExpectedKind") if configured_entry else None
        configured_label = str(configured_entry.get("ElectrodeLabel", "")).strip() if configured_entry else ""
        kind_matches = kind in ({"1", "reference"} if expected_kind == 1 else
                               {"2", "bipolar"} if expected_kind == 2 else set())
        if (not isinstance(native_index, int) or isinstance(native_index, bool) or
                native_index in used_native_indexes or configured_entry is None or
                (configured_label and (not isinstance(source_label, str) or not source_label.strip())) or
                (not configured_label and source_label not in (None, "")) or
                not isinstance(export_label, str) or not export_label.strip() or
                export_label.strip().casefold() != labels[column].strip().casefold() or
                (configured_label and " ".join(source_label.split()).casefold() !=
                 " ".join(configured_label.split()).casefold()) or
                not kind_matches):
            raise RecordingImportError("RECORDING_EXPORT_METADATA_MISMATCH", "导出身份信息与文件通道或通道配置不匹配。")
        used_native_indexes.add(native_index)
        mapping.append({"source_column": column, "native_channel_index": native_index})
    if len(used_native_indexes) != len(eeg_channels):
        raise RecordingImportError("RECORDING_EXPORT_METADATA_MISMATCH", "导出身份信息包含重复 EEG 通道。")
    if montage_snapshot is not None:
        montage = json.loads(montage_snapshot)
        profile_id = montage.get("Id")
        rows = montage.get("DerivedChannels")
        if not isinstance(profile_id, str) or not profile_id.strip() or not isinstance(rows, list) or not rows:
            raise RecordingImportError("RECORDING_EXPORT_METADATA_INVALID", "导出导联配置快照无效。")
    if montage_catalogue is not None:
        profiles = json.loads(montage_catalogue)
        if any(not isinstance(item, dict) or not isinstance(item.get("Id"), str) for item in profiles):
            raise RecordingImportError("RECORDING_EXPORT_METADATA_INVALID", "导出导联目录快照无效。")
    identities = []
    for column, item in enumerate(channels):
        if not isinstance(item, dict) or str(item.get("export_label", "")).strip().casefold() != labels[column].strip().casefold():
            raise RecordingImportError("RECORDING_EXPORT_METADATA_MISMATCH", "导出通道身份与源列不符。")
        role = item.get("physiological_identity")
        if role is not None:
            if not isinstance(role, str) or role not in {"eeg", "eog", "ecg", "unconfirmed"}:
                raise RecordingImportError("CHANNEL_IDENTITY_INVALID", "导出通道生理身份无效。")
            identities.append({"SourceColumn": column, "Identity": role})
    if identities:
        snapshot["PhysiologicalIdentities"] = identities
        snapshot_json = json.dumps(snapshot, ensure_ascii=False, separators=(",", ":"))
    return (snapshot_json, json.dumps(mapping, separators=(",", ":")), acquisition_snapshot,
            montage_snapshot, montage_catalogue)


def _normalize_snapshot(hardware: dict, key: str, expected_type: type) -> str | None:
    value = hardware.get(key)
    if value is None:
        return None
    try:
        if isinstance(value, str):
            value = json.loads(value)
        if not isinstance(value, expected_type):
            raise TypeError
        encoded = json.dumps(value, ensure_ascii=False, separators=(",", ":"), allow_nan=False)
    except (json.JSONDecodeError, TypeError, ValueError):
        raise RecordingImportError("RECORDING_EXPORT_METADATA_INVALID", f"导出身份信息中的 {key} 格式无效。") from None
    if len(encoded) > 262144:
        raise RecordingImportError("RECORDING_EXPORT_METADATA_INVALID", f"导出身份信息中的 {key} 超过允许大小。")
    return encoded


def _utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()
