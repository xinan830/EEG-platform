from __future__ import annotations

from typing import Any

from app.algorithm_runtime.contracts import AlgorithmFailure, AlgorithmResult
from app.algorithms.iapf.official import estimate_iapf
from app.scientific.primitives.spectral import band_power


def compute_theta_beta(
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
    if spectrum.gate_failed:
        failure = AlgorithmFailure(code="PSD_QUALITY_GATE_FAILED", message="当前窗口未通过 PSD 质量门", detail={"reason": spectrum.gate_failed})
        return AlgorithmResult(value=None, unit="dimensionless", channel=channel, requested_range=requested_range, actual_range=actual_range, quality="gate_failed", failure=failure, evidence={"source_quality": source_quality, "spectral_evidence": dict(getattr(spectrum, "evidence", {}))})
    iapf = estimate_iapf(spectrum)
    if iapf.value is None:
        failure = AlgorithmFailure(code="IAPF_UNAVAILABLE", message="当前窗口无法得到 IAPF，不能计算 Theta/Beta", detail={"reason": iapf.gate_failed})
        return AlgorithmResult(value=None, unit="dimensionless", channel=channel, requested_range=requested_range, actual_range=actual_range, quality="gate_failed", failure=failure, evidence={"source_quality": source_quality, "spectral_evidence": dict(getattr(spectrum, "evidence", {}))})
    theta_low, theta_high = max(4.0, float(iapf.value) - 6.0), float(iapf.value) - 2.0
    beta_low, beta_high = float(iapf.value) + 2.0, 30.0
    try:
        theta = float(band_power(spectrum.freqs, spectrum.psd[0], theta_low, theta_high))
        beta = float(band_power(spectrum.freqs, spectrum.psd[0], beta_low, beta_high))
    except ValueError as exc:
        failure = AlgorithmFailure(code="BAND_RANGE_INVALID", message="IAPF 相对频段超出频率轴", detail={"error": str(exc), "iapf_hz": float(iapf.value)})
        return AlgorithmResult(value=None, unit="dimensionless", channel=channel, requested_range=requested_range, actual_range=actual_range, quality="gate_failed", failure=failure, evidence={"source_quality": source_quality, "spectral_evidence": dict(getattr(spectrum, "evidence", {}))})
    if beta <= 0:
        failure = AlgorithmFailure(code="BETA_DENOMINATOR_INVALID", message="Beta 功率不是正数，无法计算比值", detail={"beta_power": beta})
        return AlgorithmResult(value=None, unit="dimensionless", channel=channel, requested_range=requested_range, actual_range=actual_range, quality="gate_failed", failure=failure, evidence={"source_quality": source_quality, "spectral_evidence": dict(getattr(spectrum, "evidence", {}))})
    return AlgorithmResult(
        value=theta / beta,
        unit="dimensionless",
        channel=channel,
        requested_range=requested_range,
        actual_range=actual_range,
        quality="clean",
        evidence={"extensions": {"theta_beta_evidence": {"iapf_hz": float(iapf.value), "theta_range_hz": [theta_low, theta_high], "beta_range_hz": [beta_low, beta_high], "theta_power_uv2": theta * 1e12, "beta_power_uv2": beta * 1e12}},
                  "calculation_trace": {"formula": "个体化 Theta 功率 ÷ 个体化 Beta 功率", "inputs": [
                      {"label": "IAPF", "value": float(iapf.value), "unit": "Hz"},
                      {"label": "实际 Theta 频段", "range_hz": [theta_low, theta_high]},
                      {"label": "实际 Theta 功率", "value": theta * 1e12, "unit": "uV^2"},
                      {"label": "实际 Beta 频段", "range_hz": [beta_low, beta_high]},
                      {"label": "实际 Beta 功率", "value": beta * 1e12, "unit": "uV^2"},
                  ]}, "source_quality": source_quality, "spectral_evidence": dict(getattr(spectrum, "evidence", {}))},
    )
