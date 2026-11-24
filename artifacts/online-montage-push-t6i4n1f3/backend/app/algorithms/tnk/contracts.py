"""Typed contracts shared only by the unregistered TNK module family."""

from __future__ import annotations

from typing import Any

import numpy as np
from pydantic import BaseModel, ConfigDict, Field

from app.algorithm_runtime.contracts import AlgorithmFailure, AlgorithmResult


TNK_ALGORITHM_IDS = (
    "tnk_iapf", "tnk_relaxation", "tnk_spatial_distribution", "tnk_rhythm_stability",
    "tnk_composite_meditation", "tnk_brainbeat", "tnk_brainbeat_flat", "tnk_fatigue",
    "tnk_hai", "tnk_faa", "tnk_rbp", "tnk_aperiodic",
)


class TnkConfig(BaseModel):
    model_config = ConfigDict(extra="forbid")

    start_sample: int = Field(ge=0)
    end_sample: int = Field(gt=0)
    sfreq_hz: float = Field(gt=0)
    mode: str = "window"

    def valid_range(self) -> bool:
        return self.end_sample > self.start_sample


class TnkManifest(BaseModel):
    algorithm_id: str
    display_name_zh: str
    scientific_version: str
    implementation_identity: str
    channel_roles: list[str] = Field(min_length=1)
    input_unit: str = "V"
    output_unit: str
    stateful: bool
    supported_modes: list[str] = Field(default_factory=lambda: ["window"])
    failure_codes: list[str] = Field(min_length=1)


class TnkSpectralWindow(BaseModel):
    """A normalized, already-prepared PSD window owned by the module caller."""

    model_config = ConfigDict(arbitrary_types_allowed=True)

    frequencies_hz: np.ndarray
    psd_by_role: dict[str, np.ndarray]
    sfreq_hz: float = Field(gt=0)
    start_sample: int = Field(ge=0)
    end_sample: int = Field(gt=0)
    unit: str = "V"
    quality: str = "clean"
    source_quality: dict[str, Any] = Field(default_factory=dict)
    provenance: dict[str, Any] = Field(default_factory=dict)

    def validate_shape(self, roles: list[str]) -> AlgorithmFailure | None:
        if self.end_sample <= self.start_sample:
            return AlgorithmFailure(code="TNK_WINDOW_RANGE_INVALID", message="TNK 窗口样本范围无效")
        if self.unit != "V":
            return AlgorithmFailure(code="TNK_INPUT_UNIT_INVALID", message="TNK 输入必须使用 V")
        frequencies = np.asarray(self.frequencies_hz, dtype=float)
        if frequencies.ndim != 1 or not np.isfinite(frequencies).all() or np.any(np.diff(frequencies) <= 0):
            return AlgorithmFailure(code="TNK_FREQUENCY_AXIS_INVALID", message="TNK 频率轴无效")
        missing = [role for role in roles if role not in self.psd_by_role]
        if missing:
            return AlgorithmFailure(code="TNK_CHANNEL_ROLE_MISSING", message="TNK 缺少声明的通道角色", detail={"missing_roles": missing})
        for role in roles:
            values = np.asarray(self.psd_by_role[role], dtype=float)
            if values.ndim != 1 or values.shape != frequencies.shape or not np.isfinite(values).all() or np.any(values < 0):
                return AlgorithmFailure(code="TNK_PSD_INVALID", message="TNK PSD 数组无效", detail={"role": role})
        return None


def band_power(frequencies_hz: np.ndarray, psd: np.ndarray, low_hz: float, high_hz: float) -> float:
    mask = (frequencies_hz >= low_hz) & (frequencies_hz <= high_hz)
    if np.count_nonzero(mask) < 2:
        return 0.0
    return float(np.trapezoid(psd[mask], frequencies_hz[mask]))


def result(module: TnkManifest, window: TnkSpectralWindow, value: float | None,
           *, quality: str = "clean", failure: AlgorithmFailure | None = None,
           output_values: dict[str, float | None] | None = None,
           evidence: dict[str, Any] | None = None) -> AlgorithmResult:
    tnk_evidence = {"provenance": dict(window.provenance), **(evidence or {})}
    return AlgorithmResult(
        value=value, unit=module.output_unit, channel="/".join(module.channel_roles),
        requested_range={"start_s": window.start_sample / window.sfreq_hz, "end_s": window.end_sample / window.sfreq_hz},
        actual_range={"start_s": window.start_sample / window.sfreq_hz, "end_s": window.end_sample / window.sfreq_hz},
        quality=quality, failure=failure, output_values=output_values,
        evidence={
            "source_quality": dict(window.source_quality),
            "extensions": {"tnk_evidence": tnk_evidence},
        },
    )


def validate_window(module: TnkManifest, window: TnkSpectralWindow) -> AlgorithmFailure | None:
    failure = window.validate_shape(module.channel_roles)
    if failure:
        return failure
    if window.quality != "clean":
        return AlgorithmFailure(code="TNK_QUALITY_REJECTED", message="TNK 窗口未通过质量门", detail={"quality": window.quality})
    return None
