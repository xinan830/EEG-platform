"""Shared frozen spectral execution snapshot for runtime algorithms."""

from __future__ import annotations

from app.algorithm_runtime.contracts import AlgorithmConfigBase, AlgorithmExecutionSnapshot
from app.scientific.contracts.analysis import ANALYSIS_CONTRACT


def spectral_execution_snapshot(
    config: AlgorithmConfigBase, *,
    low_hz: float = float(ANALYSIS_CONTRACT["bandpass_hz"][0]),
    high_hz: float = float(ANALYSIS_CONTRACT["bandpass_hz"][1]),
    notch_hz: float | None = None,
) -> AlgorithmExecutionSnapshot:
    """Use actual module filters; search-band field names carry no filter meaning."""
    return AlgorithmExecutionSnapshot(
        window={
            "mode": config.mode,
            "welch_segment_s": ANALYSIS_CONTRACT["welch_segment_s"],
            "welch_overlap": ANALYSIS_CONTRACT["welch_segment_overlap"],
            "dynamic_window_s": config.window_s if config.mode == "dynamic" else None,
            "refresh_step_s": config.step_s if config.mode == "dynamic" else None,
            "alignment": "window_end" if config.mode == "dynamic" else "range",
        },
        filters={
            key: ANALYSIS_CONTRACT[key]
            for key in (
                "bandpass_type",
                "bandpass_prototype_order",
                "bandpass_hz",
                "preprocessing_phase",
                "filter_form",
            )
        } | {
            "bandpass_hz": [float(low_hz), float(high_hz)],
            "notch_hz": notch_hz,
            "notch_quality_factor": 30.0 if notch_hz is not None else None,
        },
        quality_rules={
            "minimum_clean_ratio": ANALYSIS_CONTRACT["minimum_clean_epoch_ratio"],
            "artifact_peak_uv": ANALYSIS_CONTRACT["artifact_peak_uv"],
            "reasons": ["non_finite", "amplitude_threshold", "flatline", "clipping", "missing_samples"],
        },
    )
