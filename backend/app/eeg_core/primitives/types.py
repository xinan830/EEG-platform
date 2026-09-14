"""Immutable scientific values used by the constrained research node layer."""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Literal

import numpy as np

from .units import Unit


class PrimitiveValueError(ValueError):
    """Raised when a scientific value violates shape, time, or channel rules."""


def _readonly(values: np.ndarray | list[float]) -> np.ndarray:
    copied = np.array(values, dtype=np.float64, copy=True)
    copied.setflags(write=False)
    return copied


@dataclass(frozen=True)
class ProvenanceStep:
    node: str
    parameters: tuple[tuple[str, str], ...] = ()

    @classmethod
    def create(cls, node: str, **parameters: object) -> "ProvenanceStep":
        return cls(node, tuple(sorted((key, repr(value)) for key, value in parameters.items())))


Provenance = tuple[ProvenanceStep, ...]


def append_provenance(provenance: Provenance, node: str, **parameters: object) -> Provenance:
    return provenance + (ProvenanceStep.create(node, **parameters),)


@dataclass(frozen=True)
class TimeRange:
    start_s: float
    end_s: float

    def __post_init__(self) -> None:
        if not np.isfinite([self.start_s, self.end_s]).all() or self.end_s <= self.start_s:
            raise PrimitiveValueError("time range must be finite with end_s > start_s")

    @property
    def center_s(self) -> float:
        return (self.start_s + self.end_s) / 2.0


@dataclass(frozen=True)
class ChannelMap:
    labels: tuple[str, ...]
    canonical_labels: tuple[str, ...] | None = None
    channel_types: tuple[str, ...] = ()

    def __post_init__(self) -> None:
        labels = tuple(self.labels)
        if not labels or any(not label.strip() for label in labels):
            raise PrimitiveValueError("channel labels must be non-empty")
        if len({label.casefold() for label in labels}) != len(labels):
            raise PrimitiveValueError("channel labels must be unique ignoring case")
        canonical = self.canonical_labels or labels
        if len(canonical) != len(labels):
            raise PrimitiveValueError("canonical labels must match channel labels")
        channel_types = self.channel_types or tuple("eeg" for _ in labels)
        if len(channel_types) != len(labels):
            raise PrimitiveValueError("channel types must match channel labels")
        object.__setattr__(self, "labels", labels)
        object.__setattr__(self, "canonical_labels", tuple(canonical))
        object.__setattr__(self, "channel_types", tuple(channel_types))

    def indices(self, requested: tuple[str, ...] | list[str]) -> tuple[int, ...]:
        source = {label.casefold(): index for index, label in enumerate(self.labels)}
        wanted = tuple(requested)
        if not wanted or len({item.casefold() for item in wanted}) != len(wanted):
            raise PrimitiveValueError("requested channels must be unique and non-empty")
        missing = [item for item in wanted if item.casefold() not in source]
        if missing:
            raise PrimitiveValueError(f"missing channels: {', '.join(missing)}")
        return tuple(source[item.casefold()] for item in wanted)

    def select(self, requested: tuple[str, ...] | list[str]) -> "ChannelMap":
        indices = self.indices(requested)
        return ChannelMap(
            tuple(self.labels[index] for index in indices),
            tuple(self.canonical_labels[index] for index in indices),
            tuple(self.channel_types[index] for index in indices),
        )


@dataclass(frozen=True)
class QualityMask:
    status: Literal["clean", "bad"] = "clean"
    reasons: tuple[str, ...] = ()
    rejected_reasons: tuple[str, ...] = ()

    def __post_init__(self) -> None:
        reasons = tuple(dict.fromkeys(self.reasons))
        rejected_reasons = tuple(dict.fromkeys(self.rejected_reasons + reasons))
        if self.status == "clean" and reasons:
            raise PrimitiveValueError("clean quality cannot contain rejection reasons")
        if self.status == "bad" and not reasons:
            raise PrimitiveValueError("bad quality requires a rejection reason")
        object.__setattr__(self, "reasons", reasons)
        object.__setattr__(self, "rejected_reasons", rejected_reasons)

    @property
    def is_available(self) -> bool:
        return self.status == "clean"

    @classmethod
    def combine(cls, masks: tuple["QualityMask", ...] | list["QualityMask"]) -> "QualityMask":
        unavailable_reasons = tuple(dict.fromkeys(reason for mask in masks if not mask.is_available for reason in mask.reasons))
        rejected_reasons = tuple(dict.fromkeys(reason for mask in masks for reason in mask.rejected_reasons))
        return cls("bad", unavailable_reasons, rejected_reasons) if unavailable_reasons else cls("clean", (), rejected_reasons)


@dataclass(frozen=True)
class EEGSignal:
    values: np.ndarray
    sfreq_hz: float
    channels: ChannelMap
    time: TimeRange
    unit: Unit = Unit.V
    quality: QualityMask = field(default_factory=QualityMask)
    provenance: Provenance = ()

    def __post_init__(self) -> None:
        values = _readonly(self.values)
        if values.ndim != 2 or not len(values) or values.shape[1] != len(self.channels.labels):
            raise PrimitiveValueError("EEGSignal values must be non-empty samples-by-channels")
        if self.unit not in (Unit.V, Unit.UV) or not np.isfinite(self.sfreq_hz) or self.sfreq_hz <= 0:
            raise PrimitiveValueError("EEGSignal requires a voltage unit and positive sampling rate")
        expected = len(values) / self.sfreq_hz
        if not np.isclose(self.time.end_s - self.time.start_s, expected, rtol=0.0, atol=1.0 / self.sfreq_hz):
            raise PrimitiveValueError("EEGSignal time range must match samples and sampling rate")
        object.__setattr__(self, "values", values)


