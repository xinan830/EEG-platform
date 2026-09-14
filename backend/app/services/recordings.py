import json
import hashlib
import sqlite3
from datetime import datetime, timezone
from pathlib import Path
from uuid import uuid4

from app.core.config import ALLOWED_EXTENSIONS, DATABASE_PATH, RECORDINGS_DIR, ensure_storage_directories
from app.models.recording import ChannelMapping, RecordingSummary
from app.models.analysis_config import AnalysisConfigRequest
from app.services.filter_checkpoint_cache import FilterCheckpointCache
from app.services.analysis_preprocess_cache import AnalysisPreprocessCache, PreprocessedRecording
from app.persistence import migrate_database
from app.services.recording_identity import (
    RECORDING_IMPORT_VERSION,
    backfill_recording_identity,
    canonical_channel_label,
)
from app.eeg_core.quality import SpectralQualityGateError


class RecordingService:
    def __init__(self, storage_dir: Path = RECORDINGS_DIR, database_path: Path = DATABASE_PATH):
        self.storage_dir = Path(storage_dir)
        self.database_path = Path(database_path)
        self.storage_dir.mkdir(parents=True, exist_ok=True)
        self.database_path.parent.mkdir(parents=True, exist_ok=True)
        self._filter_checkpoints = FilterCheckpointCache()
        self._analysis_preprocess_cache = AnalysisPreprocessCache()
        ensure_storage_directories()
        self._initialize_database()

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self.database_path)
        connection.row_factory = sqlite3.Row
        return connection

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

    def load_spectrum(
        self,
        recording: RecordingSummary,
        start_s: float = 0.0,
        window_s: float = 30.0,
        channels: list[str] | None = None,
    ) -> dict:
        """Return v3 PSD, absolute band power and relative band power.

        The complete recording is filtered before slicing so zero-phase boundary
        behavior is identical for every requested window. Analysis reference and
        filter parameters are intentionally fixed by ``offline-spectral-v3``.
        """
        import numpy as np
        from app.eeg_core.analysis_contract import ANALYSIS_CONTRACT
        from app.eeg_core.spectral import band_power, estimate_welch_psd, preprocess_offline

        version = str(ANALYSIS_CONTRACT["algorithm_version"])
        cached = self._analysis_preprocess_cache.get(recording.id, version)
        if cached is None:
            data, sfreq, names, _events = self.load_data(recording)
            filtered_all = preprocess_offline(np.asarray(data, dtype=float), sfreq)
            cached = PreprocessedRecording(filtered_all, sfreq, tuple(names))
            self._analysis_preprocess_cache.put(recording.id, version, cached)
        filtered_all, sfreq, names = cached.data, cached.sfreq, list(cached.channel_names)
        available = {name.casefold(): name for name in names}
        requested = names if channels is None else [available.get(item.casefold()) for item in channels]
        if any(item is None for item in requested):
            raise ValueError("频谱分析请求包含不存在的通道")
        requested_names = [item for item in requested if item is not None]
        if not requested_names:
            raise ValueError("频谱分析至少需要一个通道")
        indexes = [names.index(item) for item in requested_names]
        filtered = filtered_all[:, indexes]
        duration_s = len(filtered) / sfreq
        actual_start = max(0.0, min(float(start_s), duration_s))
        start_index = int(np.floor(actual_start * sfreq))
        stop_index = min(len(filtered), start_index + int(round(float(window_s) * sfreq)))
        window = filtered[start_index:stop_index]
        if len(window) < int(round(float(ANALYSIS_CONTRACT["welch_segment_s"]) * sfreq)):
            raise ValueError("频谱分析窗口至少需要 4 秒")
        spectrum = estimate_welch_psd(window, sfreq)
        if spectrum.gate_failed:
            raise SpectralQualityGateError({
                "clean_segments": spectrum.clean_epochs,
                "total_segments": spectrum.total_epochs,
                "clean_ratio": spectrum.signal_quality,
                "gate_failed": spectrum.gate_failed,
                "rejected_reasons": list(spectrum.rejected_reasons),
            })
        bands = {"delta": (1.0, 4.0), "theta": (4.0, 8.0), "alpha": (8.0, 13.0), "beta": (13.0, 30.0)}
        psd_uv = spectrum.psd * 1e12
        absolute = {
            name: {band: float(band_power(spectrum.freqs, spectrum.psd[index], *edges) * 1e12) for band, edges in bands.items()}
            for index, name in enumerate(requested_names)
        }
        relative = {}
        for name, values in absolute.items():
            total = sum(values.values())
            relative[name] = {band: value / total if total > 0 else 0.0 for band, value in values.items()}
        return {
            "recording_id": recording.id,
            "window_start_s": actual_start,
            "window_duration_s": len(window) / sfreq,
            "sfreq_hz": sfreq,
            "channels": requested_names,
            "analysis_reference": ANALYSIS_CONTRACT["reference"],
            "algorithm_version": ANALYSIS_CONTRACT["algorithm_version"],
            "filter_contract": {key: ANALYSIS_CONTRACT[key] for key in ("bandpass_type", "bandpass_prototype_order", "bandpass_hz", "preprocessing_phase", "filter_form")},
            "welch_contract": {key: ANALYSIS_CONTRACT[key] for key in ("welch_segment_s", "welch_segment_overlap", "welch_window", "welch_scaling")},
            "units": {"psd": "uV^2/Hz", "absolute_power": "uV^2", "relative_power": "ratio"},
            "frequencies_hz": spectrum.freqs.tolist(),
            "psd": {name: psd_uv[index].tolist() for index, name in enumerate(requested_names)},
            "band_power": absolute,
            "relative_band_power": relative,
            "quality": {
                "clean_segments": spectrum.clean_epochs,
                "total_segments": spectrum.total_epochs,
                "clean_ratio": spectrum.signal_quality,
                "gate_failed": spectrum.gate_failed,
                "rejected_reasons": list(spectrum.rejected_reasons),
            },
        }

    def load_spectrogram(self, recording: RecordingSummary, start_s: float = 0.0, window_s: float = 30.0, channels: list[str] | None = None) -> dict:
        """Return a fixed v1 spectrogram from the continuous v3-preprocessed signal."""
        from app.eeg_core.spectral import band_power, estimate_spectrogram_with_quality
        payload = self.load_spectrum(recording, start_s=start_s, window_s=window_s, channels=channels)
        names = payload["channels"]
        cached = self._analysis_preprocess_cache.get(recording.id, str(payload["algorithm_version"]))
        assert cached is not None
        indexes = [list(cached.channel_names).index(name) for name in names]
        import numpy as np
        start_index = int(np.floor(float(start_s) * cached.sfreq))
        stop_index = min(len(cached.data), start_index + int(round(float(window_s) * cached.sfreq)))
        times, freqs, values, quality = estimate_spectrogram_with_quality(cached.data[start_index:stop_index, :][:, indexes], cached.sfreq)
        power_uv = values * 1e12
        power_db = 10.0 * np.log10(np.maximum(power_uv, np.finfo(float).tiny))
        linear_values = {name: power_uv[:, index, :].tolist() for index, name in enumerate(names)}
        db_values = {name: power_db[:, index, :].tolist() for index, name in enumerate(names)}
        bands = {"delta": (1.0, 4.0), "theta": (4.0, 8.0), "alpha": (8.0, 13.0), "beta": (13.0, 30.0)}
        band_series: dict[str, dict[str, list[float]]] = {}
        for index, name in enumerate(names):
            band_series[name] = {}
            for band, (low, high) in bands.items():
                band_series[name][band] = [float(band_power(freqs, row, low, high) * 1e12) if np.isfinite(row).all() else float("nan") for row in values[:, index, :]]
        for item in quality:
            item["center_s"] = float(item["center_s"]) + float(start_s)
            item["start_s"] = float(item["start_s"]) + float(start_s)
            item["end_s"] = float(item["end_s"]) + float(start_s)
        return {"recording_id": recording.id, "window_start_s": float(start_s), "window_duration_s": (stop_index - start_index) / cached.sfreq, "sfreq_hz": float(cached.sfreq), "channels": names, "times_s": (times + float(start_s)).tolist(), "frequencies_hz": freqs.tolist(), "power": linear_values, "power_linear": linear_values, "power_db": db_values, "band_power_timeseries": band_series, "units": "uV^2/Hz", "power_linear_units": "uV^2/Hz", "power_db_units": "dB re 1 uV^2/Hz", "band_power_timeseries_units": "uV^2", "analysis_algorithm_version": "offline-spectral-v3", "spectrogram_contract_version": "spectrogram-v2", "algorithm_version": "spectrogram-v2", "segment_s": 4.0, "step_s": 1.0, "quality": {"windows": quality, "clean_windows": sum(item["status"] == "clean" for item in quality), "total_windows": len(quality), "bad_windows": sum(item["status"] == "bad" for item in quality)}}

    def load_configured_spectrum(self, recording: RecordingSummary, config: AnalysisConfigRequest) -> dict:
        """Apply safe timing/channel configuration around the frozen v3 math."""
        requested = config.model_dump(mode="json")
        requested_duration = config.time.end_s - config.time.start_s
        payload = self.load_spectrum(recording, config.time.start_s, requested_duration, config.channels)
        actual_start = float(payload["window_start_s"])
        actual_end = actual_start + float(payload["window_duration_s"])
        # Sample-index slicing quantizes the end to the sampling period. A UI
        # value rounded to milliseconds may be one sample beyond that boundary.
        sample_tolerance = 1.5 / float(payload["sfreq_hz"])
        if config.mode == "static" and config.time.end_s > actual_end + sample_tolerance:
            raise ValueError(f"静态频谱分析区间超出文件范围，文件实际结束时间为 {actual_end:.3f} s")
        execution = {
            "mode": config.mode,
            "channels": payload["channels"],
            "requested_time": requested["time"],
            "actual_time": {"start_s": actual_start, "end_s": actual_end, "duration_s": actual_end - actual_start},
            "dynamic_window_s": config.dynamic_window_s,
            "refresh_step_s": config.refresh_step_s,
            "preprocessing": payload["filter_contract"],
            "welch": payload["welch_contract"],
        }
        canonical = json.dumps(execution, sort_keys=True, separators=(",", ":"), ensure_ascii=True)
        payload.update({
            "algorithm_version": "offline-spectral-v4-configurable",
            "baseline_algorithm_version": "offline-spectral-v3",
            "requested_config": requested,
            "execution_config": execution,
            "requested_start_s": config.time.start_s,
            "requested_end_s": config.time.end_s,
            "requested_window_s": requested_duration,
            "actual_start_s": actual_start,
            "actual_end_s": actual_end,
            "actual_duration_s": actual_end - actual_start,
            "analysis_config_hash": hashlib.sha256(canonical.encode("utf-8")).hexdigest()[:12].upper(),
            "warmup": config.mode == "dynamic" and payload["window_duration_s"] < config.dynamic_window_s,
        })
        return payload

    def load_configured_spectrogram(self, recording: RecordingSummary, config: AnalysisConfigRequest) -> dict:
        """Return spectrogram data with the same traceability envelope as PSD."""
        if config.mode != "spectrogram":
            raise ValueError("时频图配置的 mode 必须为 spectrogram")
        requested = config.model_dump(mode="json")
        requested_duration = config.time.end_s - config.time.start_s
        payload = self.load_spectrogram(recording, config.time.start_s, requested_duration, config.channels)
        actual_start = float(payload["window_start_s"])
        actual_end = actual_start + float(payload["window_duration_s"])
        # The UI sends milliseconds while the reader slices on sample indices.
        # Permit the same one-sample rounding tolerance as configured PSD.
        sample_tolerance = 1.5 / float(payload.get("sfreq_hz", 1.0))
        if config.time.end_s > actual_end + sample_tolerance:
            raise ValueError(f"时频图分析区间超出文件范围，文件实际结束时间为 {actual_end:.3f} s")
        custom_range = config.custom_frequency_range
        if custom_range is not None:
            from app.eeg_core.spectral import band_power
            import numpy as np
            freqs = np.asarray(payload["frequencies_hz"], dtype=float)
            source = payload.get("power_linear", payload["power"])
            custom_series: dict[str, list[float]] = {}
            for name in payload["channels"]:
                rows = np.asarray(source[name], dtype=float)
                custom_series[name] = [float(band_power(freqs, row, custom_range.low_hz, custom_range.high_hz)) if np.isfinite(row).all() else float("nan") for row in rows]
            payload["custom_band_power_timeseries"] = custom_series
            payload["custom_band"] = {
                "low_hz": custom_range.low_hz,
                "high_hz": custom_range.high_hz,
                "unit": "uV^2",
                "integration": "trapezoid_with_interpolated_boundaries",
                "frequency_resolution_hz": float(freqs[1] - freqs[0]) if len(freqs) > 1 else None,
                "frequency_points_hz": freqs[(freqs >= custom_range.low_hz) & (freqs <= custom_range.high_hz)].tolist(),
                "algorithm_version": "spectrogram-custom-band-v1",
            }
        execution = {"mode": "spectrogram", "channels": payload["channels"], "requested_time": requested["time"], "actual_time": {"start_s": actual_start, "end_s": actual_end, "duration_s": actual_end - actual_start}, "segment_s": payload["segment_s"], "step_s": payload["step_s"], "frequency_range_hz": [1.0, 30.0], "custom_frequency_range": requested.get("custom_frequency_range"), "time_axis": "window_center", "matrix_shape": [len(payload["times_s"]), len(payload["frequencies_hz"])], "quality_gate": "shared_peak_threshold_per_window"}
        canonical = json.dumps(execution, sort_keys=True, separators=(",", ":"), ensure_ascii=True)
        time_bins = len(payload["times_s"])
        frequency_bins = len(payload["frequencies_hz"])
        payload.update({"algorithm_version": "spectrogram-v2-configurable", "analysis_algorithm_version": "offline-spectral-v3", "spectrogram_contract_version": "spectrogram-v2", "baseline_algorithm_version": "spectrogram-v2", "requested_config": requested, "execution_config": execution, "requested_start_s": config.time.start_s, "requested_end_s": config.time.end_s, "requested_window_s": requested_duration, "actual_start_s": actual_start, "actual_end_s": actual_end, "actual_duration_s": actual_end - actual_start, "analysis_config_hash": hashlib.sha256(canonical.encode("utf-8")).hexdigest()[:12].upper(), "warmup": False, "time_bins": time_bins, "frequency_bins": frequency_bins, "matrix_shape": [time_bins, frequency_bins], "first_center_s": payload["times_s"][0] if time_bins else None, "last_center_s": payload["times_s"][-1] if time_bins else None})
        return payload

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
            definition = build_montage(
                montage_id, available, channels, average_exclude, custom_montage,
            )
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
                "settings": {"low_cut_hz": low, "high_cut_hz": high, "notch_hz": notch_hz, "baseline_stabilization": bool(baseline_stabilization), "reference": ref_mode, "montage": definition.id, "average_exclude": list(definition.excluded_channels), "custom_montage": custom_montage or [], "filter_contract": DISPLAY_FILTER_CONTRACT},
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
            source_sha256=row["source_sha256"],
            file_size_bytes=row["file_size_bytes"],
            raw_channel_labels=tuple(json.loads(row["raw_channel_labels_json"] or "[]")),
            canonical_channel_labels=tuple(json.loads(row["canonical_channel_labels_json"] or "[]")),
            channel_types=tuple(json.loads(row["channel_types_json"] or "[]")),
            channel_units=tuple(json.loads(row["channel_units_json"] or "[]")),
            import_version=row["import_version"],
        )
