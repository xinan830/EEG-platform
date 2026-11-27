"""Prepare auxiliary reference channels and resolve preprocessing analysis channel layouts."""
from contextlib import contextmanager

import numpy as np

from app.scientific.artifact_reference_signals import ArtifactReferenceSignals
from app.scientific.contracts.preprocessing import ScientificFilterSpec
from app.scientific.primitives import preprocess_blocks
from app.services.recordings.recording_channel_roles import discover_artifact_references


def resolve_preprocessing_analysis_channels(recordings, recording, preprocessing, montage_id=None):
    """Expose the execution reader's EEG layout without filtering or reading sample blocks."""
    bad_spec, _, removal = preprocessing.to_specs()
    selected = (*removal.eog_channels, *removal.ecg_channels)
    if selected:
        catalogue = discover_artifact_references(recordings, recording)
        available = {c["name"].casefold() for c in catalogue["channels"]}
        if any(name.casefold() not in available for name in selected):
            raise ValueError("ARTIFACT_REFERENCE_UNAVAILABLE: 选择的辅助通道不存在或不是电压信号")
    with recordings.open_access_session(
        recording,
        analysis_montage_id=montage_id,
        bad_channel_spec=bad_spec,
        artifact_reference_channels=selected,
    ) as session:
        return {"recording_id": recording.id, "channels": list(session.channel_names)}


@contextmanager
def prepare_artifact_references(
    recordings,
    recording,
    spec,
    *,
    sample_count,
    sfreq,
    filters: ScientificFilterSpec,
    allocate,
    release,
    check_cancelled=lambda: None,
):
    """Read bounded blocks on the verified recording clock; allocate within caller budget."""
    from app.services.recordings.recording_channel_roles import dedicated_reference_sources, source_identity_lookup

    selected = (*spec.eog_channels, *spec.ecg_channels)
    storage_dir = getattr(recordings, "storage_dir", None)
    dedicated_reference_sources(storage_dir, recording, selected)
    lookup_roles = source_identity_lookup(storage_dir, recording)
    if any(lookup_roles[name.casefold()] != "ecg" for name in spec.ecg_channels):
        raise ValueError("ARTIFACT_REFERENCE_INVALID: 心电参考必须确认是专用 ECG 通道")
    if any(lookup_roles[name.casefold()] not in {"eeg", "eog"} for name in spec.eog_channels):
        raise ValueError("ARTIFACT_REFERENCE_INVALID: 眼电参考必须是 EEG 代理或专用 EOG")
    eog_channels = tuple(name for name in spec.eog_channels if lookup_roles[name.casefold()] == "eog")
    if spec.method == "eog_regression" and len(eog_channels) != len(spec.eog_channels):
        raise ValueError("ARTIFACT_REFERENCE_INVALID: EOG 回归只接受专用 EOG；EEG 代理请使用 ICA 或 SSP")
    selected = (*eog_channels, *spec.ecg_channels)
    if not selected:
        yield None
        return
    source = target = None
    try:
        with recordings.open_access_session(recording, raw_voltage_channels=True) as session:
            if session.sample_count != sample_count or session.sampling_rate_hz != sfreq:
                raise ValueError("ARTIFACT_REFERENCE_ALIGNMENT_INVALID: 辅助信号采样轴不一致")
            lookup = {name.casefold(): name for name in session.channel_names}
            if any(name.casefold() not in lookup for name in selected):
                raise ValueError("ARTIFACT_REFERENCE_UNAVAILABLE: 选择的辅助通道不存在或不是电压信号")
            names = tuple(lookup[name.casefold()] for name in selected)
            source = allocate((sample_count, len(names)))
            target = allocate((sample_count, len(names)))
            for start in range(0, sample_count, 65536):
                check_cancelled()
                end = min(sample_count, start + 65536)
                source[start:end] = session.read_samples(start, end, names, check_cancelled=check_cancelled).values_v
        # Filter finite runs independently so one source gap does not poison the entire reference.
        target[:] = np.nan
        for column in range(len(names)):
            finite = np.isfinite(source[:, column])
            changes = np.diff(np.r_[False, finite, False].astype(np.int8))
            for start, end in zip(np.flatnonzero(changes == 1), np.flatnonzero(changes == -1), strict=True):
                if end - start <= 27:
                    continue
                preprocess_blocks(
                    source[start:end, column:column + 1],
                    target[start:end, column:column + 1],
                    sfreq,
                    allocate,
                    low_hz=filters.low_hz,
                    high_hz=filters.high_hz,
                    notch_hz=filters.notch_hz,
                    notch_q=filters.notch_q,
                    check_cancelled=check_cancelled,
                    release=release,
                )
        yield ArtifactReferenceSignals(target, names, names[:len(eog_channels)], names[len(eog_channels):])
    finally:
        if source is not None:
            release(source)
        if target is not None:
            release(target)
