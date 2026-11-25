"""Display-only finite-run extrema; never resamples scientific input."""
from __future__ import annotations

import math

import numpy as np


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
