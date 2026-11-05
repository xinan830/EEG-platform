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
    frequency_mode: str = "standard",
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
    iapf_value = None
    if frequency_mode == "standard":
        theta_low, theta_high, beta_low, beta_high = 4.0, 8.0, 13.0, 30.0
    elif frequency_mode == "individualized":
        iapf = estimate_iapf(spectrum)
        if iapf.value is None:
            failure = AlgorithmFailure(code="IAPF_UNAVAILABLE", message="当前窗口无法得到 IAPF，不能计算个体化 Theta/Beta", detail={"reason": iapf.gate_failed, "frequency_mode": frequency_mode})
            return AlgorithmResult(value=None, unit="dimensionless", channel=channel, requested_range=requested_range, actual_range=actual_range, quality="unavailable", failure=failure, evidence={"extensions": {"theta_beta_evidence": {"frequency_mode": frequency_mode}}, "source_quality": source_quality, "spectral_evidence": dict(getattr(spectrum, "evidence", {}))})
        iapf_value = float(iapf.value)
        theta_low, theta_high = iapf_value - 6.0, iapf_value - 4.0
        beta_low, beta_high = iapf_value + 2.0, 30.0
    else:
        raise ValueError(f"unsupported Theta/Beta frequency mode: {frequency_mode}")
    try:
        theta = float(band_power(spectrum.freqs, spectrum.psd[0], theta_low, theta_high))
        beta = float(band_power(spectrum.freqs, spectrum.psd[0], beta_low, beta_high))
    except ValueError as exc:
        failure = AlgorithmFailure(code="BAND_RANGE_INVALID", message="Theta/Beta 频段超出实际频率轴", detail={"error": str(exc), "frequency_mode": frequency_mode, "iapf_hz": iapf_value, "theta_range_hz": [theta_low, theta_high], "beta_range_hz": [beta_low, beta_high], "frequency_axis_hz": [float(spectrum.freqs[0]), float(spectrum.freqs[-1])]})
        return AlgorithmResult(value=None, unit="dimensionless", channel=channel, requested_range=requested_range, actual_range=actual_range, quality="unavailable", failure=failure, evidence={"extensions": {"theta_beta_evidence": {"frequency_mode": frequency_mode, "iapf_hz": iapf_value, "theta_range_hz": [theta_low, theta_high], "beta_range_hz": [beta_low, beta_high]}}, "source_quality": source_quality, "spectral_evidence": dict(getattr(spectrum, "evidence", {}))})
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
        evidence={"extensions": {"theta_beta_evidence": {"frequency_mode": frequency_mode, "iapf_hz": iapf_value, "theta_range_hz": [theta_low, theta_high], "beta_range_hz": [beta_low, beta_high], "theta_power_uv2": theta * 1e12, "beta_power_uv2": beta * 1e12}},
                  "calculation_trace": {"formula": "Theta 功率 ÷ Beta 功率", "inputs": [
                      *([{"label": "IAPF", "value": iapf_value, "unit": "Hz"}] if iapf_value is not None else []),
                      {"label": "实际 Theta 频段", "range_hz": [theta_low, theta_high]},
                      {"label": "实际 Theta 功率", "value": theta * 1e12, "unit": "uV^2"},
                      {"label": "实际 Beta 频段", "range_hz": [beta_low, beta_high]},
                      {"label": "实际 Beta 功率", "value": beta * 1e12, "unit": "uV^2"},
                  ]}, "source_quality": source_quality, "spectral_evidence": dict(getattr(spectrum, "evidence", {}))},
    )
