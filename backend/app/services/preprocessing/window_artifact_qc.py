"""Gate bounded algorithm windows using the authoritative continuous artifact report."""

from app.scientific.artifact_reports import ARTIFACT_DETECTION_VERSION, project_artifact_report
from app.scientific.contracts.preprocessing import OfflinePreprocessingSpec
from app.scientific.quality import SpectralQualityGateError
from app.services.preprocessing.analysis_preprocess_cache import PreprocessedRecording
from app.services.preprocessing.bounded_window_preprocessing import PreparedWindow


def apply_window_artifact_qc(
    prepared: PreparedWindow, spec: OfflinePreprocessingSpec, *,
    start_sample: int, end_sample: int, annotated_source: PreprocessedRecording,
) -> None:
    """Project full-context detections; never refit detectors on a cropped channel/window."""
    report = project_artifact_report(
        annotated_source.artifact_report, start_sample, end_sample, prepared.channel_names,
    )
    report_dict = report.to_dict()
    report_dict['qc_inputs'] = {
        'ptp_source': 'wideband_detrended_0.5_45hz' if spec.filter_spec.high_hz < 45 else 'analysis_filtered',
        'muscle_source': 'broadband_source',
    }
    report_dict['detection_scope'] = {
        'version': ARTIFACT_DETECTION_VERSION, 'scope': 'whole_recording_before_window_slice',
        'start_sample': 0, 'end_sample': len(annotated_source.data),
        'channels': list(annotated_source.channel_names),
        'prepared_source_identity': annotated_source.contract.identity,
    }
    prepared.evidence['artifact_rejection'] = report_dict
    if spec.artifact_rejection.policy == 'reject' and report.segments:
        raise SpectralQualityGateError({
            'clean_segments': 0, 'total_segments': 1, 'clean_ratio': report.clean_ratio,
            'gate_failed': 'artifact_segment_rejected',
            'rejected_reasons': sorted({seg.reason for seg in report.segments}),
            'evidence': {
                'preprocessing': prepared.evidence, 'artifact_rejection': report_dict,
                'artifact_intervals': report_dict['segments'],
            },
        })
