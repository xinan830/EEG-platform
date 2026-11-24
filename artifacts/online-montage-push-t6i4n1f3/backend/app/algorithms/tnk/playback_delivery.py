"""Bounded, resumable in-process delivery for TNK playback frame events."""

from __future__ import annotations

import threading
from collections import OrderedDict, deque
from collections.abc import Callable
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, StrictInt

from app.algorithm_runtime.on_demand import window_identity_key

from .playback_event import TnkPlaybackFrameEvent


class TnkPlaybackDiscontinuity(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    first_missing_sequence: StrictInt = Field(gt=0)
    last_missing_sequence: StrictInt = Field(gt=0)
    affected_start_sample: StrictInt = Field(ge=0)
    affected_end_sample: StrictInt = Field(gt=0)
    sample_range_exact: bool


class TnkPlaybackEventBatch(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    delivery_id: str = Field(min_length=1)
    events: tuple[TnkPlaybackFrameEvent, ...]
    next_sequence: StrictInt = Field(ge=0)
    oldest_available_sequence: StrictInt | None = Field(default=None, gt=0)
    latest_sequence: StrictInt = Field(ge=0)
    has_more: bool
    coverage: Literal["complete", "incomplete"]
    discontinuity: TnkPlaybackDiscontinuity | None = None


class TnkPlaybackEventBuffer:
    """A per-session bounded event ring; overflow is always visible to readers."""

    _MAX_RECENT_IDENTITIES = 4096
    _MAX_READ_BATCH = 512

    def __init__(self, delivery_id: str, *, capacity: int = 2048) -> None:
        if not delivery_id:
            raise ValueError("TNK playback event buffer requires a delivery identity")
        if type(capacity) is not int or capacity < 1:
            raise ValueError("TNK playback event buffer capacity must be positive")
        self.delivery_id = delivery_id
        self.capacity = capacity
        self._lock = threading.RLock()
        self._events: deque[TnkPlaybackFrameEvent] = deque()
        self._recent_identities: OrderedDict[str, TnkPlaybackFrameEvent] = OrderedDict()
        self._evicted_metadata: deque[tuple[int, int, int]] = deque(maxlen=capacity)
        self._first_evicted_sequence: int | None = None
        self._last_evicted_sequence = 0
        self._all_evicted_start_sample: int | None = None
        self._all_evicted_end_sample: int | None = None
        self._latest_sequence = 0
        self._latest_order_key: tuple[int, int, int] | None = None

    def publish(self, build_event: Callable[[int], TnkPlaybackFrameEvent]) -> TnkPlaybackFrameEvent:
        """Build and append one event atomically, assigning its delivery sequence."""
        with self._lock:
            next_sequence = self._latest_sequence + 1
            event = build_event(next_sequence)
            if event.delivery_id != self.delivery_id:
                raise ValueError("TNK playback event delivery identity does not match its buffer")
            if event.delivery_sequence != next_sequence:
                raise ValueError("TNK playback event must use the assigned delivery sequence")

            identity_key = window_identity_key(event.identity)
            if identity_key in self._recent_identities:
                previous = self._recent_identities[identity_key]
                self._recent_identities.move_to_end(identity_key)
                return previous

            order_key = (
                event.identity.start_sample,
                event.identity.end_sample,
                event.identity.generation,
            )
            if self._latest_order_key is not None and order_key < self._latest_order_key:
                raise ValueError("TNK playback events must be published in canonical window order")

            self._events.append(event)
            self._recent_identities[identity_key] = event
            self._latest_sequence = next_sequence
            self._latest_order_key = order_key
            while len(self._recent_identities) > self._MAX_RECENT_IDENTITIES:
                self._recent_identities.popitem(last=False)
            if len(self._events) > self.capacity:
                evicted = self._events.popleft()
                self._record_eviction(evicted)
            return event

    def read_after(self, after_sequence: int, *, limit: int = 256) -> TnkPlaybackEventBatch:
        if type(after_sequence) is not int or after_sequence < 0:
            raise ValueError("TNK playback resume sequence must be a non-negative integer")
        if type(limit) is not int or limit < 1 or limit > self._MAX_READ_BATCH:
            raise ValueError(f"TNK playback read limit must be between 1 and {self._MAX_READ_BATCH}")
        with self._lock:
            if after_sequence > self._latest_sequence:
                raise ValueError("TNK playback resume sequence exceeds the latest published sequence")
            oldest = self._events[0].delivery_sequence if self._events else None
            discontinuity = None
            if oldest is not None and after_sequence < oldest - 1:
                discontinuity = self._describe_discontinuity(after_sequence + 1, oldest - 1)
            start_after = max(after_sequence, oldest - 1) if oldest is not None else after_sequence
            events = tuple(event for event in self._events if event.delivery_sequence > start_after)[:limit]
            next_sequence = events[-1].delivery_sequence if events else start_after
            has_more = any(event.delivery_sequence > next_sequence for event in self._events)
            return TnkPlaybackEventBatch(
                delivery_id=self.delivery_id,
                events=events,
                next_sequence=next_sequence,
                oldest_available_sequence=oldest,
                latest_sequence=self._latest_sequence,
                has_more=has_more,
                coverage="incomplete" if discontinuity else "complete",
                discontinuity=discontinuity,
            )

    def _record_eviction(self, event: TnkPlaybackFrameEvent) -> None:
        sequence = event.delivery_sequence
        start_sample = event.identity.start_sample
        end_sample = event.identity.end_sample
        self._evicted_metadata.append((sequence, start_sample, end_sample))
        if self._first_evicted_sequence is None:
            self._first_evicted_sequence = sequence
        self._last_evicted_sequence = sequence
        self._all_evicted_start_sample = (
            start_sample if self._all_evicted_start_sample is None
            else min(self._all_evicted_start_sample, start_sample)
        )
        self._all_evicted_end_sample = max(self._all_evicted_end_sample or 0, end_sample)

    def _describe_discontinuity(self, first: int, last: int) -> TnkPlaybackDiscontinuity:
        evicted = [item for item in self._evicted_metadata if first <= item[0] <= last]
        exact = bool(evicted) and evicted[0][0] == first and evicted[-1][0] == last
        if exact:
            start_sample = min(item[1] for item in evicted)
            end_sample = max(item[2] for item in evicted)
        else:
            start_sample = self._all_evicted_start_sample or 0
            end_sample = self._all_evicted_end_sample or start_sample + 1
        return TnkPlaybackDiscontinuity(
            first_missing_sequence=first,
            last_missing_sequence=last,
            affected_start_sample=start_sample,
            affected_end_sample=end_sample,
            sample_range_exact=exact,
        )
