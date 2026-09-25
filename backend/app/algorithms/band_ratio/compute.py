from __future__ import annotations

from typing import Any

import numpy as np

from app.algorithm_runtime.contracts import AlgorithmFailure, AlgorithmResult
from app.scientific.contracts import SampleRange
from app.scientific.primitives.spectral import band_power


def _sample_coordinate_evidence(spectrum: Any, actual_range: dict[str, float] | None) -> dict[str, Any]:
    evidence = getattr(spectrum, "evidence", {})
    sfreq = evidence.get("sfreq_hz") if isinstance(evidence, dict) else None
    if sfreq is None or actual_range is None:
        return {"coordinate_system": "recording_relative_seconds", "sample_range": None}
    rate = float(sfreq)
    sample_range = SampleRange.from_seconds(float(actual_range["start_s"]), float(actual_range["end_s"]), rate)
    return {"coordinate_system": "recording_relative_sample", "sample_range": sample_range.as_dict(), "sfreq_hz": rate}


def compute_band_ratio(spectrum: Any, *, channel: str, numerator_low_hz: float, numerator_high_hz: float,
                       denominator_low_hz: float, denominator_high_hz: float,
                       requested_range: dict[str, float], actual_range: dict[str, float] | None = None) -> AlgorithmResult:
    source_quality = {"clean_segments": int(getattr(spectrum, "clean_epochs", 0)), "total_segments": int(getattr(spectrum, "total_epochs", 0)),
                      "clean_ratio": float(getattr(spectrum, "signal_quality", 0.0)), "gate_failed": getattr(spectrum, "gate_failed", None),
                      "rejected_reasons": list(getattr(spectrum, "rejected_reasons", []))}
    coordinate_evidence = _sample_coordinate_evidence(spectrum, actual_range)
    base_evidence = {"source_quality": source_quality, "spectral_evidence": dict(getattr(spectrum, "evidence", {})),
                     "extensions": {"sample_coordinate": coordinate_evidence}}
    if getattr(spectrum, "gate_failed", None):
        failure = AlgorithmFailure(code="PSD_QUALITY_GATE_FAILED", message="当前窗口未通过 PSD 质量门", detail={"reason": spectrum.gate_failed})
        return AlgorithmResult(value=None, unit="ratio", channel=channel, requested_range=requested_range, actual_range=actual_range, quality="gate_failed", failure=failure, evidence=base_evidence)
    try:
        values = np.asarray(spectrum.psd, dtype=float)
        numerator = float(band_power(spectrum.freqs, values[0], numerator_low_hz, numerator_high_hz))
        denominator = float(band_power(spectrum.freqs, values[0], denominator_low_hz, denominator_high_hz))
    except (IndexError, ValueError, TypeError) as exc:
        failure = AlgorithmFailure(code="BAND_RANGE_INVALID", message="频段边界无效或超出 PSD 频率轴", detail={"error": str(exc)})
        return AlgorithmResult(value=None, unit="ratio", channel=channel, requested_range=requested_range, actual_range=actual_range, quality="unavailable", failure=failure, evidence=base_evidence)
    if not np.isfinite(denominator) or denominator <= 0:
        failure = AlgorithmFailure(code="BAND_RATIO_DENOMINATOR_INVALID", message="分母频段功率不是正数", detail={"denominator_power_v2": denominator})
        return AlgorithmResult(value=None, unit="ratio", channel=channel, requested_range=requested_range, actual_range=actual_range, quality="unavailable", failure=failure, evidence=base_evidence)
    if not np.isfinite(numerator) or numerator < 0:
        failure = AlgorithmFailure(code="BAND_RATIO_NUMERATOR_INVALID", message="分子频段功率不是有效非负值", detail={"numerator_power_v2": numerator})
        return AlgorithmResult(value=None, unit="ratio", channel=channel, requested_range=requested_range, actual_range=actual_range, quality="unavailable", failure=failure, evidence=base_evidence)
    ratio = numerator / denominator
    evidence = {**base_evidence,
                "extensions": {"sample_coordinate": coordinate_evidence, "band_ratio_evidence": {"numerator_band_hz": [numerator_low_hz, numerator_high_hz], "denominator_band_hz": [denominator_low_hz, denominator_high_hz], "numerator_power_v2": numerator, "denominator_power_v2": denominator, "integration": "linear_edge_interpolation_trapezoid", "overlap_policy": "allowed_if_mathematically_valid"}},
                "calculation_trace": {"formula": "分子频段功率 ÷ 分母频段功率", "inputs": [{"label": "分子功率", "value": numerator, "unit": "V^2"}, {"label": "分母功率", "value": denominator, "unit": "V^2"}]}}
    return AlgorithmResult(value=float(ratio), unit="ratio", channel=channel, requested_range=requested_range, actual_range=actual_range, quality="clean", evidence=evidence)
