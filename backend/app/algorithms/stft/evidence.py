"""Scientific window evidence shared by STFT's two offline executors."""

from typing import Any


def window_evidence(payload: dict[str, Any], *, valid_count: int) -> dict[str, Any]:
    quality = dict(payload["quality"])
    return {
        "source_quality": quality,
        "spectral_evidence": dict(quality.get("evidence", {})),
        "calculation_trace": {
            "spectrogram_contract_version": payload["spectrogram_contract_version"],
            "segment_s": float(payload["segment_s"]),
            "step_s": float(payload["step_s"]),
            "inner_time_count": valid_count,
            "inner_valid_count": valid_count,
            "frequency_range_hz": payload.get("frequency_range_hz"),
            "filter_frequency_range_hz": payload.get("filter_frequency_range_hz"),
            "notch_hz": payload.get("notch_hz"),
        },
    }


def window_provenance(
    payload: dict[str, Any],
    *,
    start_s: float,
    end_s: float,
    source_channel: str,
    analysis_state: str,
) -> dict[str, Any]:
    """Return the immutable coordinate/preprocessing contract for one window."""
    return {
        "preprocessing_scope": "whole_recording_before_window_slice",
        "spectrogram_contract_version": payload["spectrogram_contract_version"],
        "segment_s": float(payload["segment_s"]),
        "step_s": float(payload["step_s"]),
        "outer_window": {"start_s": start_s, "end_s": end_s},
        "inner_time_axis": "relative_window_time_center_s",
        "source_channel": source_channel,
        "frequency_range_hz": payload.get("frequency_range_hz"),
        "filter_frequency_range_hz": payload.get("filter_frequency_range_hz"),
        "notch_hz": payload.get("notch_hz"),
        "analysis_state": analysis_state,
    }
