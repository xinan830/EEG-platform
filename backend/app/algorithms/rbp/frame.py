"""One scientific frame for standalone RBP static, full and target execution."""

from collections.abc import Callable
from dataclasses import dataclass
import logging
from time import perf_counter
from types import SimpleNamespace
from typing import Any

import numpy as np

from app.algorithm_runtime.contracts import AlgorithmFailure, AlgorithmInputs
from app.scientific.quality import SpectralQualityGateError
from app.scientific.contracts.analysis import ANALYSIS_CONTRACT

from . import official

logger = logging.getLogger("brain_platform.rbp")


@dataclass(frozen=True)
class RbpFrame:
    """Ordered relative and absolute band-power arrays for every execution path."""

    arrays: dict[str, np.ndarray]
    quality: str
    failure: AlgorithmFailure | None
    evidence: dict[str, Any]
    provenance: dict[str, Any]

    @property
    def state(self) -> str:
        if self.failure is None:
            return "ready"
        return "rejected" if self.quality == "gate_failed" else "unavailable"


def execute_rbp_frame(
    inputs: AlgorithmInputs, start_s: float, end_s: float,
    check_cancelled: Callable[[], None] | None = None,
    *, notch_hz: float | None = 50.0, request_id: str | None = None, window_id: str | None = None,
) -> RbpFrame:
    """Read one batched continuous-source spectrum, then integrate each channel."""
    started = perf_counter()
    check = check_cancelled or (inputs.execution_context.check_cancelled if inputs.execution_context else None)
    if check:
        check()
    spectrum = None
    gate_quality: dict[str, Any] | None = None
    spectrum_started = perf_counter()
    try:
        spectrum = inputs.payload.load_spectrum(
            start_s=start_s, window_s=end_s - start_s, channels=list(inputs.channels),
            filter_low_hz=official.RBP_FILTER_LOW_HZ, filter_high_hz=official.RBP_FILTER_HIGH_HZ,
            output_low_hz=official.RBP_FILTER_LOW_HZ, output_high_hz=official.RBP_FILTER_HIGH_HZ,
            notch_hz=notch_hz,
            quality_policy="per_channel_annotation",
        )
    except SpectralQualityGateError as exc:
        gate_quality = dict(exc.quality)
    spectrum_load_ms = (perf_counter() - spectrum_started) * 1000
    if check:
        check()
    if spectrum is not None:
        psd = np.asarray(spectrum.psd, dtype=np.float64)
        if psd.ndim != 2 or psd.shape[0] != len(inputs.channels):
            raise ValueError("RBP spectrum rows must match ordered input channels")
        if spectrum.gate_failed:
            gate_quality = {"reason": spectrum.gate_failed}

    arrays = {
        output: np.full(len(inputs.channels), np.nan)
        for band, *_ in official.RBP_BANDS
        for output in (band, f"{band}_absolute_power")
    }
    failures: dict[str, dict[str, Any]] = {}
    channel_quality = dict(getattr(spectrum, "evidence", {}).get("channel_quality", {})) if spectrum is not None else {}
    warnings = {channel: item for channel, item in channel_quality.items() if item["status"] == "warning"}
    band_powers: dict[str, dict[str, float]] = {}
    band_started = perf_counter()
    for index, channel in enumerate(inputs.channels):
        if check:
            check()
        if gate_quality is not None:
            failures[channel] = {
                "code": "PSD_QUALITY_GATE_FAILED", "message": "当前窗口未通过 PSD 质量门",
                "details": gate_quality,
            }
            continue
        if channel_quality.get(channel, {}).get("status") == "unavailable":
            failures[channel] = {"code": "RBP_CHANNEL_UNAVAILABLE", "message": "当前导联存在缺失或非有限样本",
                                 "details": channel_quality[channel]}
            continue
        try:
            powers, shares = official.five_band_shares(
                SimpleNamespace(freqs=spectrum.freqs, psd=psd[index:index + 1]),
            )
        except ValueError as exc:
            failures[channel] = {"code": "RBP_CHANNEL_UNAVAILABLE", "message": str(exc)}
            continue
        band_powers[channel] = powers
        for name, value in powers.items():
            arrays[f"{name}_absolute_power"][index] = value
        for name, value in shares.items():
            arrays[name][index] = value
    if check:
        check()
    band_integration_ms = (perf_counter() - band_started) * 1000

    all_failed = len(failures) == len(inputs.channels)
    quality = ("gate_failed" if gate_quality is not None else "unavailable") if all_failed else (
        "partial" if failures or warnings else "clean"
    )
    failure = AlgorithmFailure(
        code="PSD_QUALITY_GATE_FAILED" if gate_quality is not None else "RBP_ALL_CHANNELS_UNAVAILABLE",
        message="当前窗口未通过 PSD 质量门" if gate_quality is not None else "所有选定导联均不可用",
        detail={"channels": failures},
    ) if all_failed else None
    provenance = {
        "preprocessing_scope": "whole_recording_before_window_slice",
        "filter_bandpass_hz": [official.RBP_FILTER_LOW_HZ, official.RBP_FILTER_HIGH_HZ],
        "notch_hz": notch_hz,
        "notch_quality_factor": ANALYSIS_CONTRACT["notch_quality_factor"] if notch_hz is not None else None,
    }
    spectral_evidence = dict(getattr(spectrum, "evidence", {}) or {}) if spectrum is not None else dict(
        (gate_quality or {}).get("evidence", {}),
    )
    source_quality = {key: getattr(spectrum, key) for key in (
        "signal_quality", "clean_epochs", "total_epochs", "gate_failed", "rejected_reasons",
    ) if spectrum is not None and hasattr(spectrum, key)}
    if gate_quality is not None:
        source_quality.update(gate_quality)
    evidence = {
        "source_quality": source_quality,
        "spectral_evidence": spectral_evidence,
        "extensions": {
            "channel_failures": {"by_channel": failures},
            "channel_order": {"channels": list(inputs.channels)},
            "channel_warnings": {"by_channel": warnings},
            "channel_quality": {"by_channel": channel_quality},
            "preprocessing": provenance,
        },
        "calculation_trace": {
            "bands": [name for name, *_ in official.RBP_BANDS],
            "window_s": end_s - start_s, "montage_channel_order": list(inputs.channels),
            "band_powers_uv2": band_powers,
            "denominator_uv2": {channel: sum(powers.values()) for channel, powers in band_powers.items()},
        },
    }
    if request_id or window_id:
        logger.info(
            "rbp_frame_perf request_id=%s window_id=%s recording_id=%s start_s=%.6f end_s=%.6f "
            "channel_count=%d spectrum_load_ms=%.1f band_integration_ms=%.1f total_ms=%.1f",
            request_id or "unknown", window_id or "unknown", inputs.recording_id, start_s, end_s,
            len(inputs.channels), spectrum_load_ms, band_integration_ms, (perf_counter() - started) * 1000,
        )
    return RbpFrame(arrays, quality, failure, evidence, provenance)
