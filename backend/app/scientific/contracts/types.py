"""Transport-independent contracts shared by scientific primitives and modules."""

from __future__ import annotations

from dataclasses import dataclass, field
import math
from typing import Literal


ScientificUnit = Literal["V", "V^2/Hz", "V^2", "Hz", "s", "ratio", "count"]
QualityStatus = Literal["pass", "warn", "fail", "unavailable"]
QualityLayerName = Literal["DataIntegrity", "SignalQuality", "AlgorithmValidity"]
GapPolicy = Literal["reject", "impute"]
TransformPaddingKind = Literal["none", "fft_boundary"]


@dataclass(frozen=True)
class GapEvidence:
    """Evidence about missing/non-finite recording samples."""

    detected: bool
    policy: GapPolicy
    missing_samples: int = 0
    non_finite_samples: int = 0
    imputed: bool = False

    def __post_init__(self) -> None:
        if min(self.missing_samples, self.non_finite_samples) < 0:
            raise ValueError("gap evidence counts cannot be negative")
        if self.imputed and self.policy != "impute":
            raise ValueError("imputed gap evidence requires the impute policy")
        if not self.detected and (self.missing_samples or self.non_finite_samples or self.imputed):
            raise ValueError("clean gap evidence cannot contain gap counts")

    def as_dict(self) -> dict[str, object]:
        return {
            "detected": self.detected,
            "policy": self.policy,
            "missing_samples": self.missing_samples,
            "non_finite_samples": self.non_finite_samples,
            "imputed": self.imputed,
        }


@dataclass(frozen=True)
class TransformPaddingEvidence:
    """Evidence about samples introduced by a transform, not acquisition."""

    used: bool = False
    kind: TransformPaddingKind = "none"
    samples: int = 0

    def __post_init__(self) -> None:
        if self.samples < 0:
            raise ValueError("transform padding samples cannot be negative")
        if not self.used and (self.kind != "none" or self.samples):
            raise ValueError("unused transform padding must be represented as none/0")
        if self.used and self.kind == "none":
            raise ValueError("used transform padding requires a kind")

    def as_dict(self) -> dict[str, object]:
        return {"used": self.used, "kind": self.kind, "samples": self.samples}


@dataclass(frozen=True)
class SampleRange:
    """Canonical recording-relative half-open sample interval."""

    start_sample: int
    end_sample: int

    def __post_init__(self) -> None:
        if self.start_sample < 0 or self.end_sample <= self.start_sample:
            raise ValueError("sample range must satisfy 0 <= start < end")

    @property
    def length(self) -> int:
        return self.end_sample - self.start_sample

    def seconds(self, sfreq_hz: float) -> tuple[float, float]:
        if sfreq_hz <= 0:
            raise ValueError("sampling rate must be positive")
        return self.start_sample / sfreq_hz, self.end_sample / sfreq_hz

    @classmethod
    def from_seconds(cls, start_s: float, end_s: float, sfreq_hz: float) -> "SampleRange":
        """Convert a requested recording-time range to a half-open sample range.

        The conversion is intentionally centralized: the start is floored so
        no requested input sample is skipped, while the end is rounded to the
        nearest sample boundary. Persisted/display seconds remain a separate
        representation and must not be used as the scientific coordinate.
        """
        values = (start_s, end_s, sfreq_hz)
        if not all(math.isfinite(float(value)) for value in values):
            raise ValueError("sample range conversion values must be finite")
        if sfreq_hz <= 0 or start_s < 0 or end_s <= start_s:
            raise ValueError("sample range conversion requires 0 <= start < end and positive sampling rate")
        start_sample = math.floor(start_s * sfreq_hz)
        end_sample = round(end_s * sfreq_hz)
        if end_sample <= start_sample:
            raise ValueError("requested time range is shorter than one sample")
        return cls(start_sample, end_sample)

    def as_dict(self) -> dict[str, object]:
        return {
            "start_sample": self.start_sample,
            "end_sample": self.end_sample,
            "half_open": True,
        }


@dataclass(frozen=True)
class QualityEvidence:
    status: QualityStatus
    reasons: tuple[str, ...] = ()
    metrics: dict[str, float] = field(default_factory=dict)

    def __post_init__(self) -> None:
        if self.status == "pass" and self.reasons:
            raise ValueError("passing quality evidence cannot contain reasons")
        if self.status in {"fail", "unavailable"} and not self.reasons:
            raise ValueError("failed or unavailable quality requires a reason")


@dataclass(frozen=True)
class QualityLayers:
    data_integrity: QualityEvidence
    signal_quality: QualityEvidence
    algorithm_validity: QualityEvidence


@dataclass(frozen=True)
class AlgorithmQualityContract:
    """Versioned declaration of an algorithm module's quality requirements."""

    algorithm_id: str
    version: str
    required_layers: tuple[QualityLayerName, ...] = (
        "DataIntegrity",
        "SignalQuality",
        "AlgorithmValidity",
    )
    rule_ids: tuple[str, ...] = ()

    def __post_init__(self) -> None:
        if not self.algorithm_id or not self.version:
            raise ValueError("algorithm quality contract identity is required")
        if not self.required_layers:
            raise ValueError("algorithm quality contract must declare quality layers")
        if len(set(self.required_layers)) != len(self.required_layers):
            raise ValueError("algorithm quality contract layers must be unique")
        if any(not rule_id for rule_id in self.rule_ids):
            raise ValueError("algorithm quality rule ids must be non-empty")

    def as_dict(self) -> dict[str, object]:
        return {
            "algorithm_id": self.algorithm_id,
            "version": self.version,
            "required_layers": list(self.required_layers),
            "rule_ids": list(self.rule_ids),
        }


@dataclass(frozen=True)
class PreprocessingSnapshot:
    reference: str
    filters: tuple[str, ...] = ()
    gap_policy: GapPolicy = "reject"
    unit: ScientificUnit = "V"


@dataclass(frozen=True)
class OutputField:
    name: str
    unit: ScientificUnit
    meaning: str
    shape: tuple[int, ...] | None = None


@dataclass(frozen=True)
class OutputSchema:
    fields: tuple[OutputField, ...]

    def __post_init__(self) -> None:
        names = [field.name for field in self.fields]
        if not names or len(set(names)) != len(names):
            raise ValueError("output schema field names must be non-empty and unique")
