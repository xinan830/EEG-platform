from __future__ import annotations

from typing import Any

import numpy as np

from app.algorithm_runtime.contracts import AlgorithmFailure, AlgorithmResult
from app.scientific.contracts import SampleRange


def _sample_coordinate_evidence(spectrum: Any, requested_range: dict[str, float], actual_range: dict[str, float] | None) -> dict[str, Any]:
    sfreq = getattr(spectrum, "evidence", {}).get("sfreq_hz") if isinstance(getattr(spectrum, "evidence", {}), dict) else None
    if sfreq is None or actual_range is None:
        return {"coordinate_system": "recording_relative_seconds", "sample_range": None}
    rate = float(sfreq)
    sample_range = SampleRange.from_seconds(float(actual_range["start_s"]), float(actual_range["end_s"]), rate)
    return {"coordinate_system": "recording_relative_sample", "sample_range": sample_range.as_dict(), "sfreq_hz": rate}


def _source_quality(spectrum: Any) -> dict[str, Any]:
    return {
        "clean_segments": int(getattr(spectrum, "clean_epochs", 0)),
        "total_segments": int(getattr(spectrum, "total_epochs", 0)),
        "clean_ratio": float(getattr(spectrum, "signal_quality", 0.0)),
        "gate_failed": getattr(spectrum, "gate_failed", None),
        "rejected_reasons": list(getattr(spectrum, "rejected_reasons", [])),
    }


def compute_peak_frequency(spectrum: Any, *, channel: str, low_hz: float, high_hz: float,
                           requested_range: dict[str, float], actual_range: dict[str, float] | None = None) -> AlgorithmResult:
    source_quality = _source_quality(spectrum)
    coordinate_evidence = _sample_coordinate_evidence(spectrum, requested_range, actual_range)
    base_evidence = {"source_quality": source_quality, "spectral_evidence": dict(getattr(spectrum, "evidence", {})),
                     "extensions": {"sample_coordinate": coordinate_evidence}}
    if getattr(spectrum, "gate_failed", None):
        failure = AlgorithmFailure(code="PSD_QUALITY_GATE_FAILED", message="当前窗口未通过 PSD 质量门", detail={"reason": spectrum.gate_failed})
        return AlgorithmResult(value=None, unit="Hz", channel=channel, requested_range=requested_range, actual_range=actual_range,
                               quality="gate_failed", failure=failure, evidence=base_evidence)
    frequencies = np.asarray(spectrum.freqs, dtype=float)
    psd = np.asarray(spectrum.psd, dtype=float)
    if psd.ndim != 2 or psd.shape[-1] != frequencies.size:
        failure = AlgorithmFailure(code="PSD_SHAPE_INVALID", message="PSD 与频率轴形状不匹配")
        return AlgorithmResult(value=None, unit="Hz", channel=channel, requested_range=requested_range, actual_range=actual_range,
                               quality="unavailable", failure=failure, evidence=base_evidence)
    mask = (frequencies >= low_hz) & (frequencies <= high_hz) & np.isfinite(frequencies)
    if not np.any(mask):
        failure = AlgorithmFailure(code="PEAK_FREQUENCY_NO_BIN", message="指定频段不包含有效 PSD 频率点", detail={"band_hz": [low_hz, high_hz]})
        return AlgorithmResult(value=None, unit="Hz", channel=channel, requested_range=requested_range, actual_range=actual_range,
                               quality="unavailable", failure=failure, evidence=base_evidence)
    mean_psd = np.nanmean(psd, axis=0)
    band_values = mean_psd[mask]
    if not np.all(np.isfinite(band_values)):
        failure = AlgorithmFailure(code="PEAK_FREQUENCY_NONFINITE", message="指定频段 PSD 含非有限值")
        return AlgorithmResult(value=None, unit="Hz", channel=channel, requested_range=requested_range, actual_range=actual_range,
                               quality="unavailable", failure=failure, evidence=base_evidence)
    frequencies_in_band = frequencies[mask]
    peak_index = int(np.argmax(band_values))
    peak_hz = float(frequencies_in_band[peak_index])
    peak_power = float(band_values[peak_index])
    evidence = {
        **base_evidence,
        "extensions": {"sample_coordinate": coordinate_evidence, "peak_frequency_evidence": {
            "band_hz": [float(low_hz), float(high_hz)], "frequency_grid_hz": frequencies_in_band.tolist(),
            "selected_bin_hz": peak_hz, "selected_power_v2_per_hz": peak_power,
            "edge_policy": "inclusive", "tie_policy": "lowest_frequency_grid_point", "interpolation": "none",
        }},
        "calculation_trace": {"formula": "指定频段 PSD 最大值对应的频率", "inputs": [
            {"label": "频段", "range_hz": [float(low_hz), float(high_hz)]},
            {"label": "峰值功率", "value": peak_power, "unit": "V^2/Hz"},
        ]},
    }
    return AlgorithmResult(value=peak_hz, unit="Hz", channel=channel, requested_range=requested_range,
                           actual_range=actual_range, quality="clean", evidence=evidence)
