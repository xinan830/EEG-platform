"""Versioned Pydantic schemas for paired offline scientific preview comparison."""
from __future__ import annotations

from typing import Any, Literal, Optional

from pydantic import BaseModel, Field

from app.models.analysis_config import OfflinePreprocessingRequest


class PreprocessingPreviewWindowRequest(BaseModel):
    start_s: float = Field(ge=0.0)
    window_s: float = Field(gt=0.0, le=120.0)
    montage_id: Optional[str] = None
    low_hz: float = Field(default=1.0, gt=0.0)
    high_hz: float = Field(default=50.0, gt=0.0)
    notch_hz: Optional[float] = 50.0
    notch_q: float = Field(default=30.0, gt=0.0)
    max_points: int = Field(default=1200, ge=64, le=4000, description=
        "Soft target; hard limits are 16000 points/trace and 1000000 time/value scalars/response. "
        "Excess gaps return 422 PREPROCESSING_PREVIEW_LIMIT_EXCEEDED; shorten the viewport.")
    preprocessing: OfflinePreprocessingRequest = Field(default_factory=OfflinePreprocessingRequest)


class PairedPreviewDisplayCompression(BaseModel):
    method: Literal["paired_finite_run_min_max_envelope_v1"]
    scientific_resampling: Literal[False]
    target_points_per_channel: int
    gap_boundaries_may_exceed_target: bool


class PreviewFilterEvidence(BaseModel):
    preprocessing_scope: str
    filter_phase: Literal["zero_phase"]
    filter_bandpass_hz: list[float]
    notch_hz: float | None
    notch_quality_factor: float | None
    spec_fingerprint: str


class ArtifactRemovalUnavailableInterval(BaseModel):
    start_sample: int
    end_sample: int
    start_s: float
    end_s: float
    missing_input_channels: list[str]
    unavailable_output_channels: list[str]
    additional_unavailable_channels: list[str]
    reason: Literal["missing_input_dependency", "input_unavailable"]


class ArtifactRemovalValidity(BaseModel):
    schema_version: Literal["artifact-removal-validity-v1"]
    sampling_rate_hz: float
    start_sample: int
    end_sample: int
    channels: list[str]
    unavailable_sample_count: int
    output_unavailable_channel_samples: int
    additional_unavailable_channel_samples: int
    intervals: list[ArtifactRemovalUnavailableInterval]


class PreviewBaselineSide(BaseModel):
    role: Literal["scientific_baseline"]
    channels: list[str]
    trace_times_s: dict[str, list[float]]
    traces_uv: dict[str, list[float | None]]
    preprocessing_evidence: dict[str, Any]


class PreviewProcessedSide(BaseModel):
    role: Literal["scientific_processed"]
    channels: list[str]
    excluded_output_channels: list[str]
    interpolated_channels: list[str]
    excluded_channels: list[str]
    trace_times_s: dict[str, list[float]]
    traces_uv: dict[str, list[float | None]]
    preprocessing_evidence: dict[str, Any]
    ica_model_reused: bool
    artifact_removal_status: str
    artifact_removal_validity: ArtifactRemovalValidity | None


class PreprocessingPreviewWindowResponse(BaseModel):
    """Paired display-only scientific comparison (shared filter baseline vs processed)."""

    schema_version: Literal["scientific-preview-comparison-v1"]
    recording_id: str
    source_sha256: Optional[str] = None
    montage_id: Optional[str] = None
    montage_snapshot_sha256: Optional[str] = None
    start_sample: int
    end_sample: int
    start_s: float
    end_s: float
    sfreq_hz: float
    unit: Literal["µV"]
    common_filter: PreviewFilterEvidence
    enhanced_preprocessing_active: bool
    display_compression: PairedPreviewDisplayCompression
    baseline: PreviewBaselineSide
    processed: PreviewProcessedSide
