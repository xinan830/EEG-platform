"""Live scalar adapters for IAPF, Theta/Beta, and Classic BrainBeat."""

from __future__ import annotations

import math
from typing import Any, Literal

import numpy as np
from pydantic import BaseModel, ConfigDict, Field, JsonValue, field_validator

from app.algorithms.brainbeat import compute_brainbeat
from app.algorithms.brainbeat.config import FZ_SOURCE_CHANNELS, PZ_SOURCE_CHANNELS
from app.algorithms.iapf import compute_iapf
from app.algorithms.theta_beta import compute_theta_beta
from app.scientific.contracts.analysis import ANALYSIS_CONTRACT
from app.scientific.contracts.preprocessing import live_spectral_filter_spec
from app.scientific.primitives import DEFAULT_SPECTRAL_GATEWAY
from app.services.live_analysis.live_analysis_contracts import LiveWindowPlan
from app.services.live_analysis.live_recording_access import LiveRecordingAccessError, LiveRecordingAccessSession
from app.services.live_analysis.live_scientific_preprocessing import LiveScientificPreprocessingService
from app.services.recordings.sample_access_contract import SampleBlock


_SCALAR_SPECS: dict[str, dict[str, Any]] = {
    "iapf": {
        "scientific_identity": "iapf-live-v2",
        "implementation_identity": "iapf-live-window-v4",
        "unit": "Hz",
        "min_window_s": 20.0,
        "default_step_s": 2.0,
        "required_channel_count": 1,
        "output_high_hz": 30.0,
    },
    "theta_beta": {
        "scientific_identity": "theta-beta-live-v1",
        "implementation_identity": "theta-beta-live-window-v1",
        "unit": "dimensionless",
        "min_window_s": 20.0,
        "default_step_s": 1.0,
        "required_channel_count": 1,
        "output_high_hz": 50.0,
    },
    "brainbeat": {
        "scientific_identity": "brainbeat-classic-live-v1",
        "implementation_identity": "brainbeat-classic-live-window-v1",
        "unit": "ratio",
        "min_window_s": 10.0,
        "default_step_s": 1.0,
        "required_channel_count": 2,
        "output_high_hz": 30.0,
    },
}


