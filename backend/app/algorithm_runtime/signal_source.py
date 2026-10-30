"""Typed bounded signal-source contract shared by runtimes and algorithm adapters."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Literal

import numpy as np


SignalUnit = Literal["V"]
MissingSamplePolicy = Literal["preserve_nan", "reject"]
PreprocessingScope = Literal[
    "none", "configured_continuous_before_slice", "fixed_continuous_before_slice",
    "continuous_before_slice", "per_window", "module_defined",
]


@dataclass(frozen=True)
class SignalSourceRequest:
    """A bounded, role-ordered request against one immutable signal identity."""

    roles: tuple[str, ...]
    start_sample: int
    end_sample: int
    expected_sfreq_hz: float
    montage_id: str
    montage_fingerprint: str
    preprocessing_scope: PreprocessingScope
    low_hz: float | None = None
    high_hz: float | None = None
    notch_hz: float | None = None
    missing_sample_policy: MissingSamplePolicy = "preserve_nan"

    def __post_init__(self) -> None:
        if not self.roles or any(not role.strip() for role in self.roles):
            raise ValueError("signal source roles must be non-empty")
        if len({role.casefold() for role in self.roles}) != len(self.roles):
            raise ValueError("signal source roles must be unique")
        if self.start_sample < 0 or self.end_sample <= self.start_sample:
            raise ValueError("signal source sample bounds must be non-negative and ordered")
        if not np.isfinite(self.expected_sfreq_hz) or self.expected_sfreq_hz <= 0:
            raise ValueError("signal source sampling rate must be finite and positive")
        if not self.montage_id or not self.montage_fingerprint:
            raise ValueError("signal source requires an explicit montage identity")
        if (self.low_hz is None) != (self.high_hz is None):
            raise ValueError("signal source filter requires both low and high cutoff")
        if self.low_hz is not None and (self.low_hz <= 0 or self.high_hz <= self.low_hz):
            raise ValueError("signal source filter bounds are invalid")
        if self.notch_hz is not None and self.notch_hz <= 0:
            raise ValueError("signal source notch frequency must be positive")


@dataclass(frozen=True)
class SpectralSourceRequest:
    """Typed request for a bounded spectrum or spectrogram from source roles."""

    roles: tuple[str, ...]
    start_sample: int
    end_sample: int
    expected_sfreq_hz: float
    montage_id: str
    montage_fingerprint: str
    filter_low_hz: float
    filter_high_hz: float
    output_low_hz: float
    output_high_hz: float
    notch_hz: float | None
    representation: Literal["spectrum", "spectrogram"] = "spectrum"
    preprocessing_scope: Literal[
        "configured_continuous_before_slice", "tnk_window_scoped_v1",
    ] = "configured_continuous_before_slice"

    def __post_init__(self) -> None:
        if not self.roles or any(not role.strip() for role in self.roles):
            raise ValueError("spectral source roles must be non-empty")
        if len({role.casefold() for role in self.roles}) != len(self.roles):
            raise ValueError("spectral source roles must be unique")
        if self.start_sample < 0 or self.end_sample <= self.start_sample:
            raise ValueError("spectral source sample bounds must be non-negative and ordered")
        if not np.isfinite(self.expected_sfreq_hz) or self.expected_sfreq_hz <= 0:
            raise ValueError("spectral source sampling rate must be finite and positive")
        if not self.montage_id or not self.montage_fingerprint:
            raise ValueError("spectral source requires explicit montage identity")
        if not all(np.isfinite(value) for value in (
            self.filter_low_hz, self.filter_high_hz,
            self.output_low_hz, self.output_high_hz,
        )):
            raise ValueError("spectral source frequency bounds must be finite")
        if self.filter_low_hz <= 0 or self.filter_high_hz <= self.filter_low_hz:
            raise ValueError("spectral source filter range is invalid")
        if self.output_low_hz <= 0 or self.output_high_hz <= self.output_low_hz:
            raise ValueError("spectral source output range is invalid")
        if self.notch_hz is not None and self.notch_hz <= 0:
            raise ValueError("spectral source notch frequency must be positive")
        if self.representation not in {"spectrum", "spectrogram"}:
            raise ValueError("spectral source representation is unsupported")
        if self.preprocessing_scope not in {
            "configured_continuous_before_slice", "tnk_window_scoped_v1",
        }:
            raise ValueError("spectral source preprocessing scope is unsupported")


@dataclass(frozen=True)
class SignalSourceData:
    """Sample-major V/float64 values and evidence for the exact bounded request."""

    values_v: np.ndarray
    role_to_output: dict[str, str]
    channel_order: tuple[str, ...]
    sfreq_hz: float
    start_sample: int
    end_sample: int
    unit: SignalUnit
    missing_sample_policy: MissingSamplePolicy
    montage_id: str
    montage_fingerprint: str
    preprocessing_scope: PreprocessingScope
    preprocessing: dict[str, float | None]
    provenance: dict[str, object]

    def __post_init__(self) -> None:
        values = np.asarray(self.values_v)
        if values.dtype != np.dtype(np.float64):
            raise ValueError("signal source values must use float64")
        if values.ndim != 2 or values.shape != (self.end_sample - self.start_sample, len(self.channel_order)):
            raise ValueError("signal source values must be sample-major and match declared bounds/channels")
        normalized_channels = {name.casefold() for name in self.channel_order}
        if len(normalized_channels) != len(self.channel_order):
            raise ValueError("signal source channel names must be unique")
        if (not self.role_to_output
                or any(output.casefold() not in normalized_channels for output in self.role_to_output.values())):
            raise ValueError("signal source role mapping must resolve to declared channel outputs")
        if len({output.casefold() for output in self.role_to_output.values()}) != len(self.role_to_output):
            raise ValueError("signal source role mapping must preserve distinct output channels")
        if set(self.role_to_output) == set() or set(self.role_to_output.values()) != set(self.channel_order):
            raise ValueError("signal source role mapping must cover channel order exactly")
        if not np.isfinite(self.sfreq_hz) or self.sfreq_hz <= 0:
            raise ValueError("signal source sampling rate must be finite and positive")
        if self.start_sample < 0 or self.end_sample <= self.start_sample:
            raise ValueError("signal source sample bounds must be non-negative and ordered")
        if not self.montage_id or not self.montage_fingerprint:
            raise ValueError("signal source requires explicit montage identity")
        if self.unit != "V":
            raise ValueError("signal source unit must be V")
        if self.missing_sample_policy not in {"preserve_nan", "reject"}:
            raise ValueError("signal source missing-sample policy is unsupported")
        if not isinstance(self.preprocessing, dict) or not isinstance(self.provenance, dict) or not self.provenance:
            raise ValueError("signal source preprocessing and provenance must be explicit")
        if self.missing_sample_policy == "reject" and not np.isfinite(values).all():
            raise ValueError("signal source contains missing or non-finite samples")
