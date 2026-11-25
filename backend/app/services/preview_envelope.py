"""Display-only finite-run extrema; never resamples scientific input."""
from __future__ import annotations

import math

import numpy as np


MAX_PAIRED_PREVIEW_POINTS_PER_TRACE = 16000
MAX_PAIRED_PREVIEW_RESPONSE_SCALARS = 1000000


class PreviewEnvelopeLimitError(ValueError):
    """The gap-preserving display response exceeds its finite resource budget."""


def _check_envelope_limit(count: int, limit: int) -> None:
    if count > limit:
        raise PreviewEnvelopeLimitError("科学对比缺口或通道过多，超出预览资源上限；请缩短显示时间窗后重试。")


def preview_envelope_indices(values: np.ndarray, *, target_points: int) -> list[int]:
    """Preserve each bucket's first/min/max/last and every non-finite interval.

    The point target is soft: mandatory finite-run boundaries and gap markers may
    exceed it, because dropping a gap would falsely imply continuous valid data.
    Returns original indices without changing units or sample coordinates.
    """
    count = len(values)
    if count == 0:
        return []
    if target_points < 4:
        raise ValueError("预览点数至少为 4")
    finite = np.isfinite(values)
    boundaries = np.r_[0, np.flatnonzero(finite[1:] != finite[:-1]) + 1, count]
    bucket_size = max(1, math.ceil(count / max(1, target_points // 4)))
    indices: list[int] = []
    for begin, end in zip(boundaries[:-1], boundaries[1:], strict=True):
        begin, end = int(begin), int(end)
        if not finite[begin]:
            indices.append(begin)
            continue
        for first in range(begin, end, bucket_size):
            stop = min(end, first + bucket_size)
            block = values[first:stop]
            indices.extend(sorted({first, stop - 1, first + int(np.argmin(block)), first + int(np.argmax(block))}))
    return indices


def build_preview_envelope(
    values_v: np.ndarray, *, start_sample: int, sfreq: float, target_points: int,
) -> tuple[list[float], list[float | None]]:
    """Display envelope with absolute second coordinates and microvolt values."""
    indices = preview_envelope_indices(values_v, target_points=target_points)
    times = [round((start_sample + index) / sfreq, 6) for index in indices]
    values = [round(float(values_v[index]) * 1e6, 4) if np.isfinite(values_v[index]) else None for index in indices]
    return times, values


def paired_preview_envelope_indices(
    baseline_v: np.ndarray,
    processed_v: np.ndarray,
    *,
    target_points: int,
    max_output_points: int = MAX_PAIRED_PREVIEW_POINTS_PER_TRACE,
) -> list[int]:
    """Select shared sample indices preserving both traces' extrema and gap boundaries.

    Splits by the joint finiteness state of both arrays so no NaN interval on
    either side is bridged. Each finite bucket contributes up to 8 candidate
    indices (first, last, and min/max for each finite side), sorted and deduped.
    """
    count = len(baseline_v)
    if count != len(processed_v):
        raise ValueError("配对预览压缩的两侧样本长度不一致")
    if count == 0:
        return []
    if target_points < 8:
        raise ValueError("配对预览点数至少为 8")

    base_finite = np.isfinite(baseline_v)
    proc_finite = np.isfinite(processed_v)
    state = base_finite.astype(np.int8) + 2 * proc_finite.astype(np.int8)
    boundaries = np.r_[0, np.flatnonzero(state[1:] != state[:-1]) + 1, count]
    # Each finite-state run needs at least one point; reject before building lists.
    _check_envelope_limit(len(boundaries) - 1, max_output_points)

    # When both arrays are identical objects (all-disabled), 4 pts/bucket suffices;
    # otherwise budget up to 8 candidate indices per bucket for the joint union.
    per_bucket = 4 if baseline_v is processed_v else 8
    bucket_size = max(1, math.ceil(count / max(1, target_points // per_bucket)))
    indices: list[int] = []
    for begin, end in zip(boundaries[:-1], boundaries[1:], strict=True):
        begin, end = int(begin), int(end)
        bf, pf = bool(base_finite[begin]), bool(proc_finite[begin])
        if not bf and not pf:
            _check_envelope_limit(len(indices) + 1, max_output_points)
            indices.append(begin)
            continue
        for first in range(begin, end, bucket_size):
            stop = min(end, first + bucket_size)
            picked = {first, stop - 1}
            if bf:
                b_block = baseline_v[first:stop]
                picked.add(first + int(np.argmin(b_block)))
                picked.add(first + int(np.argmax(b_block)))
            if pf and baseline_v is not processed_v:
                p_block = processed_v[first:stop]
                picked.add(first + int(np.argmin(p_block)))
                picked.add(first + int(np.argmax(p_block)))
            _check_envelope_limit(len(indices) + len(picked), max_output_points)
            indices.extend(sorted(picked))
    return indices


def build_paired_preview_envelope(
    baseline_v: np.ndarray,
    processed_v: np.ndarray,
    *,
    start_sample: int,
    sfreq: float,
    target_points: int,
    max_output_points: int = MAX_PAIRED_PREVIEW_POINTS_PER_TRACE,
) -> tuple[list[float], list[float | None], list[float | None]]:
    """Return shared absolute-second coordinates and per-side microvolt values without interpolation."""
    indices = paired_preview_envelope_indices(
        baseline_v, processed_v, target_points=target_points, max_output_points=max_output_points,
    )
    times = [round((start_sample + index) / sfreq, 6) for index in indices]
    base_uv = [
        round(float(baseline_v[index]) * 1e6, 4) if np.isfinite(baseline_v[index]) else None
        for index in indices
    ]
    if baseline_v is processed_v:
        return times, base_uv, list(base_uv)
    proc_uv = [
        round(float(processed_v[index]) * 1e6, 4) if np.isfinite(processed_v[index]) else None
        for index in indices
    ]
    return times, base_uv, proc_uv
