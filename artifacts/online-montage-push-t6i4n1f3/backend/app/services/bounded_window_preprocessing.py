"""Read, zero-phase filter and crop a declared finite sample context."""

from dataclasses import dataclass
import logging
from typing import Any

import numpy as np

from app.scientific.primitives import preprocess_offline
from app.scientific.quality import SpectralQualityGateError
from app.services.recording_access import RecordingAccessSession


@dataclass(frozen=True)
class PreparedWindow:
    data: np.ndarray
    sfreq: float
    channel_names: tuple[str, ...]
    evidence: dict[str, object]


def prepare_window(
    session: RecordingAccessSession, *, start_sample: int, end_sample: int,
    channels: list[str] | None, low_hz: float, high_hz: float,
    notch_hz: float | None, context_s: float, algorithm_version: str,
    execution_context: Any = None,
) -> PreparedWindow:
    """Input/output are sample-major float64 V; the caller owns the session."""
    if not 0 <= start_sample < end_sample <= session.sample_count:
        raise ValueError("分析窗口样本范围超出录音边界")
    lookup = {name.casefold(): name for name in session.channel_names}
    try:
        requested = tuple(session.channel_names if channels is None else (
            lookup[name.casefold()] for name in channels
        ))
    except KeyError as exc:
        raise ValueError("频谱分析请求包含不存在的通道") from exc
    if not requested or len({name.casefold() for name in requested}) != len(requested):
        raise ValueError("频谱分析通道不能为空或重复")
    if not np.isfinite(context_s) or context_s < 0:
        raise ValueError("滤波上下文必须为有限非负秒数")
    context_samples = int(np.ceil(context_s * session.sampling_rate_hz))
    read_start = max(0, start_sample - context_samples)
    read_end = min(session.sample_count, end_sample + context_samples)
    check = execution_context.check_cancelled if execution_context is not None else None
    if check is not None:
        check()
    block = session.read_samples(read_start, read_end, requested, check_cancelled=check)
    evidence = {
        "algorithm_version": algorithm_version,
        "scope": "bounded_window_with_finite_context",
        "context_s": context_s, "context_samples": context_samples,
        "read_range_samples": {"start": read_start, "end": read_end},
        "target_range_samples": {"start": start_sample, "end": end_sample},
        "boundary_policy": "clamp_to_recording", "filter_phase": "zero_phase",
        "unit": "V", "channel_order": list(requested), "gap_policy": "reject",
    }
    logging.getLogger(__name__).debug(
        "bounded_window_source_read recording_id=%s version=%s target=%s:%s read=%s:%s channels=%s",
        session.identity.recording_id, algorithm_version, start_sample, end_sample,
        read_start, read_end, requested,
    )
    if block.unavailable_intervals or not np.isfinite(block.values_v).all():
        raise SpectralQualityGateError({
            "clean_segments": 0, "total_segments": 1, "clean_ratio": 0.0,
            "gate_failed": "bounded_source_gap", "rejected_reasons": ["source_gap"],
            "evidence": {"preprocessing": evidence, "gap": {
                "detected": True, "policy": "reject", "imputed": False,
                "unavailable_intervals": [
                    {"start_sample": item.start_sample, "end_sample": item.end_sample,
                     "reason": item.reason, "channels": list(item.channels)}
                    for item in block.unavailable_intervals
                ],
            }},
        })
    if check is not None:
        check()
    filtered = preprocess_offline(
        block.values_v, session.sampling_rate_hz,
        low_hz=low_hz, high_hz=high_hz, notch_hz=notch_hz,
    )
    offset = start_sample - read_start
    cropped = np.asarray(filtered[offset:offset + end_sample - start_sample], dtype=np.float64)
    if cropped.shape != (end_sample - start_sample, len(requested)):
        raise ValueError("Bounded preprocessing returned an invalid target shape")
    if check is not None:
        check()
    return PreparedWindow(cropped, float(session.sampling_rate_hz), requested, evidence)
