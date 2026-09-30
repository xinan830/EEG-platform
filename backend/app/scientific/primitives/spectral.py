"""Offline preprocessing, Welch PSD, band integration, and spectrograms."""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

import numpy as np
from scipy import signal

from app.scientific.contracts.analysis import ANALYSIS_CONTRACT
from app.scientific.quality.spectral import evaluate_spectral_window
from app.scientific.contracts.types import TransformPaddingEvidence


@dataclass(frozen=True)
class SpectralEstimate:
    freqs: np.ndarray
    psd: np.ndarray
    signal_quality: float
    clean_epochs: int
    total_epochs: int
    gate_failed: str | None
    rejected_reasons: tuple[str, ...] = ()
    evidence: dict[str, Any] = field(default_factory=dict)


def _gap_summary(checks: list[Any], values: np.ndarray | None = None, expected_samples: int | None = None) -> dict[str, Any]:
    if values is not None and values.ndim == 2:
        finite = np.isfinite(values).all(axis=1)
        non_finite_samples = int(np.count_nonzero(~finite))
        missing_samples = max((expected_samples or len(values)) - len(values), 0)
    else:
        missing_samples = int(sum(check.gap.missing_samples for check in checks))
        non_finite_samples = int(sum(check.gap.non_finite_samples for check in checks))
    return {
        "detected": any(check.gap.detected for check in checks),
        "policy": "reject",
        "missing_samples": missing_samples,
        "non_finite_samples": non_finite_samples,
        "imputed": False,
    }


def _spectral_evidence(checks: list[Any], *, transform_padding: TransformPaddingEvidence, values: np.ndarray | None = None, expected_samples: int | None = None) -> dict[str, Any]:
    peak_values = [check.peak_uv for check in checks if check.peak_uv is not None and np.isfinite(check.peak_uv)]
    peak_uv = max(peak_values) if peak_values else None
    threshold_uv = float(ANALYSIS_CONTRACT["artifact_peak_uv"])
    return {
        "schema_version": "spectral-window-evidence-v1",
        "welch": {
            "segment_s": float(ANALYSIS_CONTRACT["welch_segment_s"]),
            "segment_samples": expected_samples,
            "overlap_fraction": float(ANALYSIS_CONTRACT["welch_segment_overlap"]),
            "overlap_samples": None,
            "step_s": float(ANALYSIS_CONTRACT["welch_step_s"]),
            "window": str(ANALYSIS_CONTRACT["welch_window"]),
            "scaling": str(ANALYSIS_CONTRACT["welch_scaling"]),
        },
        "gap": _gap_summary(checks, values, expected_samples),
        "transform_padding": transform_padding.as_dict(),
        "amplitude": {
            "peak_uv": peak_uv,
            "threshold_uv": threshold_uv,
            "exceeded_uv": max(0.0, peak_uv - threshold_uv) if peak_uv is not None else None,
            "exceeded": peak_uv is not None and peak_uv > threshold_uv,
        },
    }


def preprocess_offline(
    data: np.ndarray,
    sfreq: float,
    *,
    low_hz: float | None = None,
    high_hz: float | None = None,
    notch_hz: float | None = None,
    notch_q: float = 30.0,
) -> np.ndarray:
    """Apply optional zero-phase power-line notch and SOS bandpass filters."""
    values = np.asarray(data, dtype=float)
    if values.ndim != 2 or not len(values):
        raise ValueError("脑电数据必须是非空二维数组")
    default_low, default_high = (float(value) for value in ANALYSIS_CONTRACT["bandpass_hz"])
    low = default_low if low_hz is None else float(low_hz)
    high = default_high if high_hz is None else float(high_hz)
    if not np.isfinite(low) or not np.isfinite(high) or low <= 0.0 or high <= low:
        raise ValueError("PSD 预处理频率范围无效")
    if high >= float(sfreq) / 2:
        raise ValueError(f"分析高切必须低于奈奎斯特频率（{float(sfreq) / 2:g}Hz）")
    if notch_hz is not None:
        notch = float(notch_hz)
        if not np.isfinite(notch) or notch <= 0.0 or notch >= float(sfreq) / 2:
            raise ValueError("陷波频率必须位于 0 与奈奎斯特频率之间")
        if not np.isfinite(notch_q) or notch_q <= 0.0:
            raise ValueError("陷波质量因数必须为正数")
        notch_b, notch_a = signal.iirnotch(notch, float(notch_q), fs=float(sfreq))
        try:
            values = signal.filtfilt(notch_b, notch_a, values, axis=0)
        except ValueError as exc:
            raise ValueError("记录太短，无法完成零相位陷波") from exc
    sos = signal.butter(
        int(ANALYSIS_CONTRACT["bandpass_prototype_order"]),
        [low, high], btype="bandpass", fs=float(sfreq), output="sos",
    )
    try:
        return signal.sosfiltfilt(sos, values, axis=0)
    except ValueError as exc:
        raise ValueError("记录太短，无法完成离线零相位滤波") from exc


