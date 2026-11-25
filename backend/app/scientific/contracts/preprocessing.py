"""Shared stateless scientific preprocessing contracts for live and offline pipelines."""

from __future__ import annotations

from dataclasses import dataclass
import hashlib
import json
import math

from app.scientific.contracts.analysis import ANALYSIS_CONTRACT


LIVE_SPECTRAL_LOW_HZ = 1.0
LIVE_SPECTRAL_HIGH_HZ = 50.0
DEFAULT_NOTCH_Q = float(ANALYSIS_CONTRACT["notch_quality_factor"])


@dataclass(frozen=True)
class ScientificFilterSpec:
    """Immutable filter contract shared by live and offline scientific preprocessing."""

    low_hz: float
    high_hz: float
    notch_hz: float | None = 50.0
    notch_q: float = DEFAULT_NOTCH_Q
    filter_phase: str = "zero_phase"
    scope: str = "window_local_zero_phase"

    def validate(self, sampling_rate_hz: float) -> None:
        rate = float(sampling_rate_hz)
        if not math.isfinite(rate) or rate <= 0:
            raise ValueError("采样率必须为有限正数")
        nyquist = rate / 2.0
        if not math.isfinite(self.low_hz) or not math.isfinite(self.high_hz):
            raise ValueError("滤波频率必须为有限数值")
        if self.low_hz <= 0 or self.high_hz <= self.low_hz:
            raise ValueError("高通频率必须大于 0 且小于低通频率")
        if self.high_hz >= nyquist:
            raise ValueError(f"低通频率 {self.high_hz:g} Hz 必须低于奈奎斯特频率 {nyquist:g} Hz")
        if self.notch_hz is not None:
            if not math.isfinite(self.notch_hz) or self.notch_hz <= 0 or self.notch_hz >= nyquist:
                raise ValueError("陷波频率必须为正数且低于奈奎斯特频率")
        if not math.isfinite(self.notch_q) or self.notch_q <= 0:
            raise ValueError("陷波品质因数 Q 必须为有限正数")

    def fingerprint(self) -> str:
        payload = json.dumps(
            {
                "low_hz": round(float(self.low_hz), 6),
                "high_hz": round(float(self.high_hz), 6),
                "notch_hz": None if self.notch_hz is None else round(float(self.notch_hz), 6),
                "notch_q": round(float(self.notch_q), 6),
                "filter_phase": self.filter_phase,
                "scope": self.scope,
            },
            sort_keys=True,
            separators=(",", ":"),
        )
        return hashlib.sha256(payload.encode("utf-8")).hexdigest()[:16]

    def to_evidence(self) -> dict[str, object]:
        return {
            "preprocessing_scope": self.scope,
            "filter_phase": self.filter_phase,
            "filter_bandpass_hz": [float(self.low_hz), float(self.high_hz)],
            "notch_hz": None if self.notch_hz is None else float(self.notch_hz),
            "notch_quality_factor": float(self.notch_q) if self.notch_hz is not None else None,
            "spec_fingerprint": self.fingerprint(),
        }


def live_spectral_filter_spec(notch_hz: float | None = 50.0) -> ScientificFilterSpec:
    """Canonical 1-50 Hz live window scientific filter contract."""
    return ScientificFilterSpec(
        low_hz=LIVE_SPECTRAL_LOW_HZ,
        high_hz=LIVE_SPECTRAL_HIGH_HZ,
        notch_hz=notch_hz,
        notch_q=DEFAULT_NOTCH_Q,
        filter_phase="zero_phase",
        scope="window_local_zero_phase",
    )
