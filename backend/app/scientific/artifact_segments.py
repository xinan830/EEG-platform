"""Offline EEG artifact-segment detection and rejection using public MNE APIs.

All signal inputs/outputs are sample-major float64 arrays in SI volts (V).
Artifact bounds use recording-relative half-open sample intervals [start_sample, end_sample).
Rejected samples are never zero-filled or fabricated.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Sequence
import warnings

import mne
import numpy as np
from scipy.ndimage import maximum_filter1d, minimum_filter1d

from app.scientific.contracts.preprocessing import ArtifactRejectionSpec


@dataclass(frozen=True)
class ArtifactSegment:
    """Half-open recording-relative artifact interval [start_sample, end_sample)."""

    start_sample: int
    end_sample: int
    reason: str
    channels: tuple[str, ...] = ()
    peak_metric: float | None = None

    def to_dict(self, sampling_rate_hz: float) -> dict[str, object]:
        return {
            "start_sample": self.start_sample,
            "end_sample": self.end_sample,
            "start_s": round(self.start_sample / sampling_rate_hz, 6),
            "end_s": round(self.end_sample / sampling_rate_hz, 6),
            "duration_s": round((self.end_sample - self.start_sample) / sampling_rate_hz, 6),
            "reason": self.reason,
            "channels": list(self.channels),
            "peak_metric": None if self.peak_metric is None else round(float(self.peak_metric), 4),
        }


@dataclass(frozen=True)
class ArtifactSegmentReport:
    sampling_rate_hz: float
    sample_count: int
    start_sample_offset: int
    segments: tuple[ArtifactSegment, ...]
    rejected_sample_count: int
    clean_ratio: float
    muscle_band_hz: tuple[float, float] | None
    muscle_check_status: str

    def to_dict(self) -> dict[str, object]:
        return {
            "sampling_rate_hz": self.sampling_rate_hz,
            "sample_count": self.sample_count,
            "start_sample": self.start_sample_offset,
            "end_sample": self.start_sample_offset + self.sample_count,
            "rejected_sample_count": self.rejected_sample_count,
            "clean_ratio": round(self.clean_ratio, 6),
            "muscle_band_hz": None if self.muscle_band_hz is None else [self.muscle_band_hz[0], self.muscle_band_hz[1]],
            "muscle_check_status": self.muscle_check_status,
            "segments": [item.to_dict(self.sampling_rate_hz) for item in self.segments],
        }


def _select_muscle_band_hz(sfreq_hz: float) -> tuple[tuple[float, float] | None, str]:
    nyquist = sfreq_hz / 2.0
    if sfreq_hz >= 300.0:
        return (110.0, 140.0), "mne_standard_110_140_hz"
    if sfreq_hz >= 150.0 and nyquist > 68.0:
        return (60.0, round(min(95.0, nyquist - 5.0), 2)), "adapted_high_freq_band"
    return None, "skipped_low_sampling_rate"


def _contiguous_true_runs(mask: np.ndarray) -> list[tuple[int, int]]:
    if mask.size == 0 or not np.any(mask):
        return []
    padded = np.concatenate(([False], mask, [False]))
    changes = np.diff(padded.astype(np.int8))
    starts = np.flatnonzero(changes == 1)
    ends = np.flatnonzero(changes == -1)
    return [(int(s), int(e)) for s, e in zip(starts, ends, strict=True)]


def _detect_amplitude_ptp_segments(
    data_v: np.ndarray,
    sfreq_hz: float,
    channel_names: tuple[str, ...],
    *,
    threshold_v: float,
    window_s: float,
    min_duration_samples: int,
    pad_samples: int,
    offset: int,
) -> list[ArtifactSegment]:
    n_samples, n_channels = data_v.shape
    if n_samples < 2 or n_channels == 0:
        return []
    win = max(2, min(n_samples, int(round(window_s * sfreq_hz))))
    results: list[ArtifactSegment] = []
    for ch_idx in range(n_channels):
        col = data_v[:, ch_idx]
        finite = np.isfinite(col)
        # Evaluate [start, start + win) at every origin; discard incomplete tail windows.
        maxima = maximum_filter1d(np.where(finite, col, -np.inf), win, origin=-(win // 2),
                                  mode='constant', cval=-np.inf)
        minima = minimum_filter1d(np.where(finite, col, np.inf), win, origin=-(win // 2),
                                  mode='constant', cval=np.inf)
        ptp = maxima[:n_samples - win + 1] - minima[:n_samples - win + 1]
        merged_ch: list[tuple[int, int, float]] = []
        for start, stop in _contiguous_true_runs(ptp > threshold_v):
            end = stop + win - 1
            peak_uv = float(np.max(ptp[start:stop])) * 1e6
            if merged_ch and start <= merged_ch[-1][1]:
                prev_s, prev_e, prev_peak = merged_ch[-1]
                merged_ch[-1] = (prev_s, max(prev_e, end), max(prev_peak, peak_uv))
            else:
                merged_ch.append((start, end, peak_uv))
        for start, end, peak_uv in merged_ch:
            if end - start < min_duration_samples:
                continue
            padded_s = max(0, start - pad_samples)
            padded_e = min(n_samples, end + pad_samples)
            results.append(ArtifactSegment(
                start_sample=offset + padded_s,
                end_sample=offset + padded_e,
                reason="amplitude_ptp",
                channels=(channel_names[ch_idx],),
                peak_metric=peak_uv,
            ))
    return results


def _detect_muscle_segments_mne(
    data_v: np.ndarray,
    sfreq_hz: float,
    channel_names: tuple[str, ...],
    *,
    threshold_z: float,
    band_hz: tuple[float, float],
    min_duration_samples: int,
    pad_samples: int,
    offset: int,
) -> list[ArtifactSegment]:
    n_samples, n_channels = data_v.shape
    min_span = max(int(round(sfreq_hz * 1.0)), 64)
    if n_samples < min_span or n_channels == 0:
        return []

    all_finite_rows = np.all(np.isfinite(data_v), axis=1)
    spans = [(s, e) for s, e in _contiguous_true_runs(all_finite_rows) if (e - s) >= min_span]
    if not spans:
        return []

    info = mne.create_info(list(channel_names), sfreq=sfreq_hz, ch_types="eeg")
    results: list[ArtifactSegment] = []
    for span_start, span_end in spans:
        segment_data = np.ascontiguousarray(data_v[span_start:span_end].T, dtype=np.float64)
        raw = mne.io.RawArray(segment_data, info, verbose=False)
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", RuntimeWarning)
            annotations, scores = mne.preprocessing.annotate_muscle_zscore(
                raw,
                ch_type="eeg",
                threshold=float(threshold_z),
                min_length_good=0.2,
                filter_freq=band_hz,
                verbose=False,
            )
        for onset_s, duration_s in zip(annotations.onset, annotations.duration, strict=True):
            local_s = max(0, int(round(float(onset_s) * sfreq_hz)))
            local_e = min(span_end - span_start, int(round(float(onset_s + duration_s) * sfreq_hz)))
            if local_e - local_s < min_duration_samples:
                continue
            peak_z = float(np.max(scores[local_s:local_e])) if local_e > local_s else float(threshold_z)
            padded_s = max(0, span_start + local_s - pad_samples)
            padded_e = min(n_samples, span_start + local_e + pad_samples)
            results.append(ArtifactSegment(
                start_sample=offset + padded_s,
                end_sample=offset + padded_e,
                reason="muscle_zscore",
                channels=channel_names,
                peak_metric=peak_z,
            ))
    return results


def _merge_segments(segments: Sequence[ArtifactSegment]) -> tuple[ArtifactSegment, ...]:
    """Merge overlapping segments ONLY within the same (reason, channel-set) to avoid cross-channel contamination."""
    if not segments:
        return ()
    by_scope: dict[tuple[str, tuple[str, ...]], list[ArtifactSegment]] = {}
    for seg in segments:
        ch_key = () if not seg.channels else tuple(sorted({ch.casefold() for ch in seg.channels}))
        by_scope.setdefault((seg.reason, ch_key), []).append(seg)

    scope_merged: list[ArtifactSegment] = []
    for group in by_scope.values():
        group.sort(key=lambda item: (item.start_sample, item.end_sample))
        acc: list[ArtifactSegment] = [group[0]]
        for seg in group[1:]:
            prev = acc[-1]
            if seg.start_sample <= prev.end_sample:
                peak = max((v for v in (prev.peak_metric, seg.peak_metric) if v is not None), default=None)
                acc[-1] = ArtifactSegment(
                    start_sample=prev.start_sample,
                    end_sample=max(prev.end_sample, seg.end_sample),
                    reason=prev.reason,
                    channels=prev.channels,
                    peak_metric=peak,
                )
            else:
                acc.append(seg)
        scope_merged.extend(acc)

    # Combine segments only when they share the exact same [start, end) and reason (never widens any channel span).
    by_exact_span: dict[tuple[int, int, str], ArtifactSegment] = {}
    for seg in sorted(scope_merged, key=lambda item: (item.start_sample, item.end_sample, item.reason, item.channels)):
        key = (seg.start_sample, seg.end_sample, seg.reason)
        existing = by_exact_span.get(key)
        if existing is None:
            by_exact_span[key] = seg
            continue
        combined_chs = () if (not existing.channels or not seg.channels) else tuple(dict.fromkeys((*existing.channels, *seg.channels)))
        peak = max((v for v in (existing.peak_metric, seg.peak_metric) if v is not None), default=None)
        by_exact_span[key] = ArtifactSegment(
            start_sample=existing.start_sample,
            end_sample=existing.end_sample,
            reason=existing.reason,
            channels=combined_chs,
            peak_metric=peak,
        )
    return tuple(sorted(by_exact_span.values(), key=lambda item: (item.start_sample, item.end_sample, item.reason, item.channels)))


def detect_artifact_segments(
    values_v: np.ndarray,
    sfreq_hz: float,
    channel_names: Sequence[str],
    spec: ArtifactRejectionSpec,
    *,
    start_sample_offset: int = 0,
    muscle_values_v: np.ndarray | None = None,
    valid_channel_names: Sequence[str] | None = None,
) -> ArtifactSegmentReport:
    """Detect amplitude-PTP, MNE muscle z-score, and manual bad segments on sample-major V data.

    Pass unfiltered broadband ``muscle_values_v`` when ``values_v`` has already been low-pass
    filtered below the muscle EMG band so 110-140 Hz EMG bursts are not attenuated away.
    """
    norm = spec.normalized()
    data = np.asarray(values_v, dtype=np.float64)
    if data.ndim != 2:
        raise ValueError("坏段检测输入必须是二维样本矩阵 (samples, channels)")
    names = tuple(str(name) for name in channel_names)
    if data.shape[1] != len(names):
        raise ValueError("通道名称数量与样本矩阵列数不匹配")
    muscle_data = data if muscle_values_v is None else np.asarray(muscle_values_v, dtype=np.float64)
    if muscle_data.shape != data.shape:
        raise ValueError("肌电检测源矩阵形状必须与主信号矩阵一致")
    rate = float(sfreq_hz)
    if not np.isfinite(rate) or rate <= 0:
        raise ValueError("采样率必须为有限正数")

    n_samples = int(data.shape[0])
    offset = int(start_sample_offset)
    if not norm.is_active:
        return ArtifactSegmentReport(rate, n_samples, offset, (), 0, 1.0, None, "disabled")

    min_dur_samples = max(1, int(round(norm.min_duration_s * rate)))
    pad_samples = max(0, int(round(norm.pad_s * rate)))
    found: list[ArtifactSegment] = []

    if norm.peak_to_peak_uv is not None:
        found.extend(_detect_amplitude_ptp_segments(
            data, rate, names,
            threshold_v=norm.peak_to_peak_uv * 1e-6,
            window_s=norm.amplitude_window_s,
            min_duration_samples=min_dur_samples,
            pad_samples=pad_samples,
            offset=offset,
        ))

    muscle_band: tuple[float, float] | None = None
    muscle_status = "disabled"
    if norm.muscle_zscore_threshold is not None:
        muscle_band, muscle_status = _select_muscle_band_hz(rate)
        if muscle_band is not None:
            found.extend(_detect_muscle_segments_mne(
                muscle_data, rate, names,
                threshold_z=norm.muscle_zscore_threshold,
                band_hz=muscle_band,
                min_duration_samples=min_dur_samples,
                pad_samples=pad_samples,
                offset=offset,
            ))

    window_end = offset + n_samples
    channel_lookup = {name.casefold(): name for name in names}
    valid_lookup = (
        channel_lookup
        if valid_channel_names is None
        else {str(name).casefold(): str(name) for name in valid_channel_names}
    )
    for manual in norm.manual_segments:
        canonical_chs: tuple[str, ...] = ()
        if manual.channels:
            resolved_chs: list[str] = []
            for ch in manual.channels:
                folded = ch.casefold()
                if folded not in valid_lookup:
                    raise ValueError(f"ARTIFACT_SEGMENT_CHANNEL_NOT_FOUND: 手动坏段指定的通道 {ch} 不存在于当前信号中")
                matched = channel_lookup.get(folded)
                if matched is not None:
                    resolved_chs.append(matched)
            if not resolved_chs:
                continue
            canonical_chs = tuple(resolved_chs)
        clipped_s = max(offset, manual.start_sample)
        clipped_e = min(window_end, manual.end_sample)
        if clipped_e > clipped_s:
            found.append(ArtifactSegment(
                start_sample=clipped_s,
                end_sample=clipped_e,
                reason=manual.reason,
                channels=canonical_chs,
            ))

    segments = _merge_segments(found)
    rejected_mask = np.zeros(n_samples, dtype=bool)
    for seg in segments:
        s = max(0, seg.start_sample - offset)
        e = min(n_samples, seg.end_sample - offset)
        if e > s:
            rejected_mask[s:e] = True
    rejected_count = int(np.count_nonzero(rejected_mask))
    clean_ratio = 1.0 if n_samples == 0 else float(1.0 - rejected_count / n_samples)

    return ArtifactSegmentReport(
        sampling_rate_hz=rate,
        sample_count=n_samples,
        start_sample_offset=offset,
        segments=segments,
        rejected_sample_count=rejected_count,
        clean_ratio=clean_ratio,
        muscle_band_hz=muscle_band,
        muscle_check_status=muscle_status,
    )


def apply_artifact_rejection_mask(
    values_v: np.ndarray,
    segments: Sequence[ArtifactSegment],
    channel_names: Sequence[str],
    *,
    start_sample_offset: int = 0,
    in_place: bool = False,
) -> np.ndarray:
    """Mark rejected artifact intervals as explicit NaN (never zero-fill)."""
    target = values_v if in_place else np.array(values_v, dtype=np.float64, copy=True)
    if not segments or target.size == 0:
        return target
    n_samples = int(target.shape[0])
    lookup = {str(name).casefold(): idx for idx, name in enumerate(channel_names)}
    offset = int(start_sample_offset)
    for seg in segments:
        s = max(0, seg.start_sample - offset)
        e = min(n_samples, seg.end_sample - offset)
        if e <= s:
            continue
        if not seg.channels:
            target[s:e, :] = np.nan
        else:
            col_indices: list[int] = []
            for ch in seg.channels:
                idx = lookup.get(ch.casefold())
                if idx is None:
                    raise ValueError(f"ARTIFACT_SEGMENT_CHANNEL_NOT_FOUND: 坏段指定的通道 {ch} 不存在于当前信号中")
                col_indices.append(idx)
            target[s:e, col_indices] = np.nan
    return target


def overlapping_artifact_segments(
    segments: Sequence[ArtifactSegment],
    start_sample: int,
    end_sample: int,
    requested_channels: Sequence[str] | None = None,
) -> tuple[ArtifactSegment, ...]:
    """Return artifact segments overlapping [start_sample, end_sample) on requested channels."""
    requested = None if requested_channels is None else {str(ch).casefold() for ch in requested_channels}
    hits: list[ArtifactSegment] = []
    for seg in segments:
        if seg.end_sample <= start_sample or seg.start_sample >= end_sample:
            continue
        if requested is not None and seg.channels:
            if not any(ch.casefold() in requested for ch in seg.channels):
                continue
        hits.append(seg)
    return tuple(hits)
