"""Opt-in, bounded ordered delivery for replayed TNK review frames."""

from __future__ import annotations

import logging
import threading
from collections import OrderedDict
from typing import Any

from app.algorithm_runtime.on_demand import (
    DynamicWindowIdentity,
    DynamicWindowResult,
    window_identity_key,
)
from app.algorithms.tnk.playback_delivery import TnkPlaybackEventBuffer
from app.algorithms.tnk.playback_event import TnkPlaybackFrameEvent
from app.persistence.repositories.dynamic_window import DynamicWindowRepository


class TnkPlaybackDeliveryService:
    """Own delivery-session buffers independently from scientific windows."""

    def __init__(self, repository: DynamicWindowRepository, logger: logging.Logger,
                 *, capacity: int = 2048, max_sessions: int = 32) -> None:
        if type(capacity) is not int or capacity < 1:
            raise ValueError("TNK playback event capacity must be positive")
        if type(max_sessions) is not int or max_sessions < 1:
            raise ValueError("TNK playback session capacity must be positive")
        self.repository = repository
        self.logger = logger
        self.capacity = capacity
        self.max_sessions = max_sessions
        self._lock = threading.RLock()
        self._buffers: OrderedDict[
            str, tuple[str, TnkPlaybackEventBuffer, dict[str, Any]]
        ] = OrderedDict()

    def prepare(self, requested: bool, delivery_id: str, scientific_session_id: str,
                target_identity: DynamicWindowIdentity, sampling_rate_hz: float,
                capability: Any, target_window_id: str):
        if not requested:
            return None, None
        if (target_identity.algorithm_id != "tnk_biofeedback" or
                not hasattr(capability, "execute_window_with_replayed_frames")):
            return {"state": "unsupported", "code": "TNK_PLAYBACK_UNSUPPORTED"}, None
        with self._lock:
            entry = self._buffers.get(delivery_id)
            if entry is None:
                if len(self._buffers) >= self.max_sessions:
                    return {"state": "capacity", "code": "TNK_PLAYBACK_SESSION_CAPACITY"}, None
                buffer = TnkPlaybackEventBuffer(delivery_id, capacity=self.capacity)
                outcome: dict[str, Any] = {"state": "enabled", "delivery_id": delivery_id}
                self._buffers[delivery_id] = (scientific_session_id, buffer, outcome)
            elif entry[0] != scientific_session_id:
                return {"state": "failure", "code": "TNK_PLAYBACK_SESSION_MISMATCH"}, None
            else:
                buffer, outcome = entry[1], entry[2]
                self._buffers.move_to_end(delivery_id)
                if outcome.get("state") == "failure":
                    return dict(outcome), None

        outcome.update({
            "state": "enabled", "delivery_id": delivery_id,
            "events_path": f"/api/analysis-windows/playback/{delivery_id}/events",
            "target_window_id": target_window_id,
        })

        def publish(frame_identity: DynamicWindowIdentity, result: DynamicWindowResult) -> None:
            try:
                normalized_result = result.model_copy(update={
                    "axes": {key: value.tolist() if hasattr(value, "tolist") else value
                             for key, value in result.axes.items()},
                    "arrays": {key: value.tolist() if hasattr(value, "tolist") else value
                               for key, value in result.arrays.items()},
                })
                buffer.publish(lambda sequence: TnkPlaybackFrameEvent(
                    delivery_sequence=sequence,
                    session_id=scientific_session_id,
                    delivery_id=delivery_id,
                    window_id=window_identity_key(frame_identity),
                    identity=frame_identity,
                    window_start_s=frame_identity.start_sample / sampling_rate_hz,
                    window_end_s=frame_identity.end_sample / sampling_rate_hz,
                    sampling_rate_hz=sampling_rate_hz,
                    result=normalized_result,
                ))
            except Exception as exc:
                outcome.update({"state": "failure", "code": "TNK_PLAYBACK_PUBLISH_FAILED",
                                "message": str(exc)[:500]})
                self.logger.exception("tnk_playback_event_publish_failed delivery_id=%s", delivery_id)

        return outcome, publish

    def events(self, delivery_id: str, *, after_sequence: int = 0,
               limit: int = 256, project_id: str | None = None) -> dict[str, Any]:
        with self._lock:
            entry = self._buffers.get(delivery_id)
            if entry is not None:
                self._buffers.move_to_end(delivery_id)
        if entry is None:
            raise KeyError("TNK playback delivery is not enabled for this session")
        session = self.repository.get_session(entry[0])
        if session is None or session.project_id != project_id:
            raise KeyError("dynamic session not found in project scope")
        batch = entry[1].read_after(after_sequence, limit=limit).model_dump(mode="json")
        batch["delivery"] = dict(entry[2])
        return batch

    def close(self, delivery_id: str, *, project_id: str | None = None) -> None:
        with self._lock:
            entry = self._buffers.get(delivery_id)
        if entry is None:
            return
        session = self.repository.get_session(entry[0])
        if session is None or session.project_id != project_id:
            raise KeyError("dynamic session not found in project scope")
        with self._lock:
            self._buffers.pop(delivery_id, None)
