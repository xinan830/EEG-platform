"""One scientific STFT outcome for static, full and on-demand execution."""

from collections.abc import Callable
from dataclasses import dataclass
from typing import Any

import numpy as np

from app.algorithm_runtime.contracts import AlgorithmFailure, AlgorithmInputs
from app.scientific.quality import SpectralQualityGateError
from app.scientific.contracts.spectrogram import (
    SPECTROGRAM_VERSION, STFT_PREPROCESSING_SCOPE, STFT_PREPROCESSING_VERSION, stft_context_s,
)

from .config import StftConfig
from .evidence import window_evidence, window_provenance
from .manifest import MANIFEST


def frequency_axis(sfreq_hz: float, low_hz: float, high_hz: float) -> np.ndarray:
    full = np.fft.rfftfreq(int(round(4.0 * sfreq_hz)), 1.0 / sfreq_hz)
    return full[(full >= low_hz) & (full <= high_hz)]


def relative_time_axis(sfreq_hz: float, duration_s: float) -> np.ndarray:
    segment = int(round(4.0 * sfreq_hz))
    starts = np.arange(0, int(round(duration_s * sfreq_hz)) - segment + 1,
                       int(round(sfreq_hz)))
    return (starts + segment / 2.0) / sfreq_hz


@dataclass(frozen=True)
class StftFrame:
    """Absolute centers and V²/Hz power; bad inner rows remain explicit NaN."""

    times_s: np.ndarray
    frequencies_hz: np.ndarray
    arrays: dict[str, np.ndarray]
    actual_range: dict[str, float] | None
    quality: str
    failure: AlgorithmFailure | None
    evidence: dict[str, Any]
    provenance: dict[str, Any]

    @property
    def window_failure(self) -> AlgorithmFailure | None:
        if self.quality == "partial":
            return _quality_failure(self.evidence["source_quality"])
        return self.failure

    @property
    def window_state(self) -> str:
        if self.window_failure is None:
            return "ready"
        return "unavailable" if self.quality == "unavailable" else "rejected"


def _quality_failure(quality: dict[str, Any]) -> AlgorithmFailure:
    return AlgorithmFailure(code="STFT_WINDOW_QUALITY_GATE_FAILED",
                            message="当前窗口包含未通过 STFT 质量门的时频窗", detail=quality)


def _failed_frame(inputs: AlgorithmInputs, config: StftConfig, failure: AlgorithmFailure,
                  quality: str, evidence: dict[str, Any]) -> StftFrame:
    frequencies = frequency_axis(inputs.sfreq_hz, config.low_hz, config.high_hz)
    return StftFrame(np.empty(0), frequencies,
                     {name: np.empty((0, frequencies.size)) for name in ("power_linear", "power_db")},
                     None, quality, failure, evidence, {})


def execute_stft_frame(
    inputs: AlgorithmInputs, config: StftConfig, start_s: float, end_s: float,
    check_cancelled: Callable[[], None] | None = None,
) -> StftFrame:
    """Load once through the typed scientific source; cancellation belongs to runtime."""
    check = check_cancelled or (inputs.execution_context.check_cancelled if inputs.execution_context else None)
    if check:
        check()
    payload = None
    failure = None
    quality = "unavailable"
    evidence: dict[str, Any] = {"calculation_trace": {"inner_valid_count": 0}}
    try:
        payload = inputs.payload.load_spectrogram(
            start_s=start_s, window_s=end_s - start_s, channels=[inputs.channel],
            low_hz=config.low_hz, high_hz=config.high_hz, notch_hz=config.notch_hz,
            output_low_hz=config.low_hz, output_high_hz=config.high_hz,
        )
    except SpectralQualityGateError as exc:
        quality = "gate_failed"
        failure = _quality_failure(dict(exc.quality))
        evidence.update(source_quality=dict(exc.quality), spectral_evidence=dict(exc.quality.get("evidence", {})))
    except (KeyError, TypeError, ValueError) as exc:
        failure = AlgorithmFailure(code="STFT_WINDOW_UNAVAILABLE", message=str(exc))
    if check:
        check()
    if failure is not None:
        return _failed_frame(inputs, config, failure, quality, evidence)
    try:
        frame = _validated_frame(inputs, config, payload, start_s, end_s)
    except (KeyError, TypeError, ValueError) as exc:
        if isinstance(payload, dict) and isinstance(payload.get("quality"), dict):
            evidence["source_quality"] = payload["quality"]
        failure = AlgorithmFailure(code="STFT_WINDOW_UNAVAILABLE", message=str(exc))
        frame = _failed_frame(inputs, config, failure, "unavailable", evidence)
    if check:
        check()
    return frame