class LiveScalarConfig(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: Literal["live-scalar-config-v1"] = "live-scalar-config-v1"
    channels: list[str] = Field(min_length=1, max_length=2)
    window_s: float = Field(default=20.0, gt=0, le=30.0)
    step_s: float | None = Field(default=None, gt=0, le=10.0)
    notch_hz: float | None = 50.0
    frequency_mode: Literal["standard", "individualized"] = "standard"

    @field_validator("channels")
    @classmethod
    def channels_are_unique(cls, values: list[str]) -> list[str]:
        cleaned = [value.strip() for value in values]
        if any(not value for value in cleaned) or len({value.casefold() for value in cleaned}) != len(cleaned):
            raise ValueError("实时标量算法通道必须非空且唯一")
        return cleaned

    @field_validator("notch_hz")
    @classmethod
    def notch_is_supported(cls, value: float | None) -> float | None:
        if value is not None and value not in (50.0, 60.0):
            raise ValueError("实时标量算法陷波仅支持关闭、50 Hz 或 60 Hz")
        return value


class LiveScalarResult(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: Literal["live-scalar-result-v1"] = "live-scalar-result-v1"
    type: Literal["live_scalar_result"] = "live_scalar_result"
    acquisition_session_id: str
    algorithm_id: Literal["iapf", "theta_beta", "brainbeat"]
    scientific_identity: str
    implementation_identity: str
    state: Literal["ready", "unavailable"]
    channels: list[str]
    start_sample: int | None = None
    end_sample: int | None = None
    start_s: float | None = None
    end_s: float | None = None
    time_s: float | None = None
    value: float | None = None
    unit: str
    quality: str
    failure: dict[str, object] | None = None
    evidence: dict[str, object] = Field(default_factory=dict)
    provenance: dict[str, object] = Field(default_factory=dict)


class LiveScalarAlgorithm:
    """Live window adapter reusing official IAPF, Theta/Beta, and Classic BrainBeat compute functions."""

    def __init__(
        self,
        algorithm_id: Literal["iapf", "theta_beta", "brainbeat"],
        preprocessor: LiveScientificPreprocessingService | None = None,
    ) -> None:
        if algorithm_id not in _SCALAR_SPECS:
            raise ValueError(f"不支持的实时标量算法：{algorithm_id}")
        spec = _SCALAR_SPECS[algorithm_id]
        self.algorithm_id: Literal["iapf", "theta_beta", "brainbeat"] = algorithm_id
        self.scientific_identity: str = spec["scientific_identity"]
        self.implementation_identity: str = spec["implementation_identity"]
        self._unit: str = spec["unit"]
        self._min_window_s: float = spec["min_window_s"]
        self._default_step_s: float = spec["default_step_s"]
        self._required_channel_count: int = spec["required_channel_count"]
        self._output_high_hz: float = spec["output_high_hz"]
        self._preprocessor = preprocessor or LiveScientificPreprocessingService()

    def configure(
        self,
        source: LiveRecordingAccessSession,
        config: dict[str, JsonValue],
    ) -> tuple[LiveScalarConfig, LiveWindowPlan]:
        requested = LiveScalarConfig.model_validate(config)
        if source.identity.acquisition_session_id == "":
            raise LiveRecordingAccessError("LIVE_SESSION_ID_INVALID", "实时采集会话身份不能为空。")
        if source.sampling_rate_hz / 2.0 <= 50.0:
            raise LiveRecordingAccessError("LIVE_SCALAR_NYQUIST_UNSUPPORTED", "实时标量算法需要奈奎斯特频率高于 50 Hz。")
        if requested.notch_hz is not None and requested.notch_hz >= source.sampling_rate_hz / 2.0:
            raise LiveRecordingAccessError("LIVE_SCALAR_NOTCH_UNSUPPORTED", "实时标量算法陷波频率必须低于奈奎斯特频率。")
        if len(requested.channels) != self._required_channel_count:
            raise LiveRecordingAccessError(
                "LIVE_CONFIG_INVALID",
                f"实时 {self.algorithm_id} 需要 {self._required_channel_count} 个通道。",
            )
        if self.algorithm_id != "theta_beta" and requested.frequency_mode != "standard":
            raise LiveRecordingAccessError("LIVE_CONFIG_INVALID", f"实时 {self.algorithm_id} 不支持自定义频段模式。")
        if requested.window_s < self._min_window_s:
            raise LiveRecordingAccessError(
                "LIVE_CONFIG_INVALID",
                f"实时 {self.algorithm_id} 窗口长度不能低于 {self._min_window_s:g} 秒。",
            )

        available = {name.casefold(): name for name in source.channel_names}
        try:
            channels = [available[name.strip().casefold()] for name in requested.channels]
        except KeyError as exc:
            raise LiveRecordingAccessError("LIVE_CHANNEL_NOT_FOUND", f"实时通道不存在：{exc.args[0]}") from exc
        if self.algorithm_id == "brainbeat" and (
            channels[0].casefold() not in FZ_SOURCE_CHANNELS
            or channels[1].casefold() not in PZ_SOURCE_CHANNELS
            or not source.matches_source_role(channels[0], "Fz")
            or not source.matches_source_role(channels[1], "Pz")
        ):
            raise LiveRecordingAccessError(
                "LIVE_CONFIG_INVALID",
                "BrainBeat 需要符合 Fz / Pz 角色的原始、REF 或 AVG 来源通道。",
            )

        effective_step_s = self._default_step_s if requested.step_s is None else float(requested.step_s)
        normalized = requested.model_copy(update={"channels": channels, "step_s": effective_step_s})
        return normalized, LiveWindowPlan(
            tuple(channels),
            round(requested.window_s * source.sampling_rate_hz),
            max(1, round(effective_step_s * source.sampling_rate_hz)),
        )

    def calculate(
        self,
        session_id: str,
        block: SampleBlock,
        config: BaseModel,
        session_first_sample: int,
    ) -> LiveScalarResult:
        if not isinstance(config, LiveScalarConfig):
            raise LiveRecordingAccessError("LIVE_CONFIG_INVALID", "实时标量算法配置类型无效。")

        start_sample, end_sample = block.start_sample, block.end_sample
        start_s = (start_sample - session_first_sample) / block.sampling_rate_hz
        end_s = (end_sample - session_first_sample) / block.sampling_rate_hz

        if block.unavailable_intervals or not np.isfinite(block.values_v).all():
            reasons = sorted({item.reason for item in block.unavailable_intervals}) or ["non_finite"]
            return self._unavailable(
                session_id,
                config,
                block,
                start_s=start_s,
                end_s=end_s,
                quality="unavailable",
                failure={
                    "code": "LIVE_SCALAR_WINDOW_UNAVAILABLE",
                    "message": "当前实时窗口包含缺失样本或采集缺口。",
                    "reasons": reasons,
                },
                scope_status="window_unavailable",
            )

        try:
            spec = live_spectral_filter_spec(config.notch_hz)
            prepared = self._preprocessor.prepare_window(session_id, block, spec)
            spectrum = DEFAULT_SPECTRAL_GATEWAY.welch(
                prepared.values_v,
                block.sampling_rate_hz,
                low_hz=1.0,
                high_hz=self._output_high_hz,
                include_segments=False,
            )
        except ValueError as exc:
            return self._unavailable(
                session_id,
                config,
                block,
                start_s=start_s,
                end_s=end_s,
                quality="unavailable",
                failure={"code": "LIVE_SCALAR_PREPROCESS_FAILED", "message": str(exc)},
                scope_status="preprocessing_failed",
            )

        requested_range = {"start_s": start_s, "end_s": end_s}
        if self.algorithm_id == "iapf":
            if getattr(spectrum, "gate_failed", None):
                quality_detail = {
                    key: getattr(spectrum, attr)
                    for key, attr in (
                        ("clean_segments", "clean_epochs"),
                        ("total_segments", "total_epochs"),
                        ("clean_ratio", "signal_quality"),
                        ("gate_failed", "gate_failed"),
                        ("rejected_reasons", "rejected_reasons"),
                    )
                    if getattr(spectrum, attr, None) is not None
                }
                return self._unavailable(
                    session_id,
                    config,
                    block,
                    start_s=start_s,
                    end_s=end_s,
                    quality="gate_failed",
                    failure={
                        "code": "PSD_QUALITY_GATE_FAILED",
                        "message": "当前动态窗口未通过 PSD 质量门",
                        "detail": quality_detail,
                    },
                    evidence={
                        "source_quality": quality_detail,
                        "spectral_evidence": dict(getattr(spectrum, "evidence", {})),
                    },
                    scope_status="complete",
                )
            result = compute_iapf(
                spectrum,
                channel=config.channels[0],
                requested_range=requested_range,
                actual_range=requested_range,
                recording_id=session_id,
            )
        elif self.algorithm_id == "theta_beta":
            result = compute_theta_beta(
                spectrum,
                channel=config.channels[0],
                requested_range=requested_range,
                actual_range=requested_range,
                frequency_mode=config.frequency_mode,
            )
        else:
            result = compute_brainbeat(
                spectrum,
                fz_channel=config.channels[0],
                pz_channel=config.channels[1],
                requested_range=requested_range,
                actual_range=requested_range,
                individualized=False,
            )

        is_valid = result.failure is None and result.value is not None and math.isfinite(result.value)
        failure: dict[str, object] | None = None
        if not is_valid:
            if result.failure is not None:
                failure = {
                    "code": result.failure.code,
                    "message": result.failure.message,
                    "detail": result.failure.detail,
                }
            else:
                failure = {"code": "LIVE_SCALAR_VALUE_INVALID", "message": "当前窗口计算结果无效。"}

        return LiveScalarResult(
            acquisition_session_id=session_id,
            algorithm_id=self.algorithm_id,
            scientific_identity=self.scientific_identity,
            implementation_identity=self.implementation_identity,
            state="ready" if is_valid else "unavailable",
            channels=list(config.channels),
            start_sample=start_sample,
            end_sample=end_sample,
            start_s=start_s,
            end_s=end_s,
            time_s=end_s,
            value=float(result.value) if is_valid else None,
            unit=self._unit,
            quality=result.quality,
            failure=failure,
            evidence=dict(result.evidence),
            provenance=self._provenance(config, block, "complete"),
        )

    def _unavailable(
        self,
        session_id: str,
        config: LiveScalarConfig,
        block: SampleBlock,
        *,
        start_s: float,
        end_s: float,
        quality: str,
        failure: dict[str, object],
        scope_status: str,
        evidence: dict[str, object] | None = None,
    ) -> LiveScalarResult:
        return LiveScalarResult(
            acquisition_session_id=session_id,
            algorithm_id=self.algorithm_id,
            scientific_identity=self.scientific_identity,
            implementation_identity=self.implementation_identity,
            state="unavailable",
            channels=list(config.channels),
            start_sample=block.start_sample,
            end_sample=block.end_sample,
            start_s=start_s,
            end_s=end_s,
            time_s=end_s,
            value=None,
            unit=self._unit,
            quality=quality,
            failure=failure,
            evidence=evidence or {},
            provenance=self._provenance(config, block, scope_status),
        )

    def _provenance(self, config: LiveScalarConfig, block: SampleBlock, scope_status: str) -> dict[str, object]:
        welch_segment_s = float(ANALYSIS_CONTRACT["welch_segment_s"])
        welch_overlap = float(ANALYSIS_CONTRACT["welch_segment_overlap"])
        return {
            "schema_version": "live-scalar-provenance-v1",
            "preprocessing_scope": "window_local_zero_phase",
            "preprocessing_status": scope_status,
            "filter_bandpass_hz": [1.0, 50.0],
            "notch_hz": config.notch_hz,
            "notch_quality_factor": ANALYSIS_CONTRACT["notch_quality_factor"] if config.notch_hz is not None else None,
            "sampling_rate_hz": block.sampling_rate_hz,
            "unit": "V",
            "window_s": config.window_s,
            "step_s": config.step_s,
            "welch_segment_s": welch_segment_s,
            "welch_segment_overlap": welch_overlap,
            "welch_step_s": float(ANALYSIS_CONTRACT["welch_step_s"]),
            "channel_order": list(config.channels),
            "paired_role_order": ["Fz", "Pz"] if self.algorithm_id == "brainbeat" else None,
            "frequency_mode": config.frequency_mode if self.algorithm_id == "theta_beta" else None,
            "equivalent_to_offline_continuous_preprocessing": False,
        }
