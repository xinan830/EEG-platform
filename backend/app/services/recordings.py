import json
import sqlite3
from datetime import datetime, timezone
from pathlib import Path
from uuid import uuid4

from app.core.config import ALLOWED_EXTENSIONS, DATABASE_PATH, RECORDINGS_DIR, ensure_storage_directories
from app.models.recording import ChannelMapping, RecordingSummary


class RecordingService:
    def __init__(self, storage_dir: Path = RECORDINGS_DIR, database_path: Path = DATABASE_PATH):
        self.storage_dir = Path(storage_dir)
        self.database_path = Path(database_path)
        self.storage_dir.mkdir(parents=True, exist_ok=True)
        self.database_path.parent.mkdir(parents=True, exist_ok=True)
        ensure_storage_directories()
        self._initialize_database()

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self.database_path)
        connection.row_factory = sqlite3.Row
        return connection

    def _initialize_database(self) -> None:
        with self._connect() as connection:
            connection.execute(
                """
                CREATE TABLE IF NOT EXISTS recordings (
                    id TEXT PRIMARY KEY,
                    original_name TEXT NOT NULL,
                    stored_name TEXT NOT NULL UNIQUE,
                    extension TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    sfreq REAL,
                    duration_s REAL,
                    channels_json TEXT NOT NULL DEFAULT '[]',
                    mapping_json TEXT
                )
                """
            )

    def create_recording(self, original_name: str, suffix: str, raw_bytes: bytes) -> RecordingSummary:
        normalized_suffix = str(suffix).lower()
        if normalized_suffix not in ALLOWED_EXTENSIONS:
            raise ValueError("仅支持 BDF 或 EDF 文件")
        if not raw_bytes:
            raise ValueError("录制文件不能为空")

        recording_id = uuid4().hex
        stored_name = f"{recording_id}{normalized_suffix}"
        created_at = datetime.now(timezone.utc).isoformat()
        (self.storage_dir / stored_name).write_bytes(raw_bytes)

        with self._connect() as connection:
            connection.execute(
                """
                INSERT INTO recordings (id, original_name, stored_name, extension, created_at)
                VALUES (?, ?, ?, ?, ?)
                """,
                (recording_id, Path(str(original_name)).name, stored_name, normalized_suffix, created_at),
            )

        return RecordingSummary(
            id=recording_id,
            original_name=Path(str(original_name)).name,
            stored_name=stored_name,
            extension=normalized_suffix,
            created_at=created_at,
        )

    def create_imported_recording(self, original_name: str, suffix: str, raw_bytes: bytes) -> RecordingSummary:
        recording = self.create_recording(original_name, suffix, raw_bytes)
        try:
            sfreq, duration_s, channels = self._read_metadata(self.storage_dir / recording.stored_name)
        except Exception:
            self._delete_recording(recording)
            raise ValueError("无法读取 BDF/EDF 脑电文件") from None
        self._set_metadata(recording.id, sfreq, duration_s, channels)
        return self.require_recording(recording.id)

    def list_recordings(self) -> list[RecordingSummary]:
        with self._connect() as connection:
            rows = connection.execute("SELECT * FROM recordings ORDER BY created_at DESC").fetchall()
        return [self._row_to_summary(row) for row in rows]

    def require_recording(self, recording_id: str) -> RecordingSummary:
        with self._connect() as connection:
            row = connection.execute("SELECT * FROM recordings WHERE id = ?", (recording_id,)).fetchone()
        if row is None:
            raise KeyError("录制文件不存在")
        return self._row_to_summary(row)

    def update_mapping(self, recording_id: str, mapping: ChannelMapping) -> RecordingSummary:
        recording = self.require_recording(recording_id)
        self.validate_mapping(mapping, list(recording.channels))
        mapping_json = json.dumps(mapping.__dict__, ensure_ascii=False)
        with self._connect() as connection:
            connection.execute("UPDATE recordings SET mapping_json = ? WHERE id = ?", (mapping_json, recording_id))
        return self.require_recording(recording_id)

    def load_data(self, recording: RecordingSummary) -> tuple[object, float, list[str], list[dict[str, object]]]:
        import mne

        path = self.storage_dir / recording.stored_name
        reader = mne.io.read_raw_bdf if recording.extension == ".bdf" else mne.io.read_raw_edf
        raw = reader(path, preload=True, verbose=False)
        try:
            events = [
                {"elapsed_s": float(onset), "label": str(label)}
                for onset, label in zip(raw.annotations.onset, raw.annotations.description)
            ]
            return raw.get_data().T, float(raw.info["sfreq"]), list(raw.ch_names), events
        finally:
            raw.close()

    def load_preview_data(
        self,
        recording: RecordingSummary,
        start_s: float,
        window_s: float,
    ) -> tuple[object, float, list[str], list[dict[str, object]], float]:
        """只读取预览所需的时间窗，避免为查看波形加载完整记录。"""
        import mne

        path = self.storage_dir / recording.stored_name
        reader = mne.io.read_raw_bdf if recording.extension == ".bdf" else mne.io.read_raw_edf
        raw = reader(path, preload=False, verbose=False)
        try:
            sfreq = float(raw.info["sfreq"])
            duration_s = float(raw.n_times / sfreq)
            actual_start_s = max(0.0, min(float(start_s), duration_s))
            actual_stop_s = min(duration_s, actual_start_s + max(0.1, float(window_s)))
            start_sample = int(actual_start_s * sfreq)
            stop_sample = max(start_sample + 1, int(actual_stop_s * sfreq))
            events = [
                {"elapsed_s": float(onset), "label": str(label)}
                for onset, label in zip(raw.annotations.onset, raw.annotations.description)
                if actual_start_s <= float(onset) <= actual_stop_s
            ]
            return raw.get_data(start=start_sample, stop=stop_sample).T, sfreq, list(raw.ch_names), events, duration_s
        finally:
            raw.close()

    def load_preview(
        self,
        recording: RecordingSummary,
        start_s: float = 0.0,
        window_s: float = 10.0,
        max_points: int = 10000,
    ) -> dict:
        """读取高分辨率的局部波形预览，不执行 IAPF 或其它指标分析。"""
        import numpy as np

        data, sfreq, names, events, duration_s = self.load_preview_data(recording, start_s, window_s)
        values = np.asarray(data, dtype=float)
        step = max(1, int(np.ceil(len(values) / max_points)))
        sampled = values[::step]
        # 原始 EEG 常带有毫伏级电极直流偏置；预览只关心波动，逐通道去中位数
        # 才能在同一垂直量程中看见微伏级脑电，而不会被偏置压成直线。
        centered = sampled - np.median(sampled, axis=0, keepdims=True)
        actual_start_s = max(0.0, min(float(start_s), duration_s))
        return {
            "sfreq": float(sfreq),
            "duration_s": duration_s,
            "window_start_s": actual_start_s,
            "window_duration_s": float(len(values) / sfreq),
            "elapsed_s": (actual_start_s + np.arange(len(sampled)) * step / sfreq).round(6).tolist(),
            "channels": {name: (centered[:, index] * 1e6).round(4).tolist() for index, name in enumerate(names)},
            "events": events,
        }

    def load_window(
        self,
        recording: RecordingSummary,
        start_s: float = 0.0,
        window_s: float = 10.0,
        low_cut_hz: float = 0.5,
        high_cut_hz: float = 70.0,
        notch_hz: float | None = None,
        reference: str = "original",
        channels: list[str] | None = None,
    ) -> dict:
        """按需读取并处理一个阅图窗口；原始文件始终保持不变。"""
        import numpy as np
        import mne
        from app.services.waveform_playback import DisplaySignalFilter

        low = float(low_cut_hz)
        high = float(high_cut_hz)
        if not 0 < low < high:
            raise ValueError("低切必须小于高切")
        if notch_hz is not None and float(notch_hz) not in (50.0, 60.0):
            raise ValueError("陷波仅支持关闭、50Hz 或 60Hz")

        path = self.storage_dir / recording.stored_name
        reader = mne.io.read_raw_bdf if recording.extension == ".bdf" else mne.io.read_raw_edf
        raw = reader(path, preload=False, verbose=False)
        try:
            sfreq = float(raw.info["sfreq"])
            nyquist = sfreq / 2.0
            if high >= nyquist:
                raise ValueError(f"高切必须低于奈奎斯特频率（{nyquist:g}Hz）")
            duration_s = float(raw.n_times / sfreq)
            actual_start = max(0.0, min(float(start_s), duration_s))
            actual_window = max(0.1, float(window_s))
            actual_stop = min(duration_s, actual_start + actual_window)

            available = list(raw.ch_names)
            by_upper = {name.upper(): name for name in available}
            if channels:
                selected_names = []
                for requested in channels:
                    match = by_upper.get(str(requested).upper())
                    if match and match not in selected_names:
                        selected_names.append(match)
                if not selected_names:
                    raise ValueError("请求的通道不存在")
            else:
                selected_names = available
            reference_name = None
            ref_mode = str(reference or "original")
            if ref_mode not in {"original", "average"}:
                reference_name = by_upper.get(ref_mode.upper())
                if reference_name is None:
                    raise ValueError("参考通道不存在")
            # 平均参考按文件内全部通道计算，但返回仍只包含当前显示通道。
            read_names = list(available) if ref_mode == "average" else list(selected_names)
            if reference_name and reference_name not in read_names:
                read_names.append(reference_name)
            indices = [available.index(name) for name in read_names]

            # 扩展边界后滤波，再裁剪，避免窗口首尾出现明显滤波瞬态。
            pad_s = max(1.0, min(10.0, 3.0 / low))
            read_start = max(0.0, actual_start - pad_s)
            read_stop = min(duration_s, actual_stop + pad_s)
            start_sample = int(read_start * sfreq)
            stop_sample = max(start_sample + 1, int(read_stop * sfreq))
            values = np.asarray(raw.get_data(picks=indices, start=start_sample, stop=stop_sample), dtype=float).T
            if values.shape[0] < 4:
                raise ValueError("窗口数据不足")
            # 与 WebSocket 播放完全复用同一个连续显示滤波器，避免导入静态图
            # 和点击播放后的波形因零相位/因果滤波差异而不一致。
            display_filter = DisplaySignalFilter(
                sfreq=sfreq,
                channel_count=len(read_names),
                notch_freq=notch_hz,
                bp_low=low,
                bp_high=high,
            )
            filtered = display_filter.process(values)

            if ref_mode == "average":
                filtered = filtered - filtered.mean(axis=1, keepdims=True)
            elif reference_name:
                ref_index = read_names.index(reference_name)
                filtered = filtered - filtered[:, ref_index : ref_index + 1]
            selected_indices = [read_names.index(name) for name in selected_names]
            filtered = filtered[:, selected_indices]

            crop_start = max(0, int((actual_start - read_start) * sfreq))
            crop_stop = min(len(filtered), crop_start + max(1, int((actual_stop - actual_start) * sfreq)))
            cropped = filtered[crop_start:crop_stop]
            elapsed = actual_start + np.arange(len(cropped), dtype=float) / sfreq
            events = [
                {"elapsed_s": float(onset), "label": str(label)}
                for onset, label in zip(raw.annotations.onset, raw.annotations.description)
                if actual_start <= float(onset) <= actual_stop
            ]
            return {
                "sfreq": sfreq,
                "duration_s": duration_s,
                "window_start_s": actual_start,
                "window_duration_s": float(len(cropped) / sfreq),
                "elapsed_s": elapsed.round(6).tolist(),
                "channels": {name: (cropped[:, index] * 1e6).round(4).tolist() for index, name in enumerate(selected_names)},
                "events": events,
                "settings": {"low_cut_hz": low, "high_cut_hz": high, "notch_hz": notch_hz, "reference": ref_mode},
            }
        finally:
            raw.close()

    @staticmethod
    def validate_mapping(mapping: ChannelMapping, available: list[str]) -> ChannelMapping:
        required = [mapping.fz, mapping.pz, mapping.oz]
        if len({name.upper() for name in required}) != 3:
            raise ValueError("Fz、Pz、Oz 映射不能重复")
        optional = [name for name in (mapping.f3, mapping.f4) if name]
        all_names = required + optional
        if len({name.upper() for name in all_names}) != len(all_names):
            raise ValueError("映射通道不能重复")
        available_upper = {name.upper() for name in available}
        if any(name.upper() not in available_upper for name in required):
            raise ValueError("映射通道不存在于录制文件")
        if (mapping.f3 is None) != (mapping.f4 is None):
            raise ValueError("F3 与 F4 必须同时映射或同时留空")
        if any(name.upper() not in available_upper for name in optional):
            raise ValueError("映射通道不存在于录制文件")
        return mapping

    def _set_metadata(self, recording_id: str, sfreq: float, duration_s: float, channels: list[str]) -> None:
        with self._connect() as connection:
            connection.execute(
                "UPDATE recordings SET sfreq = ?, duration_s = ?, channels_json = ? WHERE id = ?",
                (sfreq, duration_s, json.dumps(channels, ensure_ascii=False), recording_id),
            )

    @staticmethod
    def _read_metadata(path: Path) -> tuple[float, float, list[str]]:
        import mne

        reader = mne.io.read_raw_bdf if path.suffix.lower() == ".bdf" else mne.io.read_raw_edf
        raw = reader(path, preload=False, verbose=False)
        try:
            return float(raw.info["sfreq"]), float(raw.n_times / raw.info["sfreq"]), list(raw.ch_names)
        finally:
            raw.close()

    def _delete_recording(self, recording: RecordingSummary) -> None:
        (self.storage_dir / recording.stored_name).unlink(missing_ok=True)
        with self._connect() as connection:
            connection.execute("DELETE FROM recordings WHERE id = ?", (recording.id,))

    @staticmethod
    def _row_to_summary(row: sqlite3.Row) -> RecordingSummary:
        raw_mapping = json.loads(row["mapping_json"]) if row["mapping_json"] else None
        mapping = ChannelMapping(**raw_mapping) if raw_mapping else None
        return RecordingSummary(
            id=row["id"],
            original_name=row["original_name"],
            stored_name=row["stored_name"],
            extension=row["extension"],
            created_at=row["created_at"],
            sfreq=row["sfreq"],
            duration_s=row["duration_s"],
            channels=tuple(json.loads(row["channels_json"])),
            mapping=mapping,
        )
