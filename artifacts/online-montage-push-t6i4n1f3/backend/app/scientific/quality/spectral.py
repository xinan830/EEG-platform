"""Shared deterministic quality rules for offline spectral windows."""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np

from app.scientific.contracts.analysis import ANALYSIS_CONTRACT
from app.scientific.contracts.types import GapEvidence


@dataclass(frozen=True)
class WindowQuality:
    status: str
    reasons: tuple[str, ...]
    peak_uv: float | None
    gap: GapEvidence


class SpectralQualityGateError(ValueError):
    def __init__(self, quality: dict[str, object]):
        super().__init__("频谱分析质量门未通过")
        self.quality = quality


def evaluate_spectral_window(data: np.ndarray, expected_samples: int) -> WindowQuality:
    """Evaluate one samples-by-channels window without altering its values."""
    values = np.asarray(data, dtype=float)
    reasons: list[str] = []
    if values.ndim != 2 or len(values) != expected_samples:
        reasons.append("missing_samples")
    finite_mask = np.isfinite(values) if values.ndim == 2 else np.empty((0, 0), dtype=bool)
    finite = values.ndim == 2 and bool(finite_mask.all())
    missing_samples = max(expected_samples - len(values), 0) if values.ndim == 2 else expected_samples
    non_finite_samples = int(np.count_nonzero(~finite_mask.all(axis=1))) if values.ndim == 2 and len(values) else 0
    gap = GapEvidence(
        detected=missing_samples > 0 or non_finite_samples > 0,
        policy="reject",
        missing_samples=missing_samples,
        non_finite_samples=non_finite_samples,
    )
    if not finite:
        reasons.append("non_finite")
        peak_uv = None
    else:
        peak_uv = float(np.max(np.abs(values)) * 1e6) if values.size else 0.0
        if peak_uv > float(ANALYSIS_CONTRACT["artifact_peak_uv"]):
            reasons.append("amplitude_threshold")
        if values.size and np.any(
            np.ptp(values, axis=0) * 1e6 < float(ANALYSIS_CONTRACT["flatline_peak_to_peak_uv"])
        ):
            reasons.append("flatline")
        if values.size and _has_clipping(values):
            reasons.append("clipping")
    unique = tuple(dict.fromkeys(reasons))
    return WindowQuality("bad" if unique else "clean", unique, peak_uv, gap)


def _has_clipping(values: np.ndarray) -> bool:
    minimum_count = int(ANALYSIS_CONTRACT["clipping_minimum_samples"])
    minimum_ratio = float(ANALYSIS_CONTRACT["clipping_minimum_ratio"])
    required = max(minimum_count, int(np.ceil(len(values) * minimum_ratio)))
    if required > len(values):
        return False
    for channel in values.T:
        channel_min = np.min(channel)
        channel_max = np.max(channel)
        if np.count_nonzero(channel == channel_min) >= required:
            return True
        if channel_max != channel_min and np.count_nonzero(channel == channel_max) >= required:
            return True
    return False
