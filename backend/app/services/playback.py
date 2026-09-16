"""BDF/EDF 回放会话：把原桌面程序的连续处理循环暴露为 WebSocket 帧。"""

from __future__ import annotations

import asyncio
import queue
import threading
import time
from dataclasses import asdict
from typing import Any
from uuid import uuid4

import numpy as np

from app.eeg_core import EEGProcessor
from app.eeg_core.analysis_contract import LIVE_ANALYSIS_CONTRACT
from app.models.recording import RecordingSummary
from app.services.recordings import RecordingService


def _json_safe(value: Any) -> Any:
    """把 numpy 标量和数组转换为 FastAPI/WebSocket 可发送的数据。"""
    if isinstance(value, np.ndarray):
        return value.tolist()
    if isinstance(value, np.generic):
        return value.item()
    if isinstance(value, dict):
        return {str(key): _json_safe(item) for key, item in value.items()}
    if isinstance(value, (list, tuple)):
        return [_json_safe(item) for item in value]
    return value


class PlaybackSession:
    """单个离线录制的连续回放和处理状态。"""

    def __init__(self, recording: RecordingSummary, recordings: RecordingService):
        self.id = uuid4().hex
        self.recording_id = recording.id
        self._recording = recording
        self._recordings = recordings
        self._outbound: queue.Queue[dict[str, Any]] = queue.Queue(maxsize=240)
        self._control: queue.Queue[dict[str, Any]] = queue.Queue()
        self._stop = threading.Event()
        self._paused = threading.Event()
        self._speed = 1.0
        self._thread: threading.Thread | None = None
        self.status = "created"

    def start(self) -> None:
        if self._thread is None:
            self._thread = threading.Thread(target=self._run, daemon=True, name=f"eeg-playback-{self.id[:8]}")
            self._thread.start()

    def control(self, action: str, **payload: Any) -> dict[str, Any]:
        if action not in {"pause", "resume", "restart", "set_speed", "set_filters", "stop"}:
            raise ValueError("不支持的回放控制指令")
        if action == "set_speed" and float(payload.get("speed", 0)) not in {1.0, 2.0, 4.0}:
            raise ValueError("回放倍速仅支持 1x、2x、4x")
        self._control.put({"action": action, **payload})
        return {"session_id": self.id, "status": self.status, "action": action}

    def _emit(self, payload: dict[str, Any]) -> None:
        message = _json_safe(payload)
        try:
            self._outbound.put_nowait(message)
        except queue.Full:
            try:
                self._outbound.get_nowait()
            except queue.Empty:
                pass
            self._outbound.put_nowait(message)

    def _canonical_data(self) -> tuple[np.ndarray, float, list[str], list[dict[str, Any]]]:
        data, sfreq, names, events = self._recordings.load_data(self._recording)
        # Raw recording labels are the only labels valid for playback. Spatial
        # roles are selected by an algorithm per Run, not aliased globally.
        return np.asarray(data, dtype=float), sfreq, list(names), events

    def _drain_controls(self, processor: EEGProcessor) -> bool:
        restart = False
        while True:
            try:
                command = self._control.get_nowait()
            except queue.Empty:
                return restart
            action = command["action"]
            if action == "pause":
                self._paused.set(); self.status = "paused"
            elif action == "resume":
                self._paused.clear(); self.status = "running"
            elif action == "restart":
                restart = True; self._paused.clear(); self.status = "running"
            elif action == "set_speed":
                self._speed = float(command["speed"])
            elif action == "set_filters":
                processor.set_filters(float(command.get("notch", 50)), float(command.get("bp_low", 1)), float(command.get("bp_high", 30)))
            elif action == "stop":
                self._stop.set(); self.status = "stopped"

    def _reset_processor(self, processor: EEGProcessor) -> None:
        processor.reset_buffers()
        processor.start_segment()
        processor.start_rbp_session()
        self._emit({"type": "iapf_status", "locked": False, "iapf": processor.iapf_global, "candidates": 0})

    def _run(self) -> None:
        try:
            data, sfreq, names, events = self._canonical_data()
            processor = EEGProcessor(sfreq, names)
            self._reset_processor(processor)
            self.status = "running"
            self._emit({"type": "info", "sfreq": sfreq, "ch_names": names, "duration_s": len(data) / sfreq,
                        "algorithm_contract": LIVE_ANALYSIS_CONTRACT})
            position = 0
            event_index = 0
            chunk_samples = max(1, int(round(sfreq * 0.05)))
            last_clock = time.monotonic()
            while not self._stop.is_set() and position < len(data):
                if self._drain_controls(processor):
                    position = 0; event_index = 0; self._reset_processor(processor)
                if self._paused.is_set():
                    time.sleep(0.05)
                    continue
                end = min(len(data), position + chunk_samples)
                chunk = data[position:end]
                processor.push_chunk(chunk)
                processor.add_segment_chunk(chunk)
                position = end
                elapsed_s = position / sfreq
                while event_index < len(events) and float(events[event_index]["elapsed_s"]) <= elapsed_s:
                    self._emit({"type": "event", **events[event_index]})
                    event_index += 1
                lock = processor.try_lock_iapf()
                if lock is not None:
                    self._emit({"type": "iapf_status", "locked": True, "iapf": processor.iapf_global,
                                "candidates": len(processor.iapf_lock_candidates), "spectrum": asdict(lock)})
                if processor.should_update():
                    metrics, iapf, iapf_info, visualization, rbp = processor.calculate_metrics()
                    if metrics:
                        self._emit({"type": "metrics", "elapsed_s": processor.elapsed_s(), "metrics": metrics,
                                    "algorithm_version": LIVE_ANALYSIS_CONTRACT["algorithm_version"],
                                    "iapf": iapf, "iapf_live": processor.iapf_live,
                                    "iapf_locked": processor.iapf_locked, "iapf_info": iapf_info,
                                    "rbp": rbp, "fatigue": processor.fatigue_index(), "hai": processor.hai_index(),
                                    "aperiodic": processor.estimate_aperiodic_exponents()})
                self._emit({"type": "waveform", "elapsed_s": elapsed_s, "samples": chunk[-1] * 1e6})
                target_interval = len(chunk) / sfreq / self._speed
                delay = target_interval - (time.monotonic() - last_clock)
                if delay > 0:
                    time.sleep(delay)
                last_clock = time.monotonic()
            if not self._stop.is_set():
                self.status = "completed"
                self._emit({"type": "completed", "elapsed_s": len(data) / sfreq})
        except Exception as exc:
            self.status = "failed"
            self._emit({"type": "error", "detail": str(exc)})

    async def next_message(self) -> dict[str, Any]:
        return await asyncio.to_thread(self._outbound.get)


class PlaybackService:
    def __init__(self, recordings: RecordingService):
        self.recordings = recordings
        self.sessions: dict[str, PlaybackSession] = {}

    def create(self, recording_id: str) -> PlaybackSession:
        recording = self.recordings.require_recording(recording_id)
        session = PlaybackSession(recording, self.recordings)
        self.sessions[session.id] = session
        session.start()
        return session

    def require(self, session_id: str) -> PlaybackSession:
        try:
            return self.sessions[session_id]
        except KeyError as exc:
            raise KeyError("回放会话不存在") from exc
