"""Owned temporary maps for unchanged continuous offline preprocessing."""

from __future__ import annotations

from typing import Any

from app.scientific.primitives import preprocess_blocks
from app.algorithm_runtime.execution_control import ExecutionControl
from app.services.preprocessing.analysis_preprocess_cache import PreprocessedRecording
from app.services.preprocessing.mapped_arrays import MappedArrays


def mapped_preprocess(recordings: Any, recording: Any, *, low_hz: float,
                      high_hz: float, notch_hz: float | None, notch_q: float = 30.0,
                      channels: list[str] | None = None, execution_context: ExecutionControl | None = None,
                      bad_channel_spec: Any = None, artifact_rejection_spec: Any = None,
                      artifact_removal_spec: Any = None,
                      annotated_source: PreprocessedRecording | None = None,
                      artifact_reference_channels=()) -> PreprocessedRecording:
    policy = getattr(execution_context, "preparation_resource_policy", None)
    owner = MappedArrays() if policy is None else MappedArrays(policy)
    session = None
    def check_cancelled():
        if execution_context is not None:
            execution_context.check_cancelled()
    try:
        reference_options = ({"artifact_reference_channels": artifact_reference_channels}
                             if artifact_reference_channels else {})
        session = (
            recordings.open_access_session(recording, **reference_options)
            if bad_channel_spec is None
            else recordings.open_access_session(recording, bad_channel_spec=bad_channel_spec, **reference_options)
        )
        lookup = {name.casefold(): name for name in session.channel_names}
        names = tuple(session.channel_names) if channels is None else tuple(lookup[name.casefold()] for name in channels)
        shape = (session.sample_count, len(names))
        target = owner.allocate(shape)
        block_samples = max(1, min(65536, (8 * 1024**2) // (8 * len(names))))
        source = None
        if annotated_source is not None:
            if (annotated_source.channel_names != names or annotated_source.data.shape != shape
                    or annotated_source.sfreq != session.sampling_rate_hz
                    or annotated_source.source_identity != session.identity
                    or annotated_source.artifact_report is None):
                raise ValueError('ARTIFACT_REPORT_SOURCE_MISMATCH: 坏段报告与当前输入不一致')
            for start in range(0, session.sample_count, block_samples):
                check_cancelled()
                stop = min(start + block_samples, session.sample_count)
                target[start:stop] = annotated_source.data[start:stop]
        else:
            source = owner.allocate(shape)
            for start in range(0, session.sample_count, block_samples):
                check_cancelled()
                stop = min(start + block_samples, session.sample_count)
                source[start:stop] = session.read_samples(
                    start, stop, names, check_cancelled=check_cancelled,
                ).values_v
            preprocess_blocks(source, target, session.sampling_rate_hz, owner.allocate,
                              low_hz=low_hz, high_hz=high_hz, notch_hz=notch_hz,
                              notch_q=notch_q, check_cancelled=check_cancelled, release=owner.release,
                              block_samples=block_samples)
        report = annotated_source.artifact_report if annotated_source is not None else None
        artifact_intervals: tuple[Any, ...] = ()
        if artifact_rejection_spec is not None and artifact_rejection_spec.normalized().is_active:
            from app.scientific.artifact_segments import apply_artifact_rejection_mask, detect_artifact_segments

            check_cancelled()
            norm_art = artifact_rejection_spec.normalized()
            # Use wideband detrended signal for PTP if the analysis low-pass is narrow (<45 Hz),
            # and broadband source for muscle z-score so 110-140 Hz EMG is never attenuated.
            ptp_source = target
            wide_hi = min(45.0, (session.sampling_rate_hz / 2.0) - 1.5)
            if report is None and norm_art.peak_to_peak_uv is not None and high_hz < 45.0 and wide_hi > max(high_hz, 1.0):
                from app.scientific.primitives import detrend_and_filter_for_inspection

                ptp_source, _ = detrend_and_filter_for_inspection(
                    source, session.sampling_rate_hz,
                    low_hz=min(low_hz, 0.5), high_hz=wide_hi, notch_hz=notch_hz, notch_q=notch_q,
                )
            report = report or detect_artifact_segments(
                ptp_source, session.sampling_rate_hz, names, norm_art,
                muscle_values_v=source, valid_channel_names=session.channel_names,
            )
            artifact_intervals = report.segments
            if norm_art.policy == "reject" and artifact_intervals:
                apply_artifact_rejection_mask(target, artifact_intervals, names, in_place=True)
        norm_rem = artifact_removal_spec.normalized() if artifact_removal_spec is not None else None
        keep_source_for_ica = source is not None and norm_rem is not None and norm_rem.method == "ica_infomax"
        if source is not None and not keep_source_for_ica:
            owner.release(source)
            source = None
        artifact_removal_evidence: dict[str, object] | None = None
        if norm_rem is not None and norm_rem.is_active:
            from app.services.preprocessing.artifact_removal_preparation import apply_prepared_removal
            from app.scientific.contracts.preprocessing import ScientificFilterSpec
            check_cancelled()
            try:
                target[:], artifact_removal_evidence = apply_prepared_removal(
                    recordings, recording, session, target, names, norm_rem,
                    filters=ScientificFilterSpec(low_hz, high_hz, notch_hz, notch_q),
                    bad_spec=bad_channel_spec, artifact_spec=artifact_rejection_spec,
                    owner=owner, check_cancelled=check_cancelled, raw_source=source)
            finally:
                if source is not None:
                    owner.release(source)
                    source = None
        session._validate_source()
        target.flush()
        return PreprocessedRecording(
            target, session.sampling_rate_hz, names, owner,
            artifact_intervals=artifact_intervals,
            artifact_removal_evidence=artifact_removal_evidence,
            artifact_report=report,
            source_identity=session.identity,
        )
    except BaseException:
        owner.close()
        raise
    finally:
        if session is not None:
            session.close()
