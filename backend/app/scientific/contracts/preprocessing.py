"""Shared stateless scientific preprocessing contracts for live and offline pipelines."""

from __future__ import annotations

from dataclasses import dataclass
import hashlib
import json
import math
from typing import Literal

from app.scientific.contracts.analysis import ANALYSIS_CONTRACT
from app.scientific.contracts.artifact_removal import ArtifactRemovalMethod, ArtifactRemovalSpec


LIVE_SPECTRAL_LOW_HZ = 1.0
LIVE_SPECTRAL_HIGH_HZ = 50.0
DEFAULT_NOTCH_Q = float(ANALYSIS_CONTRACT["notch_quality_factor"])
STANDARD_MONTAGE_NAME = "colin27_1020"
ARTIFACT_DETECTION_VERSION = 'continuous-artifact-report-v3-exact-ptp'

BadChannelPolicy = Literal["none", "exclude", "interpolate"]
_VALID_BAD_CHANNEL_POLICIES = frozenset({"none", "exclude", "interpolate"})
ArtifactRejectionPolicy = Literal["none", "annotate", "reject"]
_VALID_ARTIFACT_REJECTION_POLICIES = frozenset({"none", "annotate", "reject"})


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


@dataclass(frozen=True)
class BadChannelSpec:
    """Immutable bad-channel policy applied before re-referencing and filtering."""

    policy: BadChannelPolicy = "none"
    channels: tuple[str, ...] = ()

    def normalized(self) -> BadChannelSpec:
        policy = str(self.policy).strip().casefold()
        if policy not in _VALID_BAD_CHANNEL_POLICIES:
            raise ValueError(f"不支持的坏导处理策略：{self.policy!r}")
        deduped: list[str] = []
        seen: set[str] = set()
        for raw in self.channels:
            label = str(raw).strip()
            if not label:
                continue
            folded = label.casefold()
            if folded not in seen:
                seen.add(folded)
                deduped.append(label)
        if not deduped:
            return BadChannelSpec(policy="none", channels=())
        if policy == "none":
            raise ValueError("已指定坏导通道时，坏导处理策略必须为 exclude 或 interpolate")
        return BadChannelSpec(policy=policy, channels=tuple(deduped))  # type: ignore[arg-type]

    @property
    def is_active(self) -> bool:
        return self.policy != "none" and len(self.channels) > 0

    def fingerprint(self) -> str:
        norm = self.normalized()
        if not norm.is_active:
            return "none"
        payload = json.dumps(
            {
                "policy": norm.policy,
                "channels": sorted(label.casefold() for label in norm.channels),
            },
            sort_keys=True,
            separators=(",", ":"),
        )
        return hashlib.sha256(payload.encode("utf-8")).hexdigest()[:16]

    def to_evidence(self) -> dict[str, object]:
        norm = self.normalized()
        return {
            "policy": norm.policy,
            "channels": list(norm.channels),
            "applied_before_rereference": norm.is_active,
            "spec_fingerprint": norm.fingerprint(),
        }


@dataclass(frozen=True)
class ManualArtifactSegment:
    """User-declared bad time interval in recording-relative half-open sample coordinates."""

    start_sample: int
    end_sample: int
    reason: str = "manual_artifact"
    channels: tuple[str, ...] = ()

    def normalized(self) -> ManualArtifactSegment:
        if (
            isinstance(self.start_sample, bool)
            or isinstance(self.end_sample, bool)
            or not isinstance(self.start_sample, int)
            or not isinstance(self.end_sample, int)
            or self.start_sample < 0
            or self.end_sample <= self.start_sample
        ):
            raise ValueError("手动坏段必须使用合法的半开样本区间 [start_sample, end_sample)")
        reason = str(self.reason).strip() or "manual_artifact"
        deduped: list[str] = []
        seen: set[str] = set()
        for raw in self.channels:
            label = str(raw).strip()
            if label and label.casefold() not in seen:
                seen.add(label.casefold())
                deduped.append(label)
        return ManualArtifactSegment(
            start_sample=int(self.start_sample),
            end_sample=int(self.end_sample),
            reason=reason,
            channels=tuple(deduped),
        )


