"""Filtered montage preview from the bounded display waveform path."""
import logging
import os
import time
from dataclasses import dataclass

import numpy as np


logger = logging.getLogger("brain_platform.signal_preview")


@dataclass(frozen=True)
class _DisplayProjection:
    elapsed_s: list[float]
    channels: dict[str, list[float]]
    stride_samples: int
    extrema_elapsed_s: list[float] | None
    channel_extrema: dict[str, dict[str, list[float]]] | None


def signal_preview(service, recording_id: str, *, start_s: float, window_s: float,
                   montage_id: str | None, channels: list[str], max_points: int,
                   low_hz: float = 1.0, high_hz: float = 30.0, notch_hz: float | None = None,
                   request_id: str = "unknown") -> dict:
    service_started = time.perf_counter()
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
    preparation_started = time.perf_counter()
    window = service.load_window(
        recording,
        start_s=start_s,
        window_s=window_s,
        low_cut_hz=low_hz,
        high_cut_hz=high_hz,
        notch_hz=notch_hz,
        reference="original",
        channels=channels,
        montage="original",
    )
    window_read_filter_ms = (time.perf_counter() - preparation_started) * 1000
    slice_started = time.perf_counter()
    sfreq = float(window["sfreq"])
    elapsed_s = list(window["elapsed_s"])
    start = int(np.floor(float(window["window_start_s"]) * sfreq))
    stop = start + len(elapsed_s)
    if not elapsed_s:
        raise ValueError("窗口数据不足")
    if len(elapsed_s) * len(channels) > 2_000_000:
        raise ValueError("预览窗口超出 200 万样本值预算，请缩短窗口或减少通道")
    values = np.column_stack([
        np.asarray(window["channels"][name], dtype=np.float64) * 1e-6
        for name in channels
    ])
    if not np.all(np.isfinite(values)):
        raise ValueError("预览信号包含非有限数值")
    slice_validation_ms = (time.perf_counter() - slice_started) * 1000

    projection_started = time.perf_counter()
    projection = _project_display_samples(values, channels, start, sfreq, max_points)
    projection_ms = (time.perf_counter() - projection_started) * 1000
    result = {
        "recording_id": recording_id, "montage_id": montage_id,
        "unit": "uV", "filter": "display_causal_iir", "sfreq": sfreq,
        "low_hz": low_hz, "high_hz": high_hz, "notch_hz": notch_hz,
        "start_s": start / sfreq, "end_s": stop / sfreq,
        "elapsed_s": projection.elapsed_s,
        "channels": projection.channels,
        "display_stride_samples": projection.stride_samples,
        "extrema_elapsed_s": projection.extrema_elapsed_s,
        "channel_extrema": projection.channel_extrema,
    }
    _log_preview_diagnostics(
        request_id=request_id,
        recording_id=recording_id,
        window_read_filter_ms=window_read_filter_ms,
        slice_validation_ms=slice_validation_ms,
        projection_ms=projection_ms,
        channel_count=len(channels),
        source_sample_count=len(values),
        projection=projection,
        total_service_ms=(time.perf_counter() - service_started) * 1000,
    )
    return result


def _project_display_samples(values: np.ndarray, channels: list[str], start: int,
                             sfreq: float, max_points: int) -> _DisplayProjection:
    stride = max(1, int(np.ceil(len(values) / max_points)))
    elapsed_s = ((start + np.arange(0, len(values), stride)) / sfreq).tolist()
    channel_values = {
        name: (values[::stride, index] * 1e6).tolist()
        for index, name in enumerate(channels)
    }
    if stride == 1:
        return _DisplayProjection(elapsed_s, channel_values, stride, None, None)

    extrema_times: list[float] = []
    minimum_values = {name: [] for name in channels}
    maximum_values = {name: [] for name in channels}
    for offset in range(0, len(values), stride):
        bucket = values[offset:offset + stride, :]
        extrema_times.append((start + offset + (len(bucket) - 1) / 2) / sfreq)
        minima = np.min(bucket, axis=0) * 1e6
        maxima = np.max(bucket, axis=0) * 1e6
        for index, name in enumerate(channels):
            minimum_values[name].append(float(minima[index]))
            maximum_values[name].append(float(maxima[index]))

    channel_extrema = {
        name: {"minimum_uv": minimum_values[name], "maximum_uv": maximum_values[name]}
        for name in channels
    }
    return _DisplayProjection(elapsed_s, channel_values, stride, extrema_times, channel_extrema)


def _log_preview_diagnostics(*, request_id: str, recording_id: str,
                             window_read_filter_ms: float, slice_validation_ms: float,
                             projection_ms: float, channel_count: int,
                             source_sample_count: int, projection: _DisplayProjection,
                             total_service_ms: float) -> None:
    if os.environ.get("BRAIN_REVIEW_PERF_DIAGNOSTICS") != "1":
        return

    logger.info(
        "signal_preview_perf request_id=%s recording_id=%s window_read_filter_ms=%.1f "
        "window_slice_validation_ms=%.1f display_projection_ms=%.1f channel_count=%s "
        "source_sample_count=%s displayed_point_count=%s display_stride_samples=%s "
        "envelope_bin_count=%s total_service_ms=%.1f",
        request_id, recording_id, window_read_filter_ms, slice_validation_ms, projection_ms,
        channel_count, source_sample_count, len(projection.elapsed_s), projection.stride_samples,
        len(projection.extrema_elapsed_s) if projection.extrema_elapsed_s is not None else 0,
        total_service_ms,
    )