@dataclass(frozen=True)
class WindowedSignal:
    values: np.ndarray
    sfreq_hz: float
    channels: ChannelMap
    ranges: tuple[TimeRange, ...]
    window_length_s: float
    step_s: float
    residual_policy: Literal["drop", "reject"]
    unit: Unit = Unit.V
    quality: tuple[QualityMask, ...] = ()
    provenance: Provenance = ()

    def __post_init__(self) -> None:
        values = _readonly(self.values)
        if values.ndim != 3 or not len(values) or values.shape[2] != len(self.channels.labels):
            raise PrimitiveValueError("WindowedSignal values must be windows-by-samples-by-channels")
        if len(self.ranges) != values.shape[0] or len(self.quality or self.ranges) != values.shape[0]:
            raise PrimitiveValueError("each window requires time and quality metadata")
        if self.unit not in (Unit.V, Unit.UV) or self.window_length_s <= 0 or self.step_s <= 0:
            raise PrimitiveValueError("WindowedSignal requires voltage units and positive window settings")
        expected = int(round(self.window_length_s * self.sfreq_hz))
        if values.shape[1] != expected:
            raise PrimitiveValueError("window sample count must match window length and sampling rate")
        quality = self.quality or tuple(QualityMask() for _ in self.ranges)
        object.__setattr__(self, "values", values)
        object.__setattr__(self, "quality", tuple(quality))


@dataclass(frozen=True)
class PSDSeries:
    values: np.ndarray | None
    frequencies_hz: np.ndarray
    channels: ChannelMap
    time: TimeRange
    unit: Unit = Unit.V2_PER_HZ
    quality: QualityMask = field(default_factory=QualityMask)
    provenance: Provenance = ()

    def __post_init__(self) -> None:
        frequencies = _readonly(self.frequencies_hz)
        if frequencies.ndim != 1 or not len(frequencies) or not np.all(np.diff(frequencies) > 0):
            raise PrimitiveValueError("PSD frequency axis must be a non-empty ascending vector")
        if self.unit not in (Unit.V2_PER_HZ, Unit.UV2_PER_HZ):
            raise PrimitiveValueError("PSDSeries requires a density unit")
        values = None if self.values is None else _readonly(self.values)
        if values is not None and (values.shape != (len(self.channels.labels), len(frequencies)) or not np.isfinite(values).all()):
            raise PrimitiveValueError("PSD values must be finite channels-by-frequency data")
        if values is None and self.quality.is_available:
            raise PrimitiveValueError("available PSD must contain values")
        object.__setattr__(self, "frequencies_hz", frequencies)
        object.__setattr__(self, "values", values)


@dataclass(frozen=True)
class BandPower:
    values: np.ndarray | None
    channels: ChannelMap
    band_hz: tuple[float, float]
    unit: Unit = Unit.V2
    quality: QualityMask = field(default_factory=QualityMask)
    provenance: Provenance = ()

    def __post_init__(self) -> None:
        low, high = self.band_hz
        if not 0 <= low < high or self.unit not in (Unit.V2, Unit.UV2):
            raise PrimitiveValueError("BandPower requires a valid band and power unit")
        values = None if self.values is None else _readonly(self.values)
        if values is not None and (values.shape != (len(self.channels.labels),) or not np.isfinite(values).all()):
            raise PrimitiveValueError("BandPower values must be finite and ordered by channel")
        if values is None and self.quality.is_available:
            raise PrimitiveValueError("available BandPower must contain values")
        object.__setattr__(self, "values", values)


@dataclass(frozen=True)
class RelativePower:
    values: np.ndarray | None
    channels: ChannelMap
    band_hz: tuple[float, float]
    unit: Unit = Unit.RATIO
    quality: QualityMask = field(default_factory=QualityMask)
    provenance: Provenance = ()

    def __post_init__(self) -> None:
        values = None if self.values is None else _readonly(self.values)
        if self.unit != Unit.RATIO or values is not None and (values.shape != (len(self.channels.labels),) or not np.isfinite(values).all()):
            raise PrimitiveValueError("RelativePower values must be finite ratios ordered by channel")
        if values is None and self.quality.is_available:
            raise PrimitiveValueError("available RelativePower must contain values")
        object.__setattr__(self, "values", values)


@dataclass(frozen=True)
class Scalar:
    value: float | None
    unit: Unit
    quality: QualityMask = field(default_factory=QualityMask)
    provenance: Provenance = ()

    def __post_init__(self) -> None:
        if self.value is not None and not np.isfinite(self.value):
            raise PrimitiveValueError("Scalar value must be finite")
        if self.value is None and self.quality.is_available:
            raise PrimitiveValueError("available Scalar must contain a value")


@dataclass(frozen=True)
class TimeSeries:
    values: np.ndarray | None
    times_s: np.ndarray
    unit: Unit
    quality: QualityMask = field(default_factory=QualityMask)
    provenance: Provenance = ()

    def __post_init__(self) -> None:
        times = _readonly(self.times_s)
        values = None if self.values is None else _readonly(self.values)
        if times.ndim != 1 or not len(times) or not np.all(np.diff(times) > 0):
            raise PrimitiveValueError("TimeSeries times must be ascending")
        if values is not None and (values.shape != times.shape or not np.isfinite(values).all()):
            raise PrimitiveValueError("TimeSeries values must align to finite timestamps")
        if values is None and self.quality.is_available:
            raise PrimitiveValueError("available TimeSeries must contain values")
        object.__setattr__(self, "times_s", times)
        object.__setattr__(self, "values", values)