def estimate_welch_psd(
    data: np.ndarray,
    sfreq: float,
    *,
    low_hz: float | None = None,
    high_hz: float | None = None,
) -> SpectralEstimate:
    """Compute overlapping Welch segments and average clean segments only."""
    values = np.asarray(data, dtype=float)
    segment_samples = int(round(float(ANALYSIS_CONTRACT["welch_segment_s"]) * sfreq))
    overlap = float(ANALYSIS_CONTRACT["welch_segment_overlap"])
    overlap_samples = int(round(segment_samples * overlap))
    step = max(1, int(round(segment_samples * (1.0 - overlap))))
    bounds = range(0, max(0, len(values) - segment_samples + 1), step)
    segments = [values[start:start + segment_samples] for start in bounds]
    checks = [evaluate_spectral_window(item, segment_samples) for item in segments]
    clean = [item for item, check in zip(segments, checks) if check.status == "clean"]
    rejected_reasons = tuple(dict.fromkeys(reason for check in checks for reason in check.reasons))
    if not segments:
        rejected_reasons = ("missing_samples",)
    quality = len(clean) / len(segments) if segments else 0.0
    minimum = float(ANALYSIS_CONTRACT["minimum_clean_epoch_ratio"])
    if not segments or not clean or quality < minimum:
        evidence = _spectral_evidence(
            checks,
            transform_padding=TransformPaddingEvidence(),
            values=values,
            expected_samples=segment_samples,
        )
        evidence["welch"]["overlap_samples"] = overlap_samples
        return SpectralEstimate(
            np.array([]), np.empty((values.shape[1], 0)), quality,
            len(clean), len(segments), "low_quality", rejected_reasons,
            evidence=evidence,
        )
    spectra = []
    freqs = np.array([])
    for item in clean:
        freqs, segment_psd = signal.welch(
            item, fs=sfreq, window=str(ANALYSIS_CONTRACT["welch_window"]),
            nperseg=segment_samples, noverlap=overlap_samples, detrend="constant",
            scaling=str(ANALYSIS_CONTRACT["welch_scaling"]), axis=0,
        )
        spectra.append(segment_psd.T)
    default_low, default_high = (float(value) for value in ANALYSIS_CONTRACT["bandpass_hz"])
    low = default_low if low_hz is None else float(low_hz)
    high = default_high if high_hz is None else float(high_hz)
    mask = (freqs >= low) & (freqs <= high)
    averaged = np.mean(np.stack(spectra), axis=0)[:, mask]
    evidence = _spectral_evidence(
        checks,
        transform_padding=TransformPaddingEvidence(),
        values=values,
        expected_samples=segment_samples,
    )
    evidence["welch"]["overlap_samples"] = overlap_samples
    return SpectralEstimate(
        freqs[mask], np.maximum(averaged, 1e-20), quality,
        len(clean), len(segments), None, rejected_reasons,
        evidence=evidence,
    )


def band_power(freqs: np.ndarray, psd: np.ndarray, low: float, high: float) -> np.ndarray | float:
    """Integrate a continuous band after linearly interpolating its edges."""
    x = np.asarray(freqs, dtype=float)
    values = np.asarray(psd, dtype=float)
    if x.ndim != 1 or values.shape[-1] != len(x) or len(x) < 2:
        raise ValueError("PSD 与频率轴形状不匹配")
    if not np.all(np.diff(x) > 0) or low >= high or low < x[0] or high > x[-1]:
        raise ValueError("频段边界无效或超出频率轴")
    interior = (x > low) & (x < high)
    integration_freqs = np.concatenate(([low], x[interior], [high]))
    flat = values.reshape((-1, len(x)))
    integration_values = np.vstack([np.interp(integration_freqs, x, row) for row in flat])
    result = np.trapezoid(integration_values, integration_freqs, axis=-1)
    result = result.reshape(values.shape[:-1])
    return float(result) if np.ndim(result) == 0 else result


