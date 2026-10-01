from __future__ import annotations

from typing import Any

from app.algorithm_runtime.contracts import AlgorithmFailure, AlgorithmResult
from app.algorithms.iapf.official import estimate_iapf
from app.eeg_core.official_algorithms.brainbeat import segment_brainbeat


def compute_brainbeat(
    spectrum: Any,
    *,
    fz_channel: str,
    pz_channel: str,
    requested_range: dict[str, float],
    actual_range: dict[str, float] | None = None,
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

    iapf = estimate_iapf(spectrum)
    if iapf.value is None:
        failure = AlgorithmFailure(
            code="IAPF_UNAVAILABLE", message="当前 Brainbeat 窗口无法得到 IAPF",
            detail={"reason": iapf.gate_failed},
        )
        return AlgorithmResult(
            value=None, unit="ratio", channel=f"{fz_channel}/{pz_channel}",
            requested_range=requested_range, actual_range=actual_range,
            quality="unavailable", failure=failure, evidence=evidence,
        )

    try:
        value = segment_brainbeat(spectrum.freqs, spectrum.psd[0], spectrum.psd[1], float(iapf.value))
    except (ValueError, IndexError, FloatingPointError) as exc:
        failure = AlgorithmFailure(
            code="BRAINBEAT_BAND_RANGE_INVALID", message="Brainbeat 的 IAPF 相对频段无法在当前频率轴计算",
            detail={"error": str(exc), "iapf_hz": float(iapf.value)},
        )
        return AlgorithmResult(
            value=None, unit="ratio", channel=f"{fz_channel}/{pz_channel}",
            requested_range=requested_range, actual_range=actual_range,
            quality="unavailable", failure=failure, evidence=evidence,
        )

    if not float(value) >= 0:
        failure = AlgorithmFailure(
            code="BRAINBEAT_VALUE_INVALID", message="Brainbeat 计算结果不是有效的非负数",
            detail={"value": float(value), "iapf_hz": float(iapf.value)},
        )
        return AlgorithmResult(
            value=None, unit="ratio", channel=f"{fz_channel}/{pz_channel}",
            requested_range=requested_range, actual_range=actual_range,
            quality="unavailable", failure=failure, evidence=evidence,
        )

    evidence["extensions"] = {"brainbeat_evidence": {
        "fz_channel": fz_channel, "pz_channel": pz_channel, "iapf_hz": float(iapf.value),
        "formula": "relative_theta(Fz) / relative_alpha(Pz)",
    }}
    evidence["calculation_trace"] = {"formula": "Fz 相对 Theta ÷ Pz 相对 Alpha", "iapf_hz": float(iapf.value)}
    return AlgorithmResult(
        value=float(value), unit="ratio", channel=f"{fz_channel}/{pz_channel}",
        requested_range=requested_range, actual_range=actual_range,
        quality="clean", evidence=evidence,
    )
