from __future__ import annotations

from typing import Any

from app.algorithm_runtime.contracts import AlgorithmFailure, AlgorithmResult
from app.eeg_core.official_algorithms.iapf import IAPFEstimate, estimate_iapf


def compute_iapf(
    spectrum: Any,
    *,
    channel: str,
    requested_range: dict[str, float],
    actual_range: dict[str, float] | None = None,
) -> AlgorithmResult:
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
            evidence={"source": estimate.source, "peak_hz": estimate.peak_hz, "cog_hz": estimate.cog},
        )
    return AlgorithmResult(
        value=float(estimate.value),
        unit="Hz",
        channel=channel,
        requested_range=requested_range,
        actual_range=actual_range,
        quality="clean",
        evidence={
            "source": estimate.source,
            "peak_hz": estimate.peak_hz,
            "cog_hz": estimate.cog,
            "model_r2": estimate.model_r2,
            "model_error": estimate.model_error,
        },
    )
