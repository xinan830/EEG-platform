"""RBP-specific live configuration, signal calculation and evidence."""

from __future__ import annotations

from types import SimpleNamespace
from typing import Literal

import numpy as np
from pydantic import BaseModel, ConfigDict, Field, JsonValue, field_validator

from app.algorithms.rbp.official import RBP_BANDS, five_band_shares
from app.scientific.contracts.analysis import ANALYSIS_CONTRACT
from app.scientific.contracts.preprocessing import live_spectral_filter_spec
from app.scientific.primitives import DEFAULT_SPECTRAL_GATEWAY
from app.services.live_analysis_contracts import LiveWindowPlan
from app.services.live_scientific_preprocessing import LiveScientificPreprocessingService
from app.services.sample_access_contract import SampleBlock
from app.services.live_recording_access import LiveRecordingAccessError, LiveRecordingAccessSession


class LiveRbpConfig(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: Literal["live-rbp-config-v1"] = "live-rbp-config-v1"
    channels: list[str] = Field(min_length=1)
    window_s: float = Field(default=10.0, gt=0, le=60)
    step_s: float = Field(default=1.0, gt=0, le=10)
    notch_hz: float | None = 50.0

    @field_validator("channels")
    @classmethod
    def channels_are_unique(cls, values: list[str]) -> list[str]:
        cleaned = [value.strip() for value in values]
        if any(not value for value in cleaned) or len({value.casefold() for value in cleaned}) != len(cleaned):
            raise ValueError("实时 RBP 通道必须非空且唯一")
        return values

    @field_validator("notch_hz")
    @classmethod
    def notch_is_supported(cls, value: float | None) -> float | None:
        if value is not None and value not in (50.0, 60.0):
            raise ValueError("实时 RBP 陷波仅支持关闭、50 Hz 或 60 Hz")
        return value


class LiveRbpResult(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: str = "live-rbp-result-v1"
    type: str = "live_rbp_result"
    acquisition_session_id: str
    state: str
    algorithm_id: str = "rbp"
    scientific_identity: str = "rbp-live-v1"
    implementation_identity: str = "rbp-live-window-v1"
    channels: list[str]
    start_sample: int | None = None
    end_sample: int | None = None
    start_s: float | None = None
    end_s: float | None = None
    values: dict[str, list[float | None]] = Field(default_factory=dict)
    units: dict[str, str] = Field(default_factory=dict)
    quality: dict[str, dict[str, object]] = Field(default_factory=dict)
    failure: dict[str, object] | None = None
    provenance: dict[str, object] = Field(default_factory=dict)


class LiveRbpAlgorithm:
    algorithm_id = "rbp"
    scientific_identity = "rbp-live-v1"
    implementation_identity = "rbp-live-window-v1"

    def __init__(self, preprocessor: LiveScientificPreprocessingService | None = None) -> None:
        self._preprocessor = preprocessor or LiveScientificPreprocessingService()

    def configure(
        self, source: LiveRecordingAccessSession, config: dict[str, JsonValue]
    ) -> tuple[LiveRbpConfig, LiveWindowPlan]:
        requested = LiveRbpConfig.model_validate(config)
        available = {name.casefold(): name for name in source.channel_names}
        if source.identity.acquisition_session_id == "":
            raise LiveRecordingAccessError("LIVE_SESSION_ID_INVALID", "实时采集会话身份不能为空。")
        if source.sampling_rate_hz / 2.0 <= 50.0:
            raise LiveRecordingAccessError("LIVE_RBP_NYQUIST_UNSUPPORTED", "实时 RBP 需要奈奎斯特频率高于 50 Hz。")
        if requested.notch_hz is not None and requested.notch_hz >= source.sampling_rate_hz / 2.0:
            raise LiveRecordingAccessError("LIVE_RBP_NOTCH_UNSUPPORTED", "实时 RBP 陷波频率必须低于奈奎斯特频率。")
        try:
            channels = [available[name.strip().casefold()] for name in requested.channels]
        except KeyError as exc:
            raise LiveRecordingAccessError("LIVE_CHANNEL_NOT_FOUND", f"实时通道不存在：{exc.args[0]}") from exc
        config_value = requested.model_copy(update={"channels": channels})
        return config_value, LiveWindowPlan(
            tuple(channels), round(requested.window_s * source.sampling_rate_hz),
            max(1, round(requested.step_s * source.sampling_rate_hz)),
        )

    def calculate(
        self,
        session_id: str,
        block: SampleBlock,
        config: BaseModel,
        session_first_sample: int,
    ) -> LiveRbpResult:
        if not isinstance(config, LiveRbpConfig):
            raise LiveRecordingAccessError("LIVE_CONFIG_INVALID", "实时 RBP 配置类型无效。")
        start_sample, end_sample = block.start_sample, block.end_sample
        start_s = (start_sample - session_first_sample) / block.sampling_rate_hz
        end_s = (end_sample - session_first_sample) / block.sampling_rate_hz
        units = {name: "ratio" for name, *_ in RBP_BANDS}
        units.update({f"{name}_absolute_power": "uV^2" for name, *_ in RBP_BANDS})
        values: dict[str, list[float | None]] = {name: [None] * len(config.channels) for name in units}
        if block.unavailable_intervals or not np.isfinite(block.values_v).all():
            reasons = sorted({item.reason for item in block.unavailable_intervals}) or ["non_finite"]
            return LiveRbpResult(
                acquisition_session_id=session_id, state="unavailable", channels=list(config.channels),
                start_sample=start_sample, end_sample=end_sample, start_s=start_s, end_s=end_s,
                values=values, units=units,
                quality={channel: {"status": "unavailable", "reasons": reasons} for channel in config.channels},
                failure={"code": "LIVE_RBP_WINDOW_UNAVAILABLE", "message": "当前实时窗口包含缺失样本或采集缺口。"},
                provenance=LiveRbpAlgorithm._provenance(config, block, "window_unavailable"),
            )
        try:
            spec = live_spectral_filter_spec(config.notch_hz)
            prepared_window = self._preprocessor.prepare_window(session_id, block, spec)
            spectrum = DEFAULT_SPECTRAL_GATEWAY.welch_annotated_channels(
                prepared_window.values_v, block.sampling_rate_hz, low_hz=spec.low_hz, high_hz=spec.high_hz,
            )
        except ValueError as exc:
            return LiveRbpResult(
                acquisition_session_id=session_id, state="unavailable", channels=list(config.channels),
                start_sample=start_sample, end_sample=end_sample, start_s=start_s, end_s=end_s,
                values=values, units=units,
                quality={channel: {"status": "unavailable", "reasons": [str(exc)]} for channel in config.channels},
                failure={"code": "LIVE_RBP_PREPROCESS_FAILED", "message": str(exc)},
                provenance=LiveRbpAlgorithm._provenance(config, block, "preprocessing_failed"),
            )
        qualities = spectrum.evidence.get("channel_quality", [])
        warnings = False
        for index, _channel in enumerate(config.channels):
            channel_quality = qualities[index] if index < len(qualities) else {"status": "clean"}
            if channel_quality.get("status") == "unavailable":
                continue
            try:
                powers, shares = five_band_shares(SimpleNamespace(
                    freqs=spectrum.freqs, psd=spectrum.psd[index:index + 1],
                ))
            except ValueError as exc:
                channel_quality = {"status": "unavailable", "reasons": [str(exc)]}
                qualities[index] = channel_quality
                continue
            for name, value in powers.items():
                values[f"{name}_absolute_power"][index] = value
            for name, value in shares.items():
                values[name][index] = value
            warnings |= channel_quality.get("status") == "warning"
        quality = {channel: qualities[index] for index, channel in enumerate(config.channels)}
        states = {item.get("status") for item in quality.values()}
        state = "unavailable" if states == {"unavailable"} else "partial" if "unavailable" in states or warnings else "ready"
        return LiveRbpResult(
            acquisition_session_id=session_id, state=state, channels=list(config.channels),
            start_sample=start_sample, end_sample=end_sample, start_s=start_s, end_s=end_s,
            values=values, units=units, quality=quality,
            failure={"code": "LIVE_RBP_CHANNEL_UNAVAILABLE", "message": "部分或全部通道无法计算。"} if state != "ready" else None,
            provenance=LiveRbpAlgorithm._provenance(config, block, "complete"),
        )

    @staticmethod
    def _provenance(config: LiveRbpConfig, block: SampleBlock, scope: str) -> dict[str, object]:
        return {
            "schema_version": "live-rbp-provenance-v1",
            "preprocessing_scope": "window_local_zero_phase",
            "preprocessing_status": scope,
            "filter_bandpass_hz": [1.0, 50.0],
            "notch_hz": config.notch_hz,
            "notch_quality_factor": ANALYSIS_CONTRACT["notch_quality_factor"] if config.notch_hz is not None else None,
            "sampling_rate_hz": block.sampling_rate_hz,
            "unit": "V",
            "window_s": config.window_s,
            "step_s": config.step_s,
            "welch_segment_s": ANALYSIS_CONTRACT["welch_segment_s"],
            "channel_order": list(config.channels),
            "equivalent_to_offline_official_rbp_v4": False,
        }
