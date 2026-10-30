import json
import hashlib
import sqlite3
from datetime import datetime, timezone
from pathlib import Path
from uuid import uuid4

from app.core.config import ALLOWED_EXTENSIONS, DATABASE_PATH, RECORDINGS_DIR, ensure_storage_directories
from app.models.recording import RecordingSummary
from app.models.analysis_config import AnalysisConfigRequest
from app.services.filter_checkpoint_cache import FilterCheckpointCache
from app.services.analysis_preprocess_cache import AnalysisPreprocessCache
from app.persistence import connect_database, migrate_database
from app.services.recording_identity import (
    RECORDING_IMPORT_VERSION,
    backfill_recording_identity,
    canonical_channel_label,
)
from app.services.spectral_analysis import SpectralAnalysisService, _AUTO_NOTCH
from app.services.recording_samples import WpfRecordingFactsCache
from app.services.recording_access import RecordingAccessService
from app.services.wpf_recording import inspect_wpf_recording


class RecordingService:
    def __init__(self, storage_dir: Path = RECORDINGS_DIR, database_path: Path = DATABASE_PATH):
        self.storage_dir = Path(storage_dir)
        self.database_path = Path(database_path)
        self.storage_dir.mkdir(parents=True, exist_ok=True)
        self.database_path.parent.mkdir(parents=True, exist_ok=True)
        self._filter_checkpoints = FilterCheckpointCache()
        self._analysis_preprocess_cache = AnalysisPreprocessCache()
        self._wpf_recording_facts = WpfRecordingFactsCache()
        self._spectral_analysis = SpectralAnalysisService(self, self._analysis_preprocess_cache)
        from app.services.analysis_montage_snapshots import AnalysisMontageSnapshots
        self._analysis_montage_snapshots = AnalysisMontageSnapshots(self.database_path)
        self._recording_access = RecordingAccessService(self.storage_dir, self._wpf_recording_facts,
                                                        self._analysis_montage_snapshots)
        ensure_storage_directories()
        self._initialize_database()

    def _connect(self) -> sqlite3.Connection:
        return connect_database(self.database_path)

    def _initialize_database(self) -> None:
        migrate_database(self.database_path)
        backfill_recording_identity(self.database_path, self.storage_dir)

    def create_recording(self, original_name: str, suffix: str, raw_bytes: bytes) -> RecordingSummary:
        normalized_suffix = str(suffix).lower()
        if normalized_suffix not in ALLOWED_EXTENSIONS:
            raise ValueError("仅支持 BDF 或 EDF 文件")
        if not raw_bytes:
            raise ValueError("录制文件不能为空")

        recording_id = uuid4().hex
        stored_name = f"{recording_id}{normalized_suffix}"
        created_at = datetime.now(timezone.utc).isoformat()
        source_sha256 = hashlib.sha256(raw_bytes).hexdigest()
        file_size_bytes = len(raw_bytes)
        (self.storage_dir / stored_name).write_bytes(raw_bytes)

        with self._connect() as connection:
            connection.execute(
                """
                INSERT INTO recordings (
                    id, original_name, stored_name, extension, created_at,
                    source_sha256, file_size_bytes, import_version
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    recording_id, Path(str(original_name)).name, stored_name,
                    normalized_suffix, created_at, source_sha256,
                    file_size_bytes, RECORDING_IMPORT_VERSION,
                ),
            )

        return RecordingSummary(
            id=recording_id,
            original_name=Path(str(original_name)).name,
            stored_name=stored_name,
            extension=normalized_suffix,
            created_at=created_at,
            source_sha256=source_sha256,
            file_size_bytes=file_size_bytes,
            import_version=RECORDING_IMPORT_VERSION,
        )

    def create_imported_recording(self, original_name: str, suffix: str, raw_bytes: bytes) -> RecordingSummary:
        recording = self.create_recording(original_name, suffix, raw_bytes)
        try:
            sfreq, duration_s, channels, channel_types, channel_units = self._read_metadata(
                self.storage_dir / recording.stored_name
            )
        except Exception:
            self._delete_recording(recording)
            raise ValueError("无法读取 BDF/EDF 脑电文件") from None
        self._set_metadata(
            recording.id, sfreq, duration_s, channels, channel_types, channel_units
        )
        return self.require_recording(recording.id)

    def register_wpf_recording(
        self, source_directory: str, *, signal_mode: str = "raw",
        analysis_montage_profiles: list[dict[str, object]] | None = None,
    ) -> RecordingSummary:
        if analysis_montage_profiles and signal_mode != "acquisition_montage":
            raise ValueError("analysis montage snapshots require acquisition_montage mode")
        prepared_snapshots = None
        if analysis_montage_profiles:
            serialized = [json.dumps(profile, sort_keys=True, ensure_ascii=False, separators=(",", ":"))
                          for profile in analysis_montage_profiles]
            prepared_snapshots = self._analysis_montage_snapshots.prepare(source_directory, serialized)
        registered = self._register_wpf_source(source_directory, signal_mode=signal_mode)
        if prepared_snapshots:
            self._analysis_montage_snapshots.save(registered.id, prepared_snapshots)
            return self.require_recording(registered.id)
        return registered

    def _register_wpf_source(self, source_directory: str, *, signal_mode: str) -> RecordingSummary:
        facts = inspect_wpf_recording(source_directory)
        self._wpf_recording_facts.remember(facts)
        if signal_mode not in {"raw", "acquisition_montage"}:
            raise ValueError("unknown analysis signal mode")
        derived = signal_mode == "acquisition_montage"
        view = None
        if derived:
            from app.services.acquisition_analysis_signals import read_acquisition_signal_view
            view = read_acquisition_signal_view(facts.root)
        stored_name = f"{'wpf-montage' if derived else 'wpf'}:{facts.root}"
        recording_id = f"{'wpf-montage' if derived else 'wpf'}-{facts.session_id}-{facts.source_sha256[:12]}"
        import_version = "wpf-acquisition-montage-v1" if derived else "wpf-raw-v1"
        raw_channels = [item for item in facts.channels if item.stream_index in facts.eeg_indexes]
        labels = [signal.name for signal in view.signals] if view else [item.label for item in raw_channels]
        with self._connect() as connection:
            # Path identity keeps registration idempotent across hash-version
            # repairs, while the content hash still deduplicates moved copies.
            existing = connection.execute(
                "SELECT id, stored_name, source_sha256, import_version FROM recordings "
                "WHERE (source_sha256 = ? AND import_version = ?) OR stored_name = ?",
                (facts.source_sha256, import_version, stored_name),
            ).fetchone()
            if existing is not None:
                existing_name = str(existing["stored_name"])
                existing_directory = existing_name.partition(":")[2]
                if (existing_name.startswith(("wpf:", "wpf-montage:"))
                        and existing["source_sha256"] == facts.source_sha256
                        and existing["import_version"] == import_version
                        and not Path(existing_directory).is_dir()):
                    # Rebind only a content-identical recording with a stale
                    # source path; snapshots remain attached to the same id.
                    connection.execute(
                        "UPDATE recordings SET stored_name = ?, original_name = ? WHERE id = ?",
                        (stored_name, facts.root.name, existing["id"]),
                    )
                    connection.commit()
                return self.require_recording(str(existing["id"]))
            try:
                connection.execute(
                    """INSERT INTO recordings
                    (id, original_name, stored_name, extension, created_at, sfreq, duration_s,
                     channels_json, source_sha256, file_size_bytes, raw_channel_labels_json,
                     canonical_channel_labels_json, channel_types_json, channel_units_json, import_version)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
                    (recording_id, facts.root.name, stored_name, ".wpf", facts.recording_start_utc,
                     facts.sampling_rate_hz, facts.duration_s,
                     json.dumps(labels, ensure_ascii=False),
                     facts.source_sha256, sum(path.stat().st_size for path in facts.root.glob("samples-*.bin")),
                     json.dumps([item.label for item in raw_channels], ensure_ascii=False),
                     json.dumps(labels, ensure_ascii=False),
                     json.dumps(["eeg"] * len(labels) if derived else [item.kind for item in raw_channels]),
                     json.dumps(["V"] * len(labels)),
                     import_version),
                )
            except sqlite3.IntegrityError:
                # A concurrent request may win between the SELECT and INSERT.
                existing = connection.execute(
                    "SELECT id FROM recordings WHERE (source_sha256 = ? AND import_version = ?) OR stored_name = ?",
                    (facts.source_sha256, import_version, stored_name),
                ).fetchone()
                if existing is None:
                    raise
                return self.require_recording(str(existing["id"]))
        return self.require_recording(recording_id)

    def list_recordings(self) -> list[RecordingSummary]:
        with self._connect() as connection:
            rows = connection.execute("SELECT * FROM recordings ORDER BY created_at DESC").fetchall()
        return [self._row_to_summary(row) for row in rows]

    def require_recording(self, recording_id: str) -> RecordingSummary:
        with self._connect() as connection:
            row = connection.execute("SELECT * FROM recordings WHERE id = ?", (recording_id,)).fetchone()
        if row is None:
            raise KeyError("录制文件不存在")
        summary = self._row_to_summary(row)
        if summary.stored_name.startswith("wpf-montage:"):
            from app.services.acquisition_analysis_signals import historical_montage_catalogue, read_acquisition_signal_view
            directory = summary.stored_name.removeprefix("wpf-montage:")
            try:
                catalogue = historical_montage_catalogue(directory)
            except Exception:
                catalogue = ()
            snapshots = self._analysis_montage_snapshots.catalogue(summary.id, directory)
            known_ids = {entry["id"] for entry in catalogue}
            combined = catalogue + tuple(entry for entry in snapshots if entry["id"] not in known_ids)
            return RecordingSummary(**{**summary.__dict__, "montage_catalogue": combined})
        return summary


    def open_access_session(
        self, recording: RecordingSummary, *, analysis_montage_id: str | None = None,
        verify_content_hash: bool = True,
    ):
        return self._recording_access.open_session(
            recording, analysis_montage_id=analysis_montage_id,
            verify_content_hash=verify_content_hash,
        )

    def select_analysis_montage(self, recording: RecordingSummary, montage_id: str) -> RecordingSummary:
        """Validate a registered montage snapshot and bind its output channels."""
        if not recording.stored_name.startswith("wpf-montage:"):
            raise ValueError("ANALYSIS_MONTAGE_UNSUPPORTED: 该记录不支持采集导联投影")
        directory = recording.stored_name.removeprefix("wpf-montage:")
        view = self._analysis_montage_snapshots.resolve(recording.id, directory, montage_id)
        return RecordingSummary(**{
            **recording.__dict__,
            "channels": tuple(signal.name for signal in view.signals),
            "selected_montage_id": montage_id,
        })

    def analysis_signal_provenance(self, recording: RecordingSummary) -> dict:
        if not hasattr(recording, "stored_name"):
            recording = self.require_recording(recording.id)
        if recording.stored_name.startswith("wpf-montage:"):
            return self._analysis_montage_snapshots.resolve(
                recording.id, recording.stored_name.removeprefix("wpf-montage:"), recording.selected_montage_id,
            ).provenance
        return {"signal_mode": "raw", "analysis_reference": "original_recording_no_software_rereference"}

    def load_preview_data(
        self,
        recording: RecordingSummary,
        start_s: float,
        window_s: float,
    ) -> tuple[object, float, list[str], list[dict[str, object]], float]:
        """只读取预览所需的时间窗，避免为查看波形加载完整记录。"""
        with self.open_access_session(recording) as session:
            sfreq = session.sampling_rate_hz
            sample_count = session.sample_count
            duration_s = sample_count / sfreq
            actual_start_s = max(0.0, min(float(start_s), duration_s))
            actual_stop_s = min(duration_s, actual_start_s + max(0.1, float(window_s)))
            start_sample = min(sample_count, int(actual_start_s * sfreq))
            stop_sample = min(sample_count, max(start_sample, int(actual_stop_s * sfreq)))
            block = session.read_samples(start_sample, stop_sample)
            events = [
                event for event in session.events
                if actual_start_s <= float(event["elapsed_s"]) <= actual_stop_s
            ]
            return block.values_v, sfreq, list(session.channel_names), events, duration_s

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

    def load_spectrum(
        self,
        recording: RecordingSummary,
        start_s: float = 0.0,
        window_s: float = 30.0,
        channels: list[str] | None = None,
        *,
        low_hz: float = 1.0,
        high_hz: float = 30.0,
        notch_hz: float | None | object = _AUTO_NOTCH,
        filter_low_hz: float | None = None,
        filter_high_hz: float | None = None,
        output_low_hz: float | None = None,
        output_high_hz: float | None = None,
        preprocessing_scope: str = "configured_continuous_before_slice",
        execution_context=None,
    ) -> dict:
        """Compatibility facade for the offline spectral analysis use case."""
        return self._spectral_analysis.load_spectrum(recording, start_s, window_s, channels, low_hz=low_hz, high_hz=high_hz, notch_hz=notch_hz, filter_low_hz=filter_low_hz, filter_high_hz=filter_high_hz, output_low_hz=output_low_hz, output_high_hz=output_high_hz, preprocessing_scope=preprocessing_scope, execution_context=execution_context)

    def load_spectrogram(
        self,
        recording: RecordingSummary,
        start_s: float = 0.0,
        window_s: float = 30.0,
        channels: list[str] | None = None,
        *,
        low_hz: float = 1.0,
        high_hz: float = 50.0,
        notch_hz: float | None = None,
        output_low_hz: float | None = None,
        output_high_hz: float | None = None,
        execution_context=None,
    ) -> dict:
        """Compatibility facade for the spectrogram-v2 analysis use case."""
        return self._spectral_analysis.load_spectrogram(recording, start_s, window_s, channels, low_hz=low_hz, high_hz=high_hz, notch_hz=notch_hz, output_low_hz=output_low_hz, output_high_hz=output_high_hz, execution_context=execution_context)

    def load_configured_spectrum(self, recording: RecordingSummary, config: AnalysisConfigRequest) -> dict:
        """Compatibility facade preserving configurable PSD API semantics."""
        return self._spectral_analysis.load_configured_spectrum(
            recording,
            config,
            spectrum_loader=self.load_spectrum,
        )

    def load_configured_spectrogram(self, recording: RecordingSummary, config: AnalysisConfigRequest) -> dict:
        """Compatibility facade preserving configurable spectrogram API semantics."""
        return self._spectral_analysis.load_configured_spectrogram(
            recording,
            config,
            spectrogram_loader=self.load_spectrogram,
        )

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
        custom_montage: list[dict[str, object]] | None = None,
        baseline_stabilization: bool = False,
    ) -> dict:
        """按需读取并处理一个阅图窗口；原始文件始终保持不变。"""
        import numpy as np
        from app.services.waveform_playback import DISPLAY_FILTER_CONTRACT, DisplaySignalFilter
        from app.services.montage import apply_montage, build_montage

        low = float(low_cut_hz)
        high = float(high_cut_hz)
        if not 0 < low < high:
            raise ValueError("低切必须小于高切")
        if notch_hz is not None and float(notch_hz) not in (50.0, 60.0):
            raise ValueError("陷波仅支持关闭、50Hz 或 60Hz")

        access_session = self.open_access_session(recording)
        try:
            sfreq = access_session.sampling_rate_hz
            nyquist = sfreq / 2.0
            if high >= nyquist:
                raise ValueError(f"高切必须低于奈奎斯特频率（{nyquist:g}Hz）")
            duration_s = float(access_session.sample_count / sfreq)
            actual_start = max(0.0, min(float(start_s), duration_s))
            actual_window = max(0.1, float(window_s))
            actual_stop = min(duration_s, actual_start + actual_window)

            available = list(access_session.channel_names)
            ref_mode = str(reference or "original")
            montage_id = str(montage or ("reference:" + ref_mode if ref_mode not in {"original", "average"} else ref_mode))
            definition = build_montage(
                montage_id, available, channels, average_exclude, custom_montage,
            )
            read_names = list(definition.required_channels)

            # 从最近检查点推进同一条因果显示滤波状态，再截取目标窗口。
            # 文件按秒级块读取，避免访问层为每个 50ms 显示帧重复触发文件读取。
            n_times = access_session.sample_count
            start_sample = min(max(0, n_times - 1), int(actual_start * sfreq))
            stop_sample = min(n_times, max(start_sample + 1, int(actual_stop * sfreq)))
            filter_chunk_samples = max(1, int(round(sfreq * 0.05)))
            # File access is much more expensive than advancing the already
            # stateful display filter. Read ahead in one-second blocks and
            # process that block as one causal sequence. This keeps the
            # display contract while preventing one file read per 50 samples.
            read_chunk_samples = max(
                filter_chunk_samples,
                int(round(sfreq * DISPLAY_FILTER_CONTRACT["read_ahead_seconds"])),
            )
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
                end = min(stop_sample, cursor + read_chunk_samples)
                values = access_session.read_samples(cursor, end, read_names).values_v
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
                for onset, label in ((event["elapsed_s"], event["label"]) for event in access_session.events)
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
                "settings": {"low_cut_hz": low, "high_cut_hz": high, "notch_hz": notch_hz, "baseline_stabilization": bool(baseline_stabilization), "reference": ref_mode, "montage": definition.id, "average_exclude": list(definition.excluded_channels), "custom_montage": custom_montage or [], "filter_contract": DISPLAY_FILTER_CONTRACT},
            }
        finally:
            access_session.close()

    def _set_metadata(
        self,
        recording_id: str,
        sfreq: float,
        duration_s: float,
        channels: list[str],
        channel_types: list[str],
        channel_units: list[str],
    ) -> None:
        raw_labels = [str(name) for name in channels]
        canonical = [canonical_channel_label(name) for name in raw_labels]
        if len({name.casefold() for name in canonical}) != len(canonical):
            raise ValueError("规范化通道标签存在重复")
        with self._connect() as connection:
            connection.execute(
                """UPDATE recordings SET sfreq = ?, duration_s = ?, channels_json = ?,
                   raw_channel_labels_json = ?, canonical_channel_labels_json = ?,
                   channel_types_json = ?, channel_units_json = ?, import_version = ?
                   WHERE id = ?""",
                (
                    sfreq, duration_s, json.dumps(raw_labels, ensure_ascii=False),
                    json.dumps(raw_labels, ensure_ascii=False),
                    json.dumps(canonical, ensure_ascii=False),
                    json.dumps(channel_types, ensure_ascii=False),
                    json.dumps(channel_units, ensure_ascii=False),
                    RECORDING_IMPORT_VERSION, recording_id,
                ),
            )

    @staticmethod
    def _read_metadata(path: Path) -> tuple[float, float, list[str], list[str], list[str]]:
        import mne

        reader = mne.io.read_raw_bdf if path.suffix.lower() == ".bdf" else mne.io.read_raw_edf
        raw = reader(path, preload=False, verbose=False)
        try:
            names = list(raw.ch_names)
            original_units = getattr(raw, "_orig_units", {}) or {}
            units = [str(original_units.get(name, "unknown")) for name in names]
            return (
                float(raw.info["sfreq"]),
                float(raw.n_times / raw.info["sfreq"]),
                names,
                list(raw.get_channel_types()),
                units,
            )
        finally:
            raw.close()

    def _delete_recording(self, recording: RecordingSummary) -> None:
        self._filter_checkpoints.clear_recording(recording.id)
        self._analysis_preprocess_cache.clear_recording(recording.id)
        (self.storage_dir / recording.stored_name).unlink(missing_ok=True)
        with self._connect() as connection:
            connection.execute("DELETE FROM recordings WHERE id = ?", (recording.id,))

    @staticmethod
    def _row_to_summary(row: sqlite3.Row) -> RecordingSummary:
        return RecordingSummary(
            id=row["id"],
            original_name=row["original_name"],
            stored_name=row["stored_name"],
            extension=row["extension"],
            created_at=row["created_at"],
            sfreq=row["sfreq"],
            duration_s=row["duration_s"],
            channels=tuple(json.loads(row["channels_json"])),
            source_sha256=row["source_sha256"],
            file_size_bytes=row["file_size_bytes"],
            raw_channel_labels=tuple(json.loads(row["raw_channel_labels_json"] or "[]")),
            canonical_channel_labels=tuple(json.loads(row["canonical_channel_labels_json"] or "[]")),
            channel_types=tuple(json.loads(row["channel_types_json"] or "[]")),
            channel_units=tuple(json.loads(row["channel_units_json"] or "[]")),
            import_version=row["import_version"],
        )
