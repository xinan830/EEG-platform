"""Apply offline bad-segment policy to a prepared finite-context target window."""

from typing import Any

from app.scientific.artifact_segments import detect_artifact_segments, overlapping_artifact_segments
from app.scientific.contracts.preprocessing import OfflinePreprocessingSpec
from app.scientific.quality import SpectralQualityGateError
from app.services.bounded_window_preprocessing import PreparedWindow


def apply_window_artifact_qc(
    prepared: PreparedWindow,
    spec: OfflinePreprocessingSpec,
    *,
    start_sample: int,
    end_sample: int,
    broadband_v: Any = None,
    valid_channel_names: Any = None,
) -> None:
    art_spec = spec.artifact_rejection
    if not art_spec.is_active:
        return
    ptp_data = prepared.data
    ptp_source = "analysis_filtered"
    if broadband_v is not None and spec.filter_spec.high_hz < 45.0:
        from app.scientific.primitives import detrend_and_filter_for_inspection

        wide_hi = min(45.0, (prepared.sfreq / 2.0) - 1.5)
        if wide_hi > max(spec.filter_spec.high_hz, 1.0):
            ptp_data, _ = detrend_and_filter_for_inspection(
                broadband_v,
                prepared.sfreq,
                low_hz=min(spec.filter_spec.low_hz, 0.5),
                high_hz=wide_hi,
                notch_hz=spec.filter_spec.notch_hz,
                notch_q=spec.filter_spec.notch_q,
            )
            ptp_source = "wideband_detrended_0.5_45hz"
    report = detect_artifact_segments(
        ptp_data,
        prepared.sfreq,
        prepared.channel_names,
        art_spec,
        start_sample_offset=start_sample,
        muscle_values_v=broadband_v if broadband_v is not None else prepared.data,
        valid_channel_names=valid_channel_names,
    )
    report_dict = report.to_dict()
    report_dict["qc_inputs"] = {
        "ptp_source": ptp_source,
        "muscle_source": "broadband_source" if broadband_v is not None else "analysis_filtered",
    }
    prepared.evidence["artifact_rejection"] = report_dict
    if art_spec.policy != "reject":
        return
    hits = overlapping_artifact_segments(
        report.segments, start_sample, end_sample, prepared.channel_names,
    )
    if hits:
        raise SpectralQualityGateError({
            "clean_segments": 0,
            "total_segments": 1,
            "clean_ratio": report.clean_ratio,
            "gate_failed": "artifact_segment_rejected",
            "rejected_reasons": sorted({seg.reason for seg in hits}),
            "evidence": {
                "preprocessing": prepared.evidence,
                "artifact_rejection": report_dict,
                "artifact_intervals": [seg.to_dict(prepared.sfreq) for seg in hits],
            },
        })
