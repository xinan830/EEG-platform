"""独立于分析流程的 BDF/EDF 连续波形回放基础组件。"""

from __future__ import annotations

import asyncio
import queue
import threading
import time
from typing import Any
from uuid import uuid4

import numpy as np
from scipy import signal

from app.models.recording import RecordingSummary
from app.services.waveform_wire import WAVEFORM_BINARY_ENCODING, encode_waveform_binary


DISPLAY_CHANNEL_GROUPS = (
    ("FZ",),
    ("PZ",),
    ("OZ", "O2", "O1"),
    ("F3",),
    ("F4",),
)

DEFAULT_DISPLAY_FILTERS = {
    "low_cut_hz": 0.5,
    "high_cut_hz": 70.0,
    "notch_hz": None,
}


def select_display_channels(
    names: list[str],
    limit: int = 5,
    requested_names: list[str] | None = None,
) -> tuple[list[int], list[str]]:
    """选择显示通道；用户选择时保持其顺序，未选择时使用默认五通道。"""
    available = [str(name) for name in names]
    by_upper = {name.upper(): index for index, name in enumerate(available)}
    if requested_names is not None:
        indices = []
        for requested in requested_names:
            index = by_upper.get(str(requested).upper())
            if index is not None and index not in indices:
                indices.append(index)
        if not indices:
            raise ValueError("所选显示通道不存在于录制文件")
        return indices, [available[index] for index in indices]
    indices: list[int] = []
    for candidates in DISPLAY_CHANNEL_GROUPS:
        index = next((by_upper[name] for name in candidates if name in by_upper), None)
        if index is not None and index not in indices:
            indices.append(index)
        if len(indices) == limit:
            break
    for index in range(len(available)):
        if len(indices) == limit:
            break
        if index not in indices:
            indices.append(index)
    return indices, [available[index] for index in indices]


class DisplaySignalFilter:
    """连续显示滤波；只作用于回放副本，绝不改写导入文件。"""

    def __init__(
        self,
        sfreq: float,
        channel_count: int,
        notch_freq: float | None = None,
        bp_low: float = 0.5,
        bp_high: float = 70.0,
    ):
        self.sfreq = float(sfreq)
        self.channel_count = int(channel_count)
        if self.channel_count <= 0:
            raise ValueError("至少需要一个 EEG 通道")
        if not 0 < float(bp_low) < float(bp_high) < self.sfreq / 2:
            raise ValueError("低切必须小于高切，且高切必须低于奈奎斯特频率")
        if notch_freq is not None and not 0 < float(notch_freq) < self.sfreq / 2:
            raise ValueError("陷波频率必须低于奈奎斯特频率")
        self.notch_freq = None if notch_freq is None else float(notch_freq)
        self.b_notch: np.ndarray | None = None
        self.a_notch: np.ndarray | None = None
        if self.notch_freq is not None:
            self.b_notch, self.a_notch = signal.iirnotch(self.notch_freq, 30.0, self.sfreq)
        self.b_bp, self.a_bp = signal.butter(4, [bp_low, bp_high], btype="bandpass", fs=self.sfreq)
        self.dc_offset: np.ndarray | None = None
        self.zi_notch: list[np.ndarray] | None = None
        self.zi_bp: list[np.ndarray] | None = None

    def process(self, samples: np.ndarray) -> np.ndarray:
        """处理 `(samples, channels)` 输入，返回保持相同形状的伏特值。"""
        chunk = np.asarray(samples, dtype=float)
        if chunk.ndim != 2 or chunk.shape[1] != self.channel_count:
            raise ValueError("EEG chunk 必须是 (samples, selected_channels) 矩阵")
        if chunk.shape[0] == 0:
            return chunk.copy()

        extracted = chunk.T
        if self.dc_offset is None:
            self.dc_offset = np.mean(extracted, axis=1, keepdims=True)
        self.dc_offset = 0.99 * self.dc_offset + 0.01 * np.mean(extracted, axis=1, keepdims=True)
        no_dc = extracted - self.dc_offset

        if self.zi_bp is None:
            if self.b_notch is not None and self.a_notch is not None:
                self.zi_notch = [
                    signal.lfilter_zi(self.b_notch, self.a_notch) * no_dc[index, 0]
                    for index in range(self.channel_count)
                ]
            self.zi_bp = [
                signal.lfilter_zi(self.b_bp, self.a_bp) * no_dc[index, 0]
                for index in range(self.channel_count)
            ]

        filtered = np.zeros_like(no_dc)
        for index in range(self.channel_count):
            current = no_dc[index]
            if self.b_notch is not None and self.a_notch is not None and self.zi_notch is not None:
                current, self.zi_notch[index] = signal.lfilter(
                    self.b_notch, self.a_notch, current, zi=self.zi_notch[index],
                )
            current, self.zi_bp[index] = signal.lfilter(
                self.b_bp, self.a_bp, current, zi=self.zi_bp[index],
            )
            filtered[index] = current
        return filtered.T


