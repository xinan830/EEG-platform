"""Typed contract for reusable continuous preprocessing sources.

The contract is deliberately independent from a dynamic window.  A cache hit
is valid only when the source identity, prepared range, channels, units and
preprocessing manifest still describe the same scientific input.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any


@dataclass(frozen=True)
class PreparedSourceContract:
    schema_version: str
    identity: str
    status: str
    sample_rate_hz: float
    prepared_start_sample: int
    prepared_end_sample: int
    valid_start_sample: int
    valid_end_sample: int
    channel_names: tuple[str, ...]
    channel_units: tuple[str, ...]
    preprocessing_manifest: str
    numerical_tolerance: float

    def __post_init__(self) -> None:
        if self.schema_version != "prepared-source-v1":
            raise ValueError("unsupported prepared-source contract version")
        if self.status not in {"ready", "unavailable"}:
            raise ValueError("invalid prepared-source status")
        if self.sample_rate_hz <= 0:
            raise ValueError("prepared-source sample rate must be positive")
        if self.prepared_start_sample < 0 or self.prepared_end_sample <= self.prepared_start_sample:
            raise ValueError("prepared-source range is invalid")
        if self.valid_start_sample < self.prepared_start_sample or self.valid_end_sample > self.prepared_end_sample:
            raise ValueError("prepared-source valid range exceeds prepared range")
        if self.valid_end_sample <= self.valid_start_sample:
            raise ValueError("prepared-source valid range is invalid")
        if len(self.channel_names) == 0 or len(self.channel_names) != len(self.channel_units):
            raise ValueError("prepared-source channels and units must align")
        if not self.preprocessing_manifest:
            raise ValueError("prepared-source preprocessing manifest is required")
        if self.numerical_tolerance < 0:
            raise ValueError("prepared-source numerical tolerance must be non-negative")

    def matches(self, expected: "PreparedSourceContract") -> bool:
        """Return whether this entry can satisfy the expected scientific source."""
        return (
            self == expected
            and self.status == "ready"
            and expected.status == "ready"
        )

    def as_dict(self) -> dict[str, Any]:
        return {
            "schema_version": self.schema_version,
            "identity": self.identity,
            "status": self.status,
            "sample_rate_hz": self.sample_rate_hz,
            "prepared_range": {
                "start_sample": self.prepared_start_sample,
                "end_sample": self.prepared_end_sample,
            },
            "valid_range": {
                "start_sample": self.valid_start_sample,
                "end_sample": self.valid_end_sample,
            },
            "channel_names": list(self.channel_names),
            "channel_units": list(self.channel_units),
            "preprocessing_manifest": self.preprocessing_manifest,
            "numerical_tolerance": self.numerical_tolerance,
        }
