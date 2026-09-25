from __future__ import annotations

from typing import Any

from app.algorithm_runtime.contracts import AlgorithmFailure, AlgorithmResult
from .official import IAPFEstimate, estimate_iapf


def compute_iapf(
    spectrum: Any,
    *,
    channel: str,
    requested_range: dict[str, float],
    actual_range: dict[str, float] | None = None,
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
                "extensions": {"iapf_evidence": {"source": estimate.source, "peak_hz": estimate.peak_hz, "cog_hz": estimate.cog}},
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
