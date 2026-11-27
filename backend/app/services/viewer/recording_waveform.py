"""Bounded display waveform reads and their causal filter checkpoints."""

from collections.abc import Callable

import numpy as np

from app.models.recording import RecordingSummary
from app.services.viewer.filter_checkpoint_cache import FilterCheckpointCache
from app.services.viewer.montage import apply_montage, build_montage
from app.services.recordings.recording_access import RecordingAccessSession
from app.services.viewer.waveform_filter import DISPLAY_FILTER_CONTRACT, DisplaySignalFilter


class RecordingWaveformService:
    """Read display-only samples without owning recording metadata or analysis."""

    def __init__(self, open_session: Callable[..., RecordingAccessSession]):
        self._open_session = open_session
        self._filter_checkpoints = FilterCheckpointCache()

    def invalidate_recording(self, recording_id: str) -> None:
        self._filter_checkpoints.clear_recording(recording_id)

    def load_preview_data(
        self,
        recording: RecordingSummary,
        start_s: float,
        window_s: float,
    ) -> tuple[np.ndarray, float, list[str], list[dict[str, object]], float]:
        """只读取预览所需的时间窗，避免为查看波形加载完整记录。"""
        with self._open_session(recording, scientific_channels=False) as session:
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
        custom_montage: list[dict[str, object]] | None = None,
        baseline_stabilization: bool = False,
    ) -> dict:
        """按需读取并处理一个阅图窗口；原始文件始终保持不变。"""
        low = float(low_cut_hz)
        high = float(high_cut_hz)
        if not 0 < low < high:
            raise ValueError("低切必须小于高切")
        if notch_hz is not None and float(notch_hz) not in (50.0, 60.0):
            raise ValueError("陷波仅支持关闭、50Hz 或 60Hz")

        access_session = self._open_session(recording, scientific_channels=False)
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
