"""Scientific identity for reusable continuous preprocessing, not display state."""

from app.core.provenance import implementation_version, sha256_json
from app.scientific.contracts.analysis import ANALYSIS_CONTRACT


def prepared_source_identity(recordings, recording, *, low_hz, high_hz, notch_hz, notch_q,
                             channel_names=None, bad_channel_spec=None, preprocessing_spec=None):
    selected_channels = tuple(channel_names) if channel_names is not None else None
    payload = {
        "schema_version": "continuous-prepared-source-v1",
        "source_sha256": recording.source_sha256,
        "sampling_rate_hz": recording.sfreq,
        "duration_s": recording.duration_s,
        "channel_order": recording.channels,
        "selected_channels": list(selected_channels) if selected_channels is not None else None,
        "raw_labels": recording.raw_channel_labels,
        "canonical_labels": recording.canonical_channel_labels,
        "channel_types": recording.channel_types,
        "channel_units": recording.channel_units,
        "import_version": recording.import_version,
        "signal_source": recordings.analysis_signal_provenance(recording),
        "algorithm_version": ANALYSIS_CONTRACT["algorithm_version"],
        "implementation_build": implementation_version(),
        "preprocessing_scope": "whole_recording_before_window_slice",
        "bandpass_hz": [low_hz, high_hz], "notch_hz": notch_hz, "notch_q": notch_q,
    }
    if preprocessing_spec is not None and (
        preprocessing_spec.bad_channels.normalized().is_active
        or preprocessing_spec.artifact_rejection.normalized().is_active
        or preprocessing_spec.artifact_removal.normalized().is_active
    ):
        payload["offline_preprocessing_spec"] = preprocessing_spec.to_evidence()
    elif bad_channel_spec is not None and bad_channel_spec.normalized().is_active:
        payload["bad_channels"] = bad_channel_spec.normalized().to_evidence()
    return sha256_json(payload)
