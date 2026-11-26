"""Discover and prepare raw-source references independently of EEG montage selection."""
from contextlib import contextmanager

import numpy as np

from app.scientific.artifact_reference_signals import ArtifactReferenceSignals
from app.scientific.contracts.preprocessing import ScientificFilterSpec
from app.scientific.primitives import preprocess_blocks


def voltage_source_indexes(raw, path) -> list[int]:
    """Verify physical dimensions and native clocks before MNE can infer or resample them."""
    import pyedflib
    voltage_units = {"V", "mV", "uV", "µV", "μV", "\x83\xcaV"}
    with pyedflib.EdfReader(str(path)) as source:
        labels = tuple(source.getSignalLabels())
        if labels != tuple(raw.ch_names):
            raise ValueError("ARTIFACT_REFERENCE_INVALID: 原始辅助通道名称或顺序无法唯一验证")
        indexes = [i for i in range(source.signals_in_file)
                   if source.getPhysicalDimension(i).strip() in voltage_units]
        rates = source.getSampleFrequencies()
        if any(not np.isclose(rates[i], raw.info["sfreq"], rtol=0, atol=1e-12) for i in indexes):
            raise ValueError("ARTIFACT_REFERENCE_ALIGNMENT_INVALID: 辅助通道原生采样率不一致，请先显式重采样")
        return indexes


def discover_artifact_references(recordings, recording) -> dict:
    from app.services.recording_channel_roles import source_channel_identities
    roles = source_channel_identities(recordings.storage_dir, recording)
    with recordings.open_access_session(recording, raw_voltage_channels=True) as session:
        return {"recording_id": recording.id, "source_sha256": recording.source_sha256, "sampling_rate_hz": session.sampling_rate_hz,
                "sample_count": session.sample_count, "channels": [
                    {"name": name, "unit": "V", "identity": roles.get(name, ("unconfirmed",))[0],
                     "identity_source": roles.get(name, (None, "unconfirmed"))[1]} for name in session.channel_names]}


def resolve_preprocessing_analysis_channels(recordings, recording, preprocessing, montage_id=None):
    """Expose the execution reader's EEG layout without filtering or reading sample blocks."""
    bad_spec, _, removal = preprocessing.to_specs()
    selected = (*removal.eog_channels, *removal.ecg_channels)
    if selected:
        catalogue = discover_artifact_references(recordings, recording)
        available = {c["name"].casefold() for c in catalogue["channels"]}
        if any(name.casefold() not in available for name in selected):
            raise ValueError("ARTIFACT_REFERENCE_UNAVAILABLE: 选择的辅助通道不存在或不是电压信号")
    with recordings.open_access_session(recording, analysis_montage_id=montage_id,
            bad_channel_spec=bad_spec, artifact_reference_channels=selected) as session:
        return {"recording_id": recording.id, "channels": list(session.channel_names)}


def exclude_reference_outputs(channel_names, source_names, selected, provenance=None, bad_spec=None):
    """Predict EEG outputs after removing selected raw auxiliary dependencies."""
    from app.scientific.contracts.preprocessing import BadChannelSpec
    from app.services.bad_channel_reader import predict_excluded_output_channels
    aliases = set(selected)
    folded = {name.casefold() for name in selected}
    mapping = (provenance or {}).get("source_mapping", {})
    for label, entry in mapping.items():
        index = entry.get("stream_index")
        if isinstance(index, int) and 0 <= index < len(source_names) and source_names[index].casefold() in folded:
            aliases.add(label)
    if bad_spec is not None and bad_spec.policy == "exclude":
        aliases.update(bad_spec.channels)
    formulas = (provenance or {}).get("formulas", [])
    if formulas:
        return predict_excluded_output_channels(channel_names,
            BadChannelSpec(policy="exclude", channels=tuple(aliases)), provenance)
    return tuple(name for name in channel_names if name.casefold() not in {n.casefold() for n in aliases})


def isolate_reference_sources(reader, selected, *, source_names=None, view=None, bad_spec=None):
    """Remove auxiliary sources before interpolation/CAR; leave underlying source identities intact."""
    from app.scientific.contracts.preprocessing import BadChannelSpec
    from app.services.bad_channel_reader import BadChannelExcludingSampleReader, apply_bad_channel_policy_to_montage_view
    source_names = tuple(source_names or reader.names)
    selected = {name.casefold() for name in selected}
    excluded = {i for i, name in enumerate(source_names) if name.casefold() in selected}
    aliases = tuple(reader.names[i] for i in excluded)
    # A source assigned a physiological role is no longer an EEG bad-channel target.
    if bad_spec is not None and selected:
        from dataclasses import replace
        reference_labels = selected | {name.casefold() for name in aliases}
        bad_spec = replace(bad_spec, channels=tuple(name for name in bad_spec.channels
            if name.casefold() not in reference_labels)).normalized()
    if view is not None and aliases:
        view = apply_bad_channel_policy_to_montage_view(view, BadChannelSpec(policy="exclude", channels=aliases))
    if excluded:
        reader = BadChannelExcludingSampleReader(reader, [i for i in range(len(reader.names)) if i not in excluded])
    return reader, view, bad_spec


@contextmanager
def prepare_artifact_references(recordings, recording, spec, *, sample_count, sfreq,
                                filters: ScientificFilterSpec, allocate, release,
                                check_cancelled=lambda: None):
    """Read bounded blocks on the verified recording clock; allocate within caller budget."""
    from app.services.recording_channel_roles import source_identity_lookup, dedicated_reference_sources
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
                preprocess_blocks(source[start:end, column:column+1], target[start:end, column:column+1],
                    sfreq, allocate, low_hz=filters.low_hz, high_hz=filters.high_hz,
                    notch_hz=filters.notch_hz, notch_q=filters.notch_q,
                    check_cancelled=check_cancelled, release=release)
        yield ArtifactReferenceSignals(target, names, names[:len(eog_channels)], names[len(eog_channels):])
    finally:
        if source is not None:
            release(source)
        if target is not None:
            release(target)


def prepare_reviewed_ica(recordings, recording, pre_ica, spec, filters):
    """Shared reference preparation for whole-recording ICA review and execution."""
    from app.scientific.artifact_removal import fit_ica_model
    from app.services.mapped_preprocessing import MappedArrays
    owner = MappedArrays()
    try:
        with prepare_artifact_references(recordings, recording, spec, sample_count=len(pre_ica.data),
                sfreq=pre_ica.sfreq, filters=filters, allocate=owner.allocate, release=owner.release) as references:
            return fit_ica_model(pre_ica.data, pre_ica.sfreq, pre_ica.channel_names, spec, references=references)
    finally:
        owner.close()
