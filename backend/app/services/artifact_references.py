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
    with recordings.open_access_session(recording, raw_voltage_channels=True) as session:
        return {"recording_id": recording.id, "sampling_rate_hz": session.sampling_rate_hz,
                "sample_count": session.sample_count, "channels": [
                    {"name": name, "unit": "V"} for name in session.channel_names]}


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


def isolate_reference_sources(reader, selected, *, source_names=None, view=None):
    """Remove auxiliary sources before interpolation/CAR; leave underlying source identities intact."""
    from app.scientific.contracts.preprocessing import BadChannelSpec
    from app.services.bad_channel_reader import BadChannelExcludingSampleReader, apply_bad_channel_policy_to_montage_view
    source_names = tuple(source_names or reader.names)
    selected = {name.casefold() for name in selected}
    excluded = {i for i, name in enumerate(source_names) if name.casefold() in selected}
    aliases = tuple(reader.names[i] for i in excluded)
    if view is not None and aliases:
        view = apply_bad_channel_policy_to_montage_view(view, BadChannelSpec(policy="exclude", channels=aliases))
    if excluded:
        reader = BadChannelExcludingSampleReader(reader, [i for i in range(len(reader.names)) if i not in excluded])
    return reader, view


@contextmanager
def prepare_artifact_references(recordings, recording, spec, *, sample_count, sfreq,
                                filters: ScientificFilterSpec, allocate, release,
                                check_cancelled=lambda: None):
    """Read bounded blocks on the verified recording clock; allocate within caller budget."""
    selected = (*spec.eog_channels, *spec.ecg_channels)
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
        yield ArtifactReferenceSignals(target, names, names[:len(spec.eog_channels)], names[len(spec.eog_channels):])
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