@dataclass(frozen=True)
class ArtifactRejectionSpec:
    """Immutable offline artifact-segment quality control specification."""

    policy: ArtifactRejectionPolicy = "none"
    peak_to_peak_uv: float | None = 300.0
    amplitude_window_s: float = 0.5
    muscle_zscore_threshold: float | None = 4.0
    min_duration_s: float = 0.1
    pad_s: float = 0.1
    manual_segments: tuple[ManualArtifactSegment, ...] = ()

    def normalized(self) -> ArtifactRejectionSpec:
        policy = str(self.policy).strip().casefold()
        if policy not in _VALID_ARTIFACT_REJECTION_POLICIES:
            raise ValueError(f"不支持的坏段质控策略：{self.policy!r}")
        if self.peak_to_peak_uv is not None and (
            not math.isfinite(self.peak_to_peak_uv) or self.peak_to_peak_uv <= 0
        ):
            raise ValueError("爆幅峰峰值阈值（µV）必须为有限正数")
        if not math.isfinite(self.amplitude_window_s) or self.amplitude_window_s <= 0:
            raise ValueError("爆幅检测滑窗长度必须为有限正数")
        if self.muscle_zscore_threshold is not None and (
            not math.isfinite(self.muscle_zscore_threshold) or self.muscle_zscore_threshold <= 0
        ):
            raise ValueError("肌电 z-score 阈值必须为有限正数")
        if not math.isfinite(self.min_duration_s) or self.min_duration_s < 0:
            raise ValueError("坏段最短持续时间不能为负数")
        if not math.isfinite(self.pad_s) or self.pad_s < 0:
            raise ValueError("坏段前后保护扩展时长不能为负数")
        manual = tuple(
            sorted(
                (seg.normalized() for seg in self.manual_segments),
                key=lambda item: (item.start_sample, item.end_sample, item.reason),
            )
        )
        if policy == "none":
            return ArtifactRejectionSpec(
                policy="none",
                peak_to_peak_uv=self.peak_to_peak_uv,
                amplitude_window_s=float(self.amplitude_window_s),
                muscle_zscore_threshold=self.muscle_zscore_threshold,
                min_duration_s=float(self.min_duration_s),
                pad_s=float(self.pad_s),
                manual_segments=(),
            )
        if self.peak_to_peak_uv is None and self.muscle_zscore_threshold is None and not manual:
            raise ValueError("启用坏段质控时必须至少开启爆幅检测、肌电检测或提供手动坏段")
        return ArtifactRejectionSpec(
            policy=policy,  # type: ignore[arg-type]
            peak_to_peak_uv=None if self.peak_to_peak_uv is None else float(self.peak_to_peak_uv),
            amplitude_window_s=float(self.amplitude_window_s),
            muscle_zscore_threshold=(
                None if self.muscle_zscore_threshold is None else float(self.muscle_zscore_threshold)
            ),
            min_duration_s=float(self.min_duration_s),
            pad_s=float(self.pad_s),
            manual_segments=manual,
        )

    @property
    def is_active(self) -> bool:
        return self.policy != "none"

    def fingerprint(self) -> str:
        norm = self.normalized()
        if not norm.is_active:
            return "none"
        payload = json.dumps(
            {
                "policy": norm.policy,
                "peak_to_peak_uv": None if norm.peak_to_peak_uv is None else round(norm.peak_to_peak_uv, 6),
                "detection_version": ARTIFACT_DETECTION_VERSION,
                "amplitude_window_s": round(norm.amplitude_window_s, 6),
                "muscle_zscore_threshold": (
                    None if norm.muscle_zscore_threshold is None else round(norm.muscle_zscore_threshold, 6)
                ),
                "min_duration_s": round(norm.min_duration_s, 6),
                "pad_s": round(norm.pad_s, 6),
                "manual_segments": [
                    {
                        "start": item.start_sample,
                        "end": item.end_sample,
                        "reason": item.reason,
                        "channels": sorted(ch.casefold() for ch in item.channels),
                    }
                    for item in norm.manual_segments
                ],
            },
            sort_keys=True,
            separators=(",", ":"),
        )
        return hashlib.sha256(payload.encode("utf-8")).hexdigest()[:16]

    def to_evidence(self) -> dict[str, object]:
        norm = self.normalized()
        return {
            "policy": norm.policy,
            "peak_to_peak_uv": norm.peak_to_peak_uv,
            "amplitude_window_s": norm.amplitude_window_s,
            "muscle_zscore_threshold": norm.muscle_zscore_threshold,
            "min_duration_s": norm.min_duration_s,
            "pad_s": norm.pad_s,
            "manual_segment_count": len(norm.manual_segments),
            "spec_fingerprint": norm.fingerprint(),
        }


@dataclass(frozen=True)
class OfflinePreprocessingSpec:
    """Unified versioned offline scientific preprocessing contract.

    Stages execute in strict neuroscientific order:
    1) Bad-channel exclusion or spherical spline interpolation (pre-rereference)
    2) Montage projection / re-referencing (REF / CAR / Cz / M1-M2 / bipolar)
    3) Zero-phase scientific bandpass + notch filtering
    4) Artifact-segment quality control (amplitude PTP + MNE muscle z-score + manual bad segments)
    5) Optional future artifact removal hook (explicitly versioned)
    """

    filter_spec: ScientificFilterSpec
    bad_channels: BadChannelSpec = BadChannelSpec()
    artifact_rejection: ArtifactRejectionSpec = ArtifactRejectionSpec()
    artifact_removal: ArtifactRemovalSpec = ArtifactRemovalSpec()

    def validate(self, sampling_rate_hz: float) -> OfflinePreprocessingSpec:
        self.filter_spec.validate(sampling_rate_hz)
        return OfflinePreprocessingSpec(
            filter_spec=self.filter_spec,
            bad_channels=self.bad_channels.normalized(),
            artifact_rejection=self.artifact_rejection.normalized(),
            artifact_removal=self.artifact_removal.normalized(),
        )

    def fingerprint(self) -> str:
        payload = json.dumps(
            {
                "schema_version": "offline-preprocessing-spec-v3",
                "filter": self.filter_spec.fingerprint(),
                "bad_channels": self.bad_channels.fingerprint(),
                "artifact_rejection": self.artifact_rejection.fingerprint(),
                "artifact_removal": self.artifact_removal.fingerprint(),
            },
            sort_keys=True,
            separators=(",", ":"),
        )
        return hashlib.sha256(payload.encode("utf-8")).hexdigest()[:16]

    def to_evidence(self) -> dict[str, object]:
        return {
            **self.filter_spec.to_evidence(),
            "bad_channels": self.bad_channels.to_evidence(),
            "artifact_rejection": self.artifact_rejection.to_evidence(),
            "artifact_removal": self.artifact_removal.to_evidence(),
            "offline_spec_fingerprint": self.fingerprint(),
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