class WaveformPlaybackSession:
    """只负责波形的连续 BDF/EDF 回放，不依赖分析通道映射。"""

    def __init__(self, recording: RecordingSummary, recordings: Any, requested_channels: list[str] | None = None):
        self.id = uuid4().hex
        self.recording_id = recording.id
        self._recording = recording
        self._recordings = recordings
        self._outbound: queue.Queue[dict[str, Any] | bytes] = queue.Queue(maxsize=240)
        self._control: queue.Queue[dict[str, Any]] = queue.Queue()
        self._stop = threading.Event()
        self._paused = threading.Event()
        self._speed = 1.0
        self._filter_settings = dict(DEFAULT_DISPLAY_FILTERS)
        self._requested_channels = requested_channels
        self._channels_changed = False
        self._thread: threading.Thread | None = None
        self.status = "created"

    def start(self) -> None:
        if self._thread is None:
            self._thread = threading.Thread(target=self._run, daemon=True, name=f"waveform-{self.id[:8]}")
            self._thread.start()

    def control(self, action: str, **payload: Any) -> dict[str, Any]:
        allowed = {"pause", "resume", "restart", "seek", "set_speed", "set_filters", "set_channels", "stop"}
        if action not in allowed:
            raise ValueError("不支持的波形回放控制指令")
        if action == "set_speed" and float(payload.get("speed", 0)) not in {1.0, 2.0, 4.0}:
            raise ValueError("回放倍速仅支持 1x、2x、4x")
        if action == "seek" and float(payload.get("position_s", -1)) < 0:
            raise ValueError("定位时间不能小于 0")
        if action == "set_filters":
            self._validate_filter_settings({**self._filter_settings, **payload})
        if action == "set_channels":
            channels = payload.get("channels")
            if not isinstance(channels, list) or not channels or not all(isinstance(name, str) and name.strip() for name in channels):
                raise ValueError("至少选择一个有效显示通道")
        self._control.put({"action": action, **payload})
        return {"session_id": self.id, "status": self.status, "action": action}

    @staticmethod
    def _validate_filter_settings(settings: dict[str, Any]) -> None:
        low_cut = float(settings.get("low_cut_hz", DEFAULT_DISPLAY_FILTERS["low_cut_hz"]))
        high_cut = float(settings.get("high_cut_hz", DEFAULT_DISPLAY_FILTERS["high_cut_hz"]))
        notch = settings.get("notch_hz")
        if not 0 < low_cut < high_cut:
            raise ValueError("低切必须小于高切")
        if notch is not None and float(notch) not in (50.0, 60.0):
            raise ValueError("当前阅图器仅支持关闭、50Hz 或 60Hz 陷波")

    def _create_filter(self, sfreq: float, channel_count: int) -> DisplaySignalFilter:
        return DisplaySignalFilter(
            sfreq=sfreq,
            channel_count=channel_count,
            notch_freq=self._filter_settings["notch_hz"],
            bp_low=self._filter_settings["low_cut_hz"],
            bp_high=self._filter_settings["high_cut_hz"],
        )

    @staticmethod
    def _resolve_reset_position(current: int, seek_to: float | None, sample_count: int, sfreq: float) -> int:
        """参数重建默认从文件开头；显式定位仍可覆盖目标位置。"""
        if seek_to is None:
            return 0
        return min(sample_count, max(0, int(seek_to * sfreq)))

    def _emit(self, payload: dict[str, Any] | bytes) -> None:
        try:
            self._outbound.put_nowait(payload)
        except queue.Full:
            try:
                self._outbound.get_nowait()
            except queue.Empty:
                pass
            self._outbound.put_nowait(payload)

    def _drain_controls(self) -> tuple[bool, float | None]:
        reset = False
        seek_to: float | None = None
        while True:
            try:
                command = self._control.get_nowait()
            except queue.Empty:
                return reset, seek_to
            action = command["action"]
            if action == "pause":
                self._paused.set()
                self.status = "paused"
            elif action == "resume":
                self._paused.clear()
                self.status = "running"
            elif action == "restart":
                reset = True
                seek_to = 0.0
                self._paused.clear()
                self.status = "running"
            elif action == "seek":
                reset = True
                seek_to = float(command["position_s"])
                self._paused.clear()
                self.status = "running"
            elif action == "set_speed":
                self._speed = float(command["speed"])
            elif action == "set_filters":
                self._filter_settings = {
                    **self._filter_settings,
                    **{key: command[key] for key in ("low_cut_hz", "high_cut_hz", "notch_hz") if key in command},
                }
                reset = True
                # 修改任一滤波参数后统一从文件开头重新播放，避免新旧参数混在同一段波形中。
                seek_to = 0.0
                self._paused.clear()
                self.status = "running"
            elif action == "set_channels":
                self._requested_channels = list(command["channels"])
                self._channels_changed = True
                reset = True
                seek_to = 0.0
                self._paused.clear()
                self.status = "running"
            elif action == "stop":
                self._stop.set()
                self.status = "stopped"

    def _run(self) -> None:
        try:
            raw_data, sfreq, all_names, events = self._recordings.load_data(self._recording)
            data = np.asarray(raw_data, dtype=float)
            indices, names = select_display_channels(list(all_names), requested_names=self._requested_channels)
            if not indices:
                raise RuntimeError("录制文件不包含可显示的 EEG 通道")
            selected = data[:, indices]
            chunk_samples = max(1, int(round(sfreq * 0.05)))
            position = 0
            event_index = 0
            display_filter = self._create_filter(sfreq, len(names))
            self.status = "running"
            self._emit({
                "type": "info", "sfreq": sfreq, "ch_names": names,
                "duration_s": len(selected) / sfreq, "start_s": 0.0,
                "filters": self._filter_settings, "waveform_encoding": WAVEFORM_BINARY_ENCODING,
            })

            while not self._stop.is_set() and position < len(selected):
                reset, seek_to = self._drain_controls()
                if reset:
                    # 参数变化与重播均在同一时间基准重新初始化滤波器。
                    position = self._resolve_reset_position(position, seek_to, len(selected), sfreq)
                    if self._channels_changed:
                        indices, names = select_display_channels(list(all_names), requested_names=self._requested_channels)
                        selected = data[:, indices]
                        self._channels_changed = False
                        self._emit({
                            "type": "info", "sfreq": sfreq, "ch_names": names,
                            "duration_s": len(selected) / sfreq, "start_s": position / sfreq,
                            "filters": self._filter_settings, "waveform_encoding": WAVEFORM_BINARY_ENCODING,
                        })
                    event_index = next((index for index, event in enumerate(events) if float(event["elapsed_s"]) >= position / sfreq), len(events))
                    display_filter = self._create_filter(sfreq, len(names))
                    self._emit({"type": "reset", "start_s": position / sfreq, "filters": self._filter_settings})
                if self._paused.is_set():
                    time.sleep(0.02)
                    continue

                end = min(len(selected), position + chunk_samples)
                chunk = selected[position:end]
                filtered_uv = display_filter.process(chunk) * 1e6
                position = end
                elapsed_s = position / sfreq
                self._emit(encode_waveform_binary(elapsed_s, filtered_uv))
                while event_index < len(events) and float(events[event_index]["elapsed_s"]) <= elapsed_s:
                    self._emit({"type": "event", **events[event_index]})
                    event_index += 1
                time.sleep(len(chunk) / sfreq / self._speed)

            if not self._stop.is_set():
                self.status = "completed"
                self._emit({"type": "completed", "elapsed_s": len(selected) / sfreq})
        except Exception as exc:
            self.status = "failed"
            self._emit({"type": "error", "detail": str(exc)})

    async def next_message(self) -> dict[str, Any] | bytes:
        return await asyncio.to_thread(self._outbound.get)


class WaveformPlaybackService:
    def __init__(self, recordings: Any):
        self.recordings = recordings
        self.sessions: dict[str, WaveformPlaybackSession] = {}

    def create(self, recording_id: str, requested_channels: list[str] | None = None) -> WaveformPlaybackSession:
        recording = self.recordings.require_recording(recording_id)
        session = WaveformPlaybackSession(recording, self.recordings, requested_channels)
        self.sessions[session.id] = session
        session.start()
        return session

    def require(self, session_id: str) -> WaveformPlaybackSession:
        try:
            return self.sessions[session_id]
        except KeyError as exc:
            raise KeyError("波形回放会话不存在") from exc
