"""IAPF 频谱质量与伪迹处理的纯函数。"""

from __future__ import annotations

import numpy as np
import mne


def select_alpha_peak(fit, alpha_search_range):
    low, high = alpha_search_range
    peaks = np.asarray(fit.get("peaks", np.empty((0, 3))), dtype=float)
    alpha_peaks = [peak for peak in peaks if len(peak) >= 3 and low <= peak[0] <= high]
    if not alpha_peaks:
        return None, 0.0, None, False
    alpha_peaks.sort(key=lambda peak: peak[1], reverse=True)
    cf, power, bandwidth = alpha_peaks[0][:3]
    return float(cf), max(0.0, float(power)), float(bandwidth), True


def alpha_center_of_gravity(freqs, psd, fit, alpha_search_range):
    low, high = alpha_search_range
    fit_freqs = np.asarray(fit.get("fit_freqs"), dtype=float)
    ap_log = np.asarray(fit.get("aperiodic_fit"), dtype=float)
    if fit_freqs.size == 0 or ap_log.size != fit_freqs.size:
        return None
    psd_fit = np.interp(fit_freqs, np.asarray(freqs, dtype=float), np.asarray(psd, dtype=float))
    residual = psd_fit - np.power(10.0, ap_log)
    alpha_mask = np.logical_and(fit_freqs >= low, fit_freqs <= high)
    weights = np.clip(residual[alpha_mask], 0.0, None)
    if weights.sum() <= 0:
        return None
    return float(np.sum(fit_freqs[alpha_mask] * weights) / np.sum(weights))


def alpha_residual_ratio(freqs, psd, fit, alpha_search_range):
    low, high = alpha_search_range
    fit_freqs = np.asarray(fit.get("fit_freqs"), dtype=float)
    ap_log = np.asarray(fit.get("aperiodic_fit"), dtype=float)
    if fit_freqs.size == 0 or ap_log.size != fit_freqs.size:
        return float("nan")
    psd_fit = np.interp(fit_freqs, np.asarray(freqs, dtype=float), np.asarray(psd, dtype=float))
    residual = np.clip(psd_fit - np.power(10.0, ap_log), 0.0, None)
    total_mask = np.logical_and(fit_freqs >= 3.0, fit_freqs <= 30.0)
    alpha_mask = np.logical_and(fit_freqs >= low, fit_freqs <= high)
    total = float(np.sum(residual[total_mask]))
    if total <= 0.0:
        return float("nan")
    return float(np.sum(residual[alpha_mask]) / total)


def peak_quality(peak_exists, peak_power, gaussian_cf, cog, prominence_min, agreement_hz):
    if cog is None or not np.isfinite(float(cog)):
        return "unavailable"
    if not peak_exists:
        return "cog_only"
    if gaussian_cf is None or not np.isfinite(float(gaussian_cf)):
        return "weak_peak"
    if peak_power < prominence_min or abs(float(cog) - float(gaussian_cf)) > agreement_hz:
        return "weak_peak"
    return "strong_peak"


def absorb_short_good_spans(bad_mask, sfreq, welch_seg_seconds):
    """把短于一个 Welch 子段的干净段并入 BAD，原地修改 mask。"""
    min_good = max(1, int(sfreq * welch_seg_seconds))
    good = ~bad_mask
    if not good.any():
        return
    edges = np.diff(np.concatenate(([False], good, [False])).astype(np.int8))
    for start, end in zip(np.flatnonzero(edges == 1), np.flatnonzero(edges == -1)):
        if end - start < min_good:
            bad_mask[start:end] = True


def annotate_peak_amplitude(raw, sfreq, artifact_window_seconds, gate_artifact_uv, welch_seg_seconds):
    """标注超幅伪迹窗口，并吸收不足 Welch 长度的干净段。"""
    data = raw.get_data()
    nperseg = max(1, int(sfreq * artifact_window_seconds))
    threshold = gate_artifact_uv * 1e-6
    bad_mask = np.zeros(data.shape[1], dtype=bool)
    for start in range(0, data.shape[1] - nperseg + 1, nperseg):
        if np.max(np.abs(data[:, start:start + nperseg])) > threshold:
            bad_mask[start:start + nperseg] = True
    absorb_short_good_spans(bad_mask, sfreq, welch_seg_seconds)

    onsets, durations = [], []
    in_bad = False
    for index, is_bad in enumerate(bad_mask):
        if is_bad and not in_bad:
            onset_sample = index
            in_bad = True
        elif not is_bad and in_bad:
            onsets.append(onset_sample / sfreq)
            durations.append((index - onset_sample) / sfreq)
            in_bad = False
    if in_bad:
        onsets.append(onset_sample / sfreq)
        durations.append((len(bad_mask) - onset_sample) / sfreq)
    if not onsets:
        return mne.Annotations([], [], [])
    return mne.Annotations(
        onset=onsets,
        duration=durations,
        description=["BAD_peak"] * len(onsets),
        orig_time=raw.info["meas_date"],
    )
