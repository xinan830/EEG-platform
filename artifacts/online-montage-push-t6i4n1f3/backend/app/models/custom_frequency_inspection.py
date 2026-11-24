"""Typed request and response contracts for custom PSD/RBP inspection."""

from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, model_validator


class CustomFrequencyWindowPair(BaseModel):
    model_config = ConfigDict(extra="forbid")

    psd_window_id: str = Field(min_length=1)
    rbp_window_id: str = Field(min_length=1)
    project_id: str | None = None


class CustomFrequencyRangeRequest(CustomFrequencyWindowPair):
    low_hz: float = Field(allow_inf_nan=False)
    high_hz: float = Field(allow_inf_nan=False)

    @model_validator(mode="after")
    def validate_range(self):
        if self.low_hz < 1.0 or self.high_hz > 50.0 or self.high_hz <= self.low_hz:
            raise ValueError("frequency range must satisfy 1 <= low_hz < high_hz <= 50")
        return self


class CustomFrequencyProvenance(BaseModel):
    window_id: str
    scientific_version: str
    implementation_identity: str
    configuration_sha256: str
    window_policy_version: str


class CustomFrequencyBinInterval(BaseModel):
    low_hz: float
    high_hz: float


class CustomFrequencyChannelQuality(BaseModel):
    channel: str
    psd_quality: str
    psd_quality_reasons: list[str] = Field(default_factory=list)
    rbp_quality: str
    rbp_quality_reasons: list[str] = Field(default_factory=list)


class CustomFrequencyBinChannel(CustomFrequencyChannelQuality):
    rbp_denominator_uv2: float | None
    psd_density_uv2_per_hz: list[float | None]
    rbp_absolute_power_uv2: list[float | None]
    rbp_relative_power_percent: list[float | None]
    amplitude_rms_uv: list[float | None]


class CustomFrequencyRangeChannel(CustomFrequencyChannelQuality):
    rbp_denominator_uv2: float | None
    psd_density_uv2_per_hz: float | None
    rbp_absolute_power_uv2: float | None
    rbp_relative_power_percent: float | None
    amplitude_rms_uv: float | None


class CustomFrequencyBinsResponse(BaseModel):
    mode: Literal["bins"] = "bins"
    recording_id: str
    start_sample: int = Field(ge=0)
    end_sample: int = Field(gt=0)
    frequency_hz: list[float]
    bin_intervals: list[CustomFrequencyBinInterval]
    channels: list[CustomFrequencyBinChannel]
    psd_provenance: CustomFrequencyProvenance
    rbp_provenance: CustomFrequencyProvenance


class CustomFrequencyRangeResponse(BaseModel):
    mode: Literal["range"] = "range"
    recording_id: str
    start_sample: int = Field(ge=0)
    end_sample: int = Field(gt=0)
    low_hz: float
    high_hz: float
    channels: list[CustomFrequencyRangeChannel]
    psd_provenance: CustomFrequencyProvenance
    rbp_provenance: CustomFrequencyProvenance
