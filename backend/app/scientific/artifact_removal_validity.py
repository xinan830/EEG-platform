"""Exact removal-application availability, independent of artifact detection."""
from __future__ import annotations

from collections.abc import Sequence

import numpy as np


def _summarize(intervals: list[dict], *, sfreq: float, start: int, end: int, channels: list[str]) -> dict:
    return {
        "schema_version": "artifact-removal-validity-v1",
        "sampling_rate_hz": sfreq, "start_sample": start, "end_sample": end,
        "channels": channels, "intervals": intervals,
        "unavailable_sample_count": sum(r["end_sample"] - r["start_sample"] for r in intervals),
        "output_unavailable_channel_samples": sum(
            (r["end_sample"] - r["start_sample"]) * len(r["unavailable_output_channels"]) for r in intervals),
        "additional_unavailable_channel_samples": sum(
            (r["end_sample"] - r["start_sample"]) * len(r["additional_unavailable_channels"]) for r in intervals),
    }


def build_application_validity(
    before_v: np.ndarray, after_v: np.ndarray, channel_names: Sequence[str], sfreq: float,
) -> dict[str, object]:
    """Run-length encode exact missing-input/output sets on the absolute sample timeline."""
    before, after = np.asarray(before_v), np.asarray(after_v)
    names = list(channel_names)
    if before.ndim != 2 or before.shape != after.shape or before.shape[1] != len(names):
        raise ValueError("去伪迹可用性输入/输出维度与通道不一致")
    if not np.isfinite(sfreq) or sfreq <= 0:
        raise ValueError("采样率必须为有限正数")
    missing_in, missing_out = ~np.isfinite(before), ~np.isfinite(after)
    intervals = []
    if len(before):
        changed = np.any(missing_in[1:] != missing_in[:-1], axis=1) | np.any(missing_out[1:] != missing_out[:-1], axis=1)
        boundaries = np.r_[0, np.flatnonzero(changed) + 1, len(before)]
        for start, end in zip(boundaries[:-1], boundaries[1:], strict=True):
            if not missing_out[start].any():
                continue
            extra = missing_out[start] & ~missing_in[start]
            intervals.append({
                "start_sample": int(start), "end_sample": int(end),
                "start_s": int(start) / sfreq, "end_s": int(end) / sfreq,
                "missing_input_channels": [name for i, name in enumerate(names) if missing_in[start, i]],
                "unavailable_output_channels": [name for i, name in enumerate(names) if missing_out[start, i]],
                "additional_unavailable_channels": [name for i, name in enumerate(names) if extra[i]],
                "reason": "missing_input_dependency" if extra.any() else "input_unavailable",
            })
    return _summarize(intervals, sfreq=float(sfreq), start=0, end=len(before), channels=names)


def project_application_validity(
    evidence: dict | None, start_sample: int, end_sample: int, channels: Sequence[str] | None = None,
) -> dict[str, object] | None:
    """Clip evidence to requested outputs; retain the full missing-input dependency."""
    if evidence is None:
        return None
    names = list(evidence["channels"] if channels is None else channels)
    selected = {name.casefold() for name in names}
    if not selected.issubset({name.casefold() for name in evidence["channels"]}):
        raise ValueError("可用性投影包含不存在的输出通道")
    start, end = max(start_sample, evidence["start_sample"]), min(end_sample, evidence["end_sample"])
    if start_sample < 0 or end_sample < start_sample or end < start:
        raise ValueError("可用性投影的绝对样本范围无效")
    sfreq = float(evidence["sampling_rate_hz"])
    intervals = []
    for row in evidence["intervals"]:
        left, right = max(start, row["start_sample"]), min(end, row["end_sample"])
        outputs = [name for name in row["unavailable_output_channels"] if name.casefold() in selected]
        if left >= right or not outputs:
            continue
        extra = [name for name in row["additional_unavailable_channels"] if name.casefold() in selected]
        intervals.append({**row, "start_sample": left, "end_sample": right,
            "start_s": left / sfreq, "end_s": right / sfreq,
            "unavailable_output_channels": outputs, "additional_unavailable_channels": extra,
            "reason": "missing_input_dependency" if extra else "input_unavailable"})
    return _summarize(intervals, sfreq=sfreq, start=start, end=end, channels=names)