def _validate_preprocessing(evidence: dict[str, Any], inputs: AlgorithmInputs,
                            config: StftConfig, start_s: float, duration_s: float) -> None:
    context_s = stft_context_s(config.low_hz)
    context_samples = int(np.ceil(context_s * inputs.sfreq_hz))
    start = int(round(start_s * inputs.sfreq_hz))
    end = start + int(round(duration_s * inputs.sfreq_hz))
    expected = {
        "algorithm_version": STFT_PREPROCESSING_VERSION, "scope": STFT_PREPROCESSING_SCOPE,
        "context_s": context_s, "context_samples": context_samples,
        "target_range_samples": {"start": start, "end": end},
        "read_range_samples": {"start": max(0, start - context_samples),
                               "end": min(int(round(inputs.duration_s * inputs.sfreq_hz)), end + context_samples)},
        "boundary_policy": "clamp_to_recording", "filter_phase": "zero_phase",
        "unit": "V", "channel_order": [inputs.channel], "gap_policy": "reject",
    }
    if any(evidence.get(key) != value for key, value in expected.items()):
        raise ValueError("STFT 有限上下文预处理证据与请求不一致")


def _validated_frame(inputs: AlgorithmInputs, config: StftConfig, payload: dict[str, Any],
                     start_s: float, end_s: float) -> StftFrame:
    times = np.asarray(payload["times_s"], dtype=float)
    frequencies = np.asarray(payload["frequencies_hz"], dtype=float)
    linear_uv = np.asarray(payload["power_linear"][inputs.channel], dtype=float)
    power_db = np.asarray(payload["power_db"][inputs.channel], dtype=float)
    actual_start = float(payload["window_start_s"])
    duration = float(payload["window_duration_s"])
    if not np.isfinite([actual_start, duration]).all() or duration <= 0:
        raise ValueError("STFT 实际源区间无效")
    tolerance = 1.5 / inputs.sfreq_hz
    if abs(actual_start - start_s) > tolerance or abs(actual_start + duration - end_s) > tolerance:
        raise ValueError("STFT 实际源区间与请求不一致")
    if payload["spectrogram_contract_version"] != SPECTROGRAM_VERSION or payload["segment_s"] != 4 or payload["step_s"] != 1:
        raise ValueError("STFT 科学源契约不一致")
    _validate_preprocessing(payload["preprocessing"], inputs, config, actual_start, duration)
    expected_times = relative_time_axis(inputs.sfreq_hz, duration) + actual_start
    expected_frequencies = frequency_axis(inputs.sfreq_hz, config.low_hz, config.high_hz)
    if (times.ndim != 1 or not times.size or times.shape != expected_times.shape
            or not np.isfinite(times).all() or not np.allclose(times, expected_times, rtol=0, atol=1e-9)):
        raise ValueError("STFT 时间轴与实际源样本窗不一致")
    if frequencies.ndim != 1 or not frequencies.size or not np.array_equal(frequencies, expected_frequencies):
        raise ValueError("STFT 频率轴与所选频段不一致")
    if linear_uv.shape != (times.size, frequencies.size) or power_db.shape != linear_uv.shape:
        raise ValueError("STFT 窗口矩阵 shape 与时频坐标不一致")
    source_quality = dict(payload["quality"])
    rows = list(source_quality["windows"])
    if len(rows) != times.size or any(row["status"] not in {"clean", "bad"} for row in rows):
        raise ValueError("STFT 质量记录必须逐行对应时频矩阵")
    bad = np.asarray([row["status"] == "bad" for row in rows])
    if source_quality["bad_windows"] != int(bad.sum()) or source_quality["total_windows"] != times.size:
        raise ValueError("STFT 质量计数与逐窗记录不一致")
    if (np.isinf(linear_uv).any() or np.isinf(power_db).any() or np.any(linear_uv < 0)
            or not np.isfinite(linear_uv[~bad]).all() or not np.isfinite(power_db[~bad]).all()):
        raise ValueError("STFT 矩阵包含未标记的非有限值或负功率")
    # Rejected rows never carry usable science, even if a source supplied finite values.
    linear = linear_uv.copy() * 1e-12
    power_db = power_db.copy()
    linear[bad] = np.nan
    power_db[bad] = np.nan
    status = "gate_failed" if bad.all() else "partial" if bad.any() else "clean"
    evidence = window_evidence(payload, valid_count=int((~bad).sum()))
    provenance = window_provenance(
        payload, start_s=actual_start, end_s=actual_start + duration, source_channel=inputs.channel,
        analysis_state="Partial" if duration < (config.window_s or MANIFEST.dynamic_policy.default_window_s) - 1e-9 else "Complete",
    )
    return StftFrame(times, frequencies, {"power_linear": linear, "power_db": power_db},
                     {"start_s": actual_start, "end_s": actual_start + duration}, status,
                     _quality_failure(source_quality) if bad.all() else None, evidence, provenance)
