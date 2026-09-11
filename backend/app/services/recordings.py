import json
import sqlite3
from datetime import datetime, timezone
from pathlib import Path
from uuid import uuid4

from app.core.config import ALLOWED_EXTENSIONS, DATABASE_PATH, RECORDINGS_DIR, ensure_storage_directories
from app.models.recording import ChannelMapping, RecordingSummary
from app.services.filter_checkpoint_cache import FilterCheckpointCache


class RecordingService:
    def __init__(self, storage_dir: Path = RECORDINGS_DIR, database_path: Path = DATABASE_PATH):
        self.storage_dir = Path(storage_dir)
        self.database_path = Path(database_path)
        self.storage_dir.mkdir(parents=True, exist_ok=True)
        self.database_path.parent.mkdir(parents=True, exist_ok=True)
        self._filter_checkpoints = FilterCheckpointCache()
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

    def open_data_reader(self, recording: RecordingSummary):
        """打开非预加载读取器，供连续回放按样本块读取；调用方必须 close。"""
        import mne

        path = self.storage_dir / recording.stored_name
        reader = mne.io.read_raw_bdf if recording.extension == ".bdf" else mne.io.read_raw_edf
        raw = reader(path, preload=False, verbose=False)
        events = [
            {"elapsed_s": float(onset), "label": str(label)}
            for onset, label in zip(raw.annotations.onset, raw.annotations.description)
        ]
        return raw, float(raw.info["sfreq"]), list(raw.ch_names), events

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
        montage: str | None = None,
        average_exclude: list[str] | None = None,
        baseline_stabilization: bool = False,
    ) -> dict:
        """按需读取并处理一个阅图窗口；原始文件始终保持不变。"""
        import numpy as np
        import mne
        from app.services.waveform_playback import DISPLAY_FILTER_CONTRACT, DisplaySignalFilter
        from app.services.montage import apply_montage, build_montage

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
            ref_mode = str(reference or "original")
            montage_id = str(montage or ("reference:" + ref_mode if ref_mode not in {"original", "average"} else ref_mode))
            definition = build_montage(montage_id, available, channels, average_exclude)
            read_names = list(definition.required_channels)
            indices = [available.index(name) for name in read_names]

            # 从最近检查点按播放相同的 50ms 块推进滤波器，再截取目标窗口。
            # 首次访问仍从文件起点建立状态，后续窗口可复用同一滤波器状态。
            n_times = int(raw.n_times)
            start_sample = min(max(0, n_times - 1), int(actual_start * sfreq))
            stop_sample = min(n_times, max(start_sample + 1, int(actual_stop * sfreq)))
            chunk_samples = max(1, int(round(sfreq * 0.05)))
            checkpoint_key = (recording.id, sfreq, low, high, notch_hz, bool(baseline_stabilization), tuple(read_names))
            checkpoint_start, checkpoint_state = self._filter_checkpoints.nearest(checkpoint_key, start_sample)
            display_filter = DisplaySignalFilter(
                sfreq=sfreq,
                channel_count=len(read_names),
                notch_freq=notch_hz,
                bp_low=low,
                bp_high=high,
                baseline_stabilization=baseline_stabilization,
            )
            if checkpoint_state is not None:
                display_filter.restore(checkpoint_state)
            pieces = []
            cursor = checkpoint_start
            checkpoint_interval = max(1, int(round(sfreq * 10.0)))
            next_checkpoint = ((cursor // checkpoint_interval) + 1) * checkpoint_interval
            while cursor < stop_sample:
                end = min(stop_sample, cursor + chunk_samples)
                values = np.asarray(raw.get_data(picks=indices, start=cursor, stop=end), dtype=float).T
                if values.shape[0] == 0:
                    break
                filtered = display_filter.process(values)
                derived = apply_montage(filtered, read_names, definition)
                keep_start = max(0, start_sample - cursor)
                keep_stop = min(len(derived), stop_sample - cursor)
                if keep_start < keep_stop:
                    pieces.append(derived[keep_start:keep_stop])
                cursor = end
                if cursor >= next_checkpoint:
                    self._filter_checkpoints.put(checkpoint_key, cursor, display_filter.snapshot())
                    while next_checkpoint <= cursor:
                        next_checkpoint += checkpoint_interval
            if not pieces:
                raise ValueError("窗口数据不足")
            cropped = np.concatenate(pieces, axis=0)
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
                "channels": {item.name: (cropped[:, index] * 1e6).round(4).tolist() for index, item in enumerate(definition.channels)},
                "events": events,
                "settings": {"low_cut_hz": low, "high_cut_hz": high, "notch_hz": notch_hz, "baseline_stabilization": bool(baseline_stabilization), "reference": ref_mode, "montage": definition.id, "average_exclude": list(definition.excluded_channels), "filter_contract": DISPLAY_FILTER_CONTRACT},
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
        self._filter_checkpoints.clear_recording(recording.id)
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
