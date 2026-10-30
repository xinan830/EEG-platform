"""Finite-context preprocessing for TNK dynamic spectral windows."""

from __future__ import annotations

from dataclasses import dataclass
import logging
from typing import Any

import numpy as np

from app.scientific.primitives import preprocess_offline
from app.scientific.quality import SpectralQualityGateError


TNK_WINDOW_PREPROCESSING_VERSION = "tnk-window-spectral-v1"
TNK_FILTER_CONTEXT_S = 2.0
_LOGGER = logging.getLogger("brain_platform.dynamic_window.tnk")


@dataclass(frozen=True)
class TnkWindowPrepared:
    data: np.ndarray
    sfreq: float
    channel_names: tuple[str, ...]
    start_sample: int
    end_sample: int
    evidence: dict[str, object]


def prepare_tnk_window(
    recordings: Any,
    recording: Any,
    *,
    start_sample: int,
    end_sample: int,
    channels: list[str] | None,
    low_hz: float,
    high_hz: float,
    notch_hz: float | None,
    execution_context: Any = None,
) -> TnkWindowPrepared:
    """Read, filter, and crop one TNK range without preparing the full source."""
    session = recordings.open_access_session(recording, verify_content_hash=False)
    try:
        if not 0 <= start_sample < end_sample <= session.sample_count:
            raise ValueError("TNK 窗口样本范围超出录音边界")
        lookup = {name.casefold(): name for name in session.channel_names}
        requested = tuple(session.channel_names if channels is None else (
            lookup[name.casefold()] for name in channels
        ))
        if not requested:
            raise ValueError("TNK 频谱分析至少需要一个通道")
        if len({name.casefold() for name in requested}) != len(requested):
            raise ValueError("TNK 频谱分析通道不能重复")

        context_samples = int(np.ceil(TNK_FILTER_CONTEXT_S * session.sampling_rate_hz))
        read_start = max(0, start_sample - context_samples)
        read_end = min(session.sample_count, end_sample + context_samples)
        check_cancelled = (
            execution_context.check_cancelled
            if execution_context is not None else None
        )
        block = session.read_samples(
            read_start, read_end, requested, check_cancelled=check_cancelled,
        )
        _LOGGER.info(
            "tnk_window_source_read target_samples=%s:%s read_samples=%s:%s "
            "channels=%s context_samples=%s",
            start_sample, end_sample, read_start, read_end, requested, context_samples,
        )
        if not np.isfinite(block.values_v).all():
            raise SpectralQualityGateError({
                "clean_segments": 0,
                "total_segments": 1,
                "clean_ratio": 0.0,
                "gate_failed": "bounded_source_gap",
                "rejected_reasons": ["source_gap"],
                "evidence": {
                    "gap": {
                        "detected": True,
                        "policy": "reject",
                        "unavailable_intervals": [
                            {
                                "start_sample": item.start_sample,
                                "end_sample": item.end_sample,
                                "reason": item.reason,
                                "channels": list(item.channels),
                            }
                            for item in block.unavailable_intervals
                        ],
                        "imputed": False,
                    },
                },
            })
        filtered = preprocess_offline(
            block.values_v,
            session.sampling_rate_hz,
            low_hz=low_hz,
            high_hz=high_hz,
            notch_hz=notch_hz,
        )
        target_start = start_sample - read_start
        target_end = target_start + (end_sample - start_sample)
        cropped = np.asarray(filtered[target_start:target_end], dtype=np.float64)
        if cropped.shape != (end_sample - start_sample, len(requested)):
            raise ValueError("TNK bounded preprocessing returned an invalid target shape")
        if check_cancelled is not None:
            check_cancelled()
        return TnkWindowPrepared(
            cropped,
            float(session.sampling_rate_hz),
            requested,
            start_sample,
            end_sample,
            {
                "algorithm_version": TNK_WINDOW_PREPROCESSING_VERSION,
                "scope": "bounded_window_with_finite_context",
                "context_s": TNK_FILTER_CONTEXT_S,
                "context_samples": context_samples,
                "read_range_samples": {"start": read_start, "end": read_end},
                "target_range_samples": {"start": start_sample, "end": end_sample},
                "boundary_policy": "clamp_to_recording",
                "filter_phase": "zero_phase",
                "unit": "V",
                "channel_order": list(requested),
                "gap_policy": "reject",
            },
        )
    finally:
        session.close()