def estimate_spectrogram(data: np.ndarray, sfreq: float, *, low_hz: float = 1.0, high_hz: float = 30.0) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Compute a 4 s Hann spectrogram with 1 s steps; output is V²/Hz."""
    values = np.asarray(data, dtype=float)
    segment_samples = int(round(4.0 * sfreq))
    step_samples = int(round(1.0 * sfreq))
    if values.ndim != 2 or len(values) < segment_samples:
        raise ValueError("时频图至少需要 4 秒数据")
    frames = [values[start:start + segment_samples] for start in range(0, len(values) - segment_samples + 1, step_samples)]
    window = signal.get_window("hann", segment_samples)
    freqs = np.fft.rfftfreq(segment_samples, 1.0 / sfreq)
    spectra = []
    for frame in frames:
        centered = frame - np.mean(frame, axis=0, keepdims=True)
        transformed = np.fft.rfft(centered * window[:, None], axis=0)
        density = np.abs(transformed) ** 2 / (sfreq * np.sum(window ** 2))
        density[1:-1] *= 2.0
        spectra.append(density.T)
    if low_hz < 0.0 or high_hz <= low_hz or high_hz >= sfreq / 2.0:
        raise ValueError("时频分析频率范围无效或超出奈奎斯特频率")
    mask = (freqs >= low_hz) & (freqs <= high_hz)
    if not np.any(mask):
        raise ValueError("时频分析频率范围没有可用频率点")
    starts = np.arange(0, len(values) - segment_samples + 1, step_samples, dtype=float)
    centers = (starts + segment_samples / 2.0) / sfreq
    return centers, freqs[mask], np.stack(spectra)[:, :, mask]


def estimate_spectrogram_with_quality(data: np.ndarray, sfreq: float, *, low_hz: float = 1.0, high_hz: float = 30.0) -> tuple[np.ndarray, np.ndarray, np.ndarray, list[dict[str, object]]]:
    """Return spectrogram power and one quality record for every time window."""
    values = np.asarray(data, dtype=float)
    segment_samples = int(round(4.0 * sfreq))
    step_samples = int(round(1.0 * sfreq))
    if values.ndim != 2 or len(values) < segment_samples:
        raise ValueError("时频图至少需要 4 秒数据")
    starts = np.arange(0, len(values) - segment_samples + 1, step_samples, dtype=int)
    window = signal.get_window("hann", segment_samples)
    freqs = np.fft.rfftfreq(segment_samples, 1.0 / sfreq)
    if low_hz < 0.0 or high_hz <= low_hz or high_hz >= sfreq / 2.0:
        raise ValueError("时频分析频率范围无效或超出奈奎斯特频率")
    mask = (freqs >= low_hz) & (freqs <= high_hz)
    if not np.any(mask):
        raise ValueError("时频分析频率范围没有可用频率点")
    spectra: list[np.ndarray] = []
    quality: list[dict[str, object]] = []
    for start in starts:
        frame = values[start:start + segment_samples]
        check = evaluate_spectral_window(frame, segment_samples)
        bad_reason = check.reasons[0] if check.reasons else None
        # A bad input window is unavailable; do not silently turn recording
        # gaps into zeros. Transform padding, if ever introduced, must be
        # reported separately from input-gap evidence.
        if check.status != "clean":
            power = np.full((values.shape[1], int(np.count_nonzero(mask))), np.nan, dtype=float)
            spectra.append(power)
            center = (float(start) + segment_samples / 2.0) / sfreq
            quality.append({
                "center_s": center,
                "start_s": float(start) / sfreq,
                "end_s": (float(start) + segment_samples) / sfreq,
                "status": check.status,
                "reason": bad_reason,
                "reasons": list(check.reasons),
                "peak_uv": check.peak_uv,
                "gap": check.gap.as_dict(),
                "transform_padding": TransformPaddingEvidence().as_dict(),
            })
            continue
        centered = frame - np.mean(frame, axis=0, keepdims=True)
        centered = centered - np.mean(centered, axis=0, keepdims=True)
        transformed = np.fft.rfft(centered * window[:, None], axis=0)
        density = np.abs(transformed) ** 2 / (sfreq * np.sum(window ** 2))
        density[1:-1] *= 2.0
        power = density.T[:, mask]
        spectra.append(power)
        center = (float(start) + segment_samples / 2.0) / sfreq
        quality.append({
            "center_s": center,
            "start_s": float(start) / sfreq,
            "end_s": (float(start) + segment_samples) / sfreq,
            "status": check.status,
            "reason": bad_reason,
            "reasons": list(check.reasons),
            "peak_uv": check.peak_uv,
            "gap": check.gap.as_dict(),
            "transform_padding": TransformPaddingEvidence().as_dict(),
        })
    centers = np.asarray([item["center_s"] for item in quality], dtype=float)
    return centers, freqs[mask], np.stack(spectra), quality
