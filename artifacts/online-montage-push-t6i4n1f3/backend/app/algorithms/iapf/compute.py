from __future__ import annotations

import json
import logging
from types import SimpleNamespace
from typing import Any

from app.algorithm_runtime.contracts import AlgorithmFailure, AlgorithmResult
from .official import IAPFEstimate, estimate_iapf

_LOGGER = logging.getLogger("brain_platform.iapf")


def compute_iapf(
    spectrum: Any,
    *,
    channel: str,
    requested_range: dict[str, float],
    actual_range: dict[str, float] | None = None,
    recording_id: str | None = None,
) -> AlgorithmResult:
    source_quality: dict[str, Any] = {}
    if (clean_segments := getattr(spectrum, "clean_epochs", None)) is not None:
        source_quality["clean_segments"] = int(clean_segments)
    if (total_segments := getattr(spectrum, "total_epochs", None)) is not None:
        source_quality["total_segments"] = int(total_segments)
    if (clean_ratio := getattr(spectrum, "signal_quality", None)) is not None:
        source_quality["clean_ratio"] = float(clean_ratio)
    source_quality["gate_failed"] = getattr(spectrum, "gate_failed", None)
    source_quality["rejected_reasons"] = list(getattr(spectrum, "rejected_reasons", []))
    estimate: IAPFEstimate = estimate_iapf(spectrum)
    segment_psd = getattr(spectrum, "welch_segments_psd", None)
    segment_offsets = getattr(spectrum, "welch_segment_offsets_s", ())
    if segment_psd is not None and len(segment_offsets) == len(segment_psd):
        segment_diagnostics = []
        for index, (offset_s, psd) in enumerate(zip(segment_offsets, segment_psd, strict=True)):
            segment = estimate_iapf(SimpleNamespace(
                freqs=spectrum.freqs, psd=psd, gate_failed=None,
            ))
            segment_diagnostics.append({
                "index": index,
                "start_s": round(float(requested_range["start_s"]) + offset_s, 6),
                "peak_hz": segment.peak_hz,
                "cog_hz": segment.cog,
                "source": segment.source,
                "model_r2": segment.model_r2,
                "peak_prominence_ratio": segment.peak_prominence_ratio,
                "peak_width_bins": segment.peak_width_bins,
            })
        _LOGGER.info(
            "iapf_welch_segment_diagnostics recording_id=%s channel=%s window_start_s=%.3f window_end_s=%.3f segments=%s",
            recording_id or "unknown", channel,
            requested_range["start_s"], requested_range["end_s"],
            json.dumps(segment_diagnostics, separators=(",", ":"), allow_nan=False),
        )
    if estimate.value is None:
        failure = AlgorithmFailure(
            code=estimate.gate_failed or "IAPF_UNAVAILABLE",
            message="当前分析窗口无法得到有效 IAPF",
            detail={
                "source": estimate.source,
                "model_r2": estimate.model_r2,
                "model_error": estimate.model_error,
            },
        )
        return AlgorithmResult(
            value=None,
            unit="Hz",
            channel=channel,
            requested_range=requested_range,
            actual_range=actual_range,
            quality="gate_failed",
            failure=failure,
            evidence={
                "source_quality": source_quality,
                "spectral_evidence": dict(getattr(spectrum, "evidence", {})),
                "extensions": {"iapf_evidence": {
                    "source": estimate.source, "peak_hz": estimate.peak_hz,
                    "cog_hz": estimate.cog, "peak_prominence_ratio": estimate.peak_prominence_ratio,
                    "peak_width_bins": estimate.peak_width_bins,
                    "cog_excess_ratio": estimate.cog_excess_ratio,
                    "cog_effective_bins": estimate.cog_effective_bins,
                }},
            },
        )
    return AlgorithmResult(
        value=float(estimate.value),
        unit="Hz",
        channel=channel,
        requested_range=requested_range,
        actual_range=actual_range,
        quality="clean",
        evidence={
            "extensions": {"iapf_evidence": {
                "source": estimate.source,
                "peak_hz": estimate.peak_hz,
                "cog_hz": estimate.cog,
                "model_r2": estimate.model_r2,
                "model_error": estimate.model_error,
                "peak_prominence_ratio": estimate.peak_prominence_ratio,
                "peak_width_bins": estimate.peak_width_bins,
                "cog_excess_ratio": estimate.cog_excess_ratio,
                "cog_effective_bins": estimate.cog_effective_bins,
            }},
            "calculation_trace": {"formula": "在 1/f 校正后的 Alpha 范围内选择 Peak 或 COG", "inputs": [
                {"label": "峰值频率", "value": estimate.peak_hz, "unit": "Hz"},
                {"label": "重心频率", "value": estimate.cog, "unit": "Hz"},
                {"label": "选峰方式", "text": estimate.source},
            ]},
            "source_quality": source_quality,
            "spectral_evidence": dict(getattr(spectrum, "evidence", {})),
        },
    )
