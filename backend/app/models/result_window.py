"""P0 contracts for saved windows; these models do not expose an HTTP route."""

from __future__ import annotations

from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field, model_validator

SampleBoundary = Annotated[int, Field(strict=True, ge=0, le=(1 << 63) - 1)]


class SampleRange(BaseModel):
    """Half-open recording-relative range, including gap positions."""

    model_config = ConfigDict(frozen=True, extra="forbid")
    start_sample: SampleBoundary
    end_sample: SampleBoundary

    @model_validator(mode="after")
    def validate_order(self) -> SampleRange:
        if self.end_sample <= self.start_sample:
            raise ValueError("sample range must be nonempty and ordered")
        return self


class SavedResultWindow(SampleRange):
    window_id: str = Field(min_length=1)
    window_index: Annotated[int, Field(strict=True, ge=0)]
    window_state: Literal["Partial", "Complete"]
    quality_status: Literal["valid", "rejected", "unavailable"]
    reason_codes: tuple[str, ...] = ()

    @model_validator(mode="after")
    def require_quality_reason(self) -> SavedResultWindow:
        if self.quality_status != "valid" and not self.reason_codes:
            raise ValueError("unusable windows must retain quality reason codes")
        if any(not code.strip() for code in self.reason_codes):
            raise ValueError("quality reason codes must not be empty")
        return self


class WindowSelection(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")
    cursor_sample: SampleBoundary
    status: Literal["available", "rejected", "unavailable", "insufficient_data", "gap"]
    window: SavedResultWindow | None = None
    reason_codes: tuple[str, ...] = ()
    has_uncovered_tail: bool = False
