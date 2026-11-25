"""Validated request schema for configurable spectral analysis and offline preprocessing."""

from typing import Literal

from pydantic import BaseModel, Field, model_validator

from app.scientific.contracts.preprocessing import (
    ArtifactRejectionSpec,
    ArtifactRemovalSpec,
    BadChannelSpec,
    ManualArtifactSegment,
)


class BadChannelRequest(BaseModel):
    policy: Literal["none", "exclude", "interpolate"] = "none"
    channels: list[str] = Field(default_factory=list)

    def to_spec(self) -> BadChannelSpec:
        return BadChannelSpec(policy=self.policy, channels=tuple(self.channels)).normalized()


class ManualArtifactSegmentRequest(BaseModel):
    start_sample: int = Field(ge=0)
    end_sample: int = Field(gt=0)
    reason: str = "manual"
    channels: list[str] = Field(default_factory=list)


class ArtifactRejectionRequest(BaseModel):
    policy: Literal["none", "annotate", "reject"] = "none"
    peak_to_peak_uv: float | None = Field(default=300.0, gt=0.0)
    amplitude_window_s: float = Field(default=0.5, gt=0.0)
    muscle_zscore_threshold: float | None = Field(default=4.0, gt=0.0)
    min_duration_s: float = Field(default=0.1, ge=0.0)
    pad_s: float = Field(default=0.1, ge=0.0)
    manual_segments: list[ManualArtifactSegmentRequest] = Field(default_factory=list)

    def to_spec(self) -> ArtifactRejectionSpec:
        manuals = tuple(
            ManualArtifactSegment(
                start_sample=seg.start_sample,
                end_sample=seg.end_sample,
                reason=seg.reason,
                channels=tuple(seg.channels),
            )
            for seg in self.manual_segments
        )
        return ArtifactRejectionSpec(
            policy=self.policy,
            peak_to_peak_uv=self.peak_to_peak_uv,
            amplitude_window_s=self.amplitude_window_s,
            muscle_zscore_threshold=self.muscle_zscore_threshold,
            min_duration_s=self.min_duration_s,
            pad_s=self.pad_s,
            manual_segments=manuals,
        ).normalized()


class ArtifactRemovalRequest(BaseModel):
    method: Literal["none", "eog_regression", "ssp_eog", "ica_infomax"] = "none"
    eog_channels: list[str] = Field(default_factory=list)
    ecg_channels: list[str] = Field(default_factory=list)
    allow_frontal_eog_proxy: bool = False
    ica_n_components: int | None = Field(default=None, ge=2, le=64)
    ica_eog_threshold: float = Field(default=0.7, gt=0.0, le=1.0)
    ica_exclude_components: list[int] | None = None
    ica_fit_fingerprint: str | None = None
    ssp_n_eeg: int = Field(default=1, ge=1, le=3)
    random_state: int = 42

    def to_spec(self) -> ArtifactRemovalSpec:
        return ArtifactRemovalSpec(
            method=self.method,
            eog_channels=tuple(self.eog_channels),
            ecg_channels=tuple(self.ecg_channels),
            allow_frontal_eog_proxy=self.allow_frontal_eog_proxy,
            ica_n_components=self.ica_n_components,
            ica_eog_threshold=self.ica_eog_threshold,
            ica_exclude_components=(
                None if self.ica_exclude_components is None else tuple(self.ica_exclude_components)
            ),
            ica_fit_fingerprint=self.ica_fit_fingerprint,
            ssp_n_eeg=self.ssp_n_eeg,
            random_state=self.random_state,
        ).normalized()


class OfflinePreprocessingRequest(BaseModel):
    bad_channels: BadChannelRequest = Field(default_factory=BadChannelRequest)
    artifact_rejection: ArtifactRejectionRequest = Field(default_factory=ArtifactRejectionRequest)
    artifact_removal: ArtifactRemovalRequest = Field(default_factory=ArtifactRemovalRequest)

    def to_specs(self) -> tuple[BadChannelSpec, ArtifactRejectionSpec, ArtifactRemovalSpec]:
        return (
            self.bad_channels.to_spec(),
            self.artifact_rejection.to_spec(),
            self.artifact_removal.to_spec(),
        )


class AnalysisTimeRange(BaseModel):
    start_s: float = Field(ge=0.0)
    end_s: float = Field(gt=0.0)

    @model_validator(mode="after")
    def validate_duration(self) -> "AnalysisTimeRange":
        if self.end_s <= self.start_s:
            raise ValueError("分析结束时间必须大于开始时间")
        if self.end_s - self.start_s < 4.0:
            raise ValueError("频谱分析区间至少需要 4 秒")
        return self


class AnalysisFrequencyRange(BaseModel):
    low_hz: float = Field(ge=1.0, le=30.0)
    high_hz: float = Field(ge=1.0, le=30.0)

    @model_validator(mode="after")
    def validate_order(self) -> "AnalysisFrequencyRange":
        if self.high_hz <= self.low_hz:
            raise ValueError("自定义频率上限必须大于下限")
        return self


class AnalysisConfigRequest(BaseModel):
    mode: Literal["static", "dynamic", "spectrogram"] = "static"
    channels: list[str] = Field(min_length=1)
    time: AnalysisTimeRange
    dynamic_window_s: Literal[5, 10, 20, 30] = 10
    refresh_step_s: Literal[1, 2, 5] = 1
    custom_frequency_range: AnalysisFrequencyRange | None = None
    preprocessing: OfflinePreprocessingRequest | None = None

    @model_validator(mode="after")
    def validate_channels(self) -> "AnalysisConfigRequest":
        cleaned = [name.strip() for name in self.channels if name.strip()]
        if not cleaned or len(set(name.casefold() for name in cleaned)) != len(cleaned):
            raise ValueError("分析通道不能为空或重复")
        self.channels = cleaned
        if self.custom_frequency_range is not None and self.mode != "spectrogram":
            raise ValueError("自定义频段趋势仅适用于时频图分析")
        return self
