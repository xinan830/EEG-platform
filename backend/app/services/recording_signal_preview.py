"""Filtered montage preview from the canonical continuous prepared signal."""
import numpy as np
from dataclasses import dataclass
from app.services.dynamic_window_resources import DynamicWindowResourcePolicy


@dataclass(frozen=True)
class PreviewPreparationControl:
    preparation_resource_policy: DynamicWindowResourcePolicy = DynamicWindowResourcePolicy(
        max_preparation_bytes=512 * 1024**2)

    def check_cancelled(self):
        pass


def signal_preview(service, recording_id: str, *, start_s: float, window_s: float,
                   montage_id: str | None, channels: list[str], max_points: int,
                   low_hz: float = 1.0, high_hz: float = 30.0, notch_hz: float | None = None) -> dict:
    recording = service.require_recording(recording_id)
    if montage_id:
        recording = service.select_analysis_montage(recording, montage_id)
    if not channels or len(channels) > 32 or len({name.casefold() for name in channels}) != len(channels):
        raise ValueError("预览需要 1–32 个不重复通道")
    if not 0 < low_hz < high_hz or notch_hz not in (None, 50.0, 60.0):
        raise ValueError("预览滤波范围无效，陷波仅支持关闭、50 或 60 Hz")
    if recording.sfreq is not None and high_hz >= recording.sfreq / 2:
        raise ValueError("预览高切必须低于奈奎斯特频率")
    if recording.duration_s is not None and start_s >= recording.duration_s:
        raise ValueError("预览窗口超出记录范围")
    if recording.sfreq is not None and window_s * recording.sfreq * len(channels) > 2_000_000:
        raise ValueError("预览窗口超出 200 万样本值预算")
    available = {name.casefold(): name for name in recording.channels}
    if any(name.casefold() not in available for name in channels):
        raise ValueError("预览通道不属于当前导联")
    channels = [available[name.casefold()] for name in channels]
    prepared = service.prepare_signal_preview(recording, low_hz, high_hz, notch_hz, channels)
    sfreq = prepared.sfreq
    prepared_lookup = {name.casefold(): index for index, name in enumerate(prepared.channel_names)}
    indexes = [prepared_lookup[name.casefold()] for name in channels]
    start = int(np.floor(start_s * sfreq))
    stop = min(len(prepared.data), int(np.ceil((start_s + window_s) * sfreq)))
    if start < 0 or start >= stop:
        raise ValueError("预览窗口超出记录范围")
    if (stop - start) * len(channels) > 2_000_000:
        raise ValueError("预览窗口超出 200 万样本值预算，请缩短窗口或减少通道")
    values = np.asarray(prepared.data[start:stop, :][:, indexes], dtype=np.float64)
    if not np.all(np.isfinite(values)):
        raise ValueError("预览信号包含非有限数值")
    step = max(1, int(np.ceil(len(values) / max_points)))
    return {
        "recording_id": recording_id, "montage_id": montage_id,
        "unit": "uV", "filter": "continuous_zero_phase", "sfreq": sfreq,
        "low_hz": low_hz, "high_hz": high_hz, "notch_hz": notch_hz,
        "start_s": start / sfreq, "end_s": stop / sfreq,
        "elapsed_s": ((start + np.arange(0, len(values), step)) / sfreq).tolist(),
        "channels": {name: (values[::step, i] * 1e6).tolist() for i, name in enumerate(channels)},
        "display_stride_samples": step,
    }
