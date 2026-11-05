from __future__ import annotations

from typing import Any

from app.algorithm_runtime.contracts import AlgorithmFailure, AlgorithmResult
from app.algorithms.iapf.official import estimate_iapf
from app.algorithms.brainbeat.official import classic_brainbeat, segment_brainbeat
from app.scientific.primitives.spectral import band_power


def compute_brainbeat(
    spectrum: Any,
    *,
    fz_channel: str,
    pz_channel: str,
    requested_range: dict[str, float],
    actual_range: dict[str, float] | None = None,
    individualized: bool = False,
) -> AlgorithmResult:
    source_quality = {
        key: value for key, value in {
            "clean_segments": getattr(spectrum, "clean_epochs", None),
            "total_segments": getattr(spectrum, "total_epochs", None),
            "clean_ratio": getattr(spectrum, "signal_quality", None),
            "gate_failed": getattr(spectrum, "gate_failed", None),
            "rejected_reasons": list(getattr(spectrum, "rejected_reasons", [])),
        }.items() if value is not None
    }
    evidence = {"source_quality": source_quality, "spectral_evidence": dict(getattr(spectrum, "evidence", {}))}
    if getattr(spectrum, "gate_failed", None):
        return AlgorithmResult(
            value=None, unit="ratio", channel=f"{fz_channel}/{pz_channel}",
            requested_range=requested_range, actual_range=actual_range,
            quality="gate_failed",
            failure=AlgorithmFailure(
                code="PSD_QUALITY_GATE_FAILED", message="当前 Brainbeat 窗口未通过 PSD 质量门",
                detail=source_quality,
            ), evidence=evidence,
        )

    if getattr(spectrum, "psd", None) is None or len(spectrum.psd) < 2:
        failure = AlgorithmFailure(
            code="BRAINBEAT_CHANNEL_DATA_UNAVAILABLE",
            message="Brainbeat 需要 Fz 和 Pz 两个频谱通道",
            detail={"channel_order": [fz_channel, pz_channel]},
        )
        return AlgorithmResult(
            value=None, unit="ratio", channel=f"{fz_channel}/{pz_channel}",
            requested_range=requested_range, actual_range=actual_range,
            quality="unavailable", failure=failure, evidence=evidence,
        )

    try:
        iapf_value = None
        if individualized:
            iapf = estimate_iapf(spectrum)
            if iapf.value is None:
                failure = AlgorithmFailure(
                    code="IAPF_UNAVAILABLE", message="当前个体化 Brainbeat 窗口无法得到 IAPF",
                    detail={"reason": iapf.gate_failed},
                )
                return AlgorithmResult(
                    value=None, unit="ratio", channel=f"{fz_channel}/{pz_channel}",
                    requested_range=requested_range, actual_range=actual_range,
                    quality="unavailable", failure=failure, evidence=evidence,
                )
            iapf_value = float(iapf.value)
            value = segment_brainbeat(spectrum.freqs, spectrum.psd[0], spectrum.psd[1], iapf_value)
            formula = "relative_theta(Fz) / relative_alpha(Pz)"
            effective_bands = {
                "theta_fz_hz": [max(4.0, iapf_value - 6.0), iapf_value - 2.0],
                "alpha_pz_hz": [iapf_value - 2.0, iapf_value + 2.0],
            }
            powers = {}
        else:
            value = classic_brainbeat(spectrum.freqs, spectrum.psd[0], spectrum.psd[1])
            formula = "absolute_power(Fz, 4-8 Hz) / absolute_power(Pz, 8-12 Hz)"
            effective_bands = {"theta_fz_hz": [4.0, 8.0], "alpha_pz_hz": [8.0, 12.0]}
            powers = {
                "fz_theta_power_v2": float(band_power(spectrum.freqs, spectrum.psd[0], 4.0, 8.0)),
                "pz_alpha_power_v2": float(band_power(spectrum.freqs, spectrum.psd[1], 8.0, 12.0)),
            }
    except (ValueError, IndexError, FloatingPointError) as exc:
        failure = AlgorithmFailure(
            code="BRAINBEAT_BAND_RANGE_INVALID", message="Brainbeat 的频带无法在当前频率轴计算",
            detail={"error": str(exc), "iapf_hz": iapf_value},
        )
        return AlgorithmResult(
            value=None, unit="ratio", channel=f"{fz_channel}/{pz_channel}",
            requested_range=requested_range, actual_range=actual_range,
            quality="unavailable", failure=failure, evidence=evidence,
        )

    if not float(value) >= 0:
        failure = AlgorithmFailure(
            code="BRAINBEAT_VALUE_INVALID", message="Brainbeat 计算结果不是有效的非负数",
            detail={"value": float(value), "iapf_hz": iapf_value},
        )
        return AlgorithmResult(
            value=None, unit="ratio", channel=f"{fz_channel}/{pz_channel}",
            requested_range=requested_range, actual_range=actual_range,
            quality="unavailable", failure=failure, evidence=evidence,
        )

    evidence["extensions"] = {"brainbeat_evidence": {
        "variant": "individualized" if individualized else "classic",
        "fz_channel": fz_channel, "pz_channel": pz_channel, "iapf_hz": iapf_value,
        "formula": formula, "effective_bands_hz": effective_bands,
        "intermediate_power_unit": "V^2" if not individualized else None,
        **powers,
    }}
    evidence["calculation_trace"] = {
        "formula": formula, "variant": "individualized" if individualized else "classic",
        "iapf_hz": iapf_value, "effective_bands_hz": effective_bands,
    }
    return AlgorithmResult(
        value=float(value), unit="ratio", channel=f"{fz_channel}/{pz_channel}",
        requested_range=requested_range, actual_range=actual_range,
        quality="clean", evidence=evidence,
    )
