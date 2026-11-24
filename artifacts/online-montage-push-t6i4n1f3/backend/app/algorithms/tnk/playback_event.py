"""Typed identity-safe event contract for ordered TNK playback frames."""

from __future__ import annotations

from datetime import datetime, timezone
from math import isclose
from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field, StrictInt, model_validator

from app.algorithm_runtime.on_demand import (
    DynamicWindowIdentity,
    DynamicWindowResult,
    window_identity_key,
)


PositiveSequence = Annotated[StrictInt, Field(gt=0)]


class TnkPlaybackFrameEvent(BaseModel):
    """One completed TNK canonical frame with transport and scientific identity."""

    model_config = ConfigDict(frozen=True, extra="forbid")

    delivery_sequence: PositiveSequence
    session_id: str = Field(min_length=1)
    delivery_id: str = Field(min_length=1)
    window_id: str = Field(min_length=1)
    window_id_kind: Literal["scientific_identity_key"] = "scientific_identity_key"
    identity: DynamicWindowIdentity
    window_start_s: float = Field(ge=0)
    window_end_s: float = Field(gt=0)
    sampling_rate_hz: float = Field(gt=0)
    published_at_utc: datetime = Field(default_factory=lambda: datetime.now(timezone.utc))
    result: DynamicWindowResult

    @model_validator(mode="after")
    def validate_frame(self) -> TnkPlaybackFrameEvent:
        if self.identity.algorithm_id != "tnk_biofeedback":
            raise ValueError("TNK playback event requires the TNK group identity")
        if self.identity.end_sample <= self.identity.start_sample:
            raise ValueError("TNK frame must use a positive half-open sample range")
        if self.window_end_s <= self.window_start_s:
            raise ValueError("TNK frame must use a positive recording-relative time range")
        expected_start = self.identity.start_sample / self.sampling_rate_hz
        expected_end = self.identity.end_sample / self.sampling_rate_hz
        if not isclose(self.window_start_s, expected_start, rel_tol=0.0, abs_tol=1e-9):
            raise ValueError("TNK frame start time must match its sample boundary")
        if not isclose(self.window_end_s, expected_end, rel_tol=0.0, abs_tol=1e-9):
            raise ValueError("TNK frame end time must match its sample boundary")
        if self.result.identity != self.identity:
            raise ValueError("TNK frame result identity must match the event identity")
        if self.window_id != window_identity_key(self.identity):
            raise ValueError("TNK frame window_id must be its stable scientific identity key")
        if self.result.state in {"preparing_source", "queued", "computing_window", "cancelled"}:
            raise ValueError("TNK playback events must describe a completed canonical frame")
        if self.published_at_utc.tzinfo is None or self.published_at_utc.utcoffset() is None:
            raise ValueError("TNK frame publication time must include a timezone")
        if self.published_at_utc.utcoffset().total_seconds() != 0:
            raise ValueError("TNK frame publication time must be UTC")
        return self
