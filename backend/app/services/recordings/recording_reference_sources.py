"""Low-level recording reference channel discovery and reader isolation primitives."""

from __future__ import annotations

import numpy as np

from app.scientific.contracts.preprocessing import BadChannelSpec
from app.services.recordings.bad_channel_reader import (
    BadChannelExcludingSampleReader,
    apply_bad_channel_policy_to_montage_view,
    predict_excluded_output_channels,
)


def voltage_source_indexes(raw, path) -> list[int]:
    """Verify physical dimensions and native clocks before MNE can infer or resample them."""
    import pyedflib

    voltage_units = {"V", "mV", "uV", "µV", "μV", "\x83\xcaV"}
    with pyedflib.EdfReader(str(path)) as source:
        labels = tuple(source.getSignalLabels())
        if labels != tuple(raw.ch_names):
            raise ValueError("ARTIFACT_REFERENCE_INVALID: 原始辅助通道名称或顺序无法唯一验证")
        indexes = [
            i for i in range(source.signals_in_file)
            if source.getPhysicalDimension(i).strip() in voltage_units
        ]
        rates = source.getSampleFrequencies()
        if any(not np.isclose(rates[i], raw.info["sfreq"], rtol=0, atol=1e-12) for i in indexes):
            raise ValueError("ARTIFACT_REFERENCE_ALIGNMENT_INVALID: 辅助通道原生采样率不一致，请先显式重采样")
        return indexes


def exclude_reference_outputs(channel_names, source_names, selected, provenance=None, bad_spec=None):
    """Predict EEG outputs after removing selected raw auxiliary dependencies."""
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
        return predict_excluded_output_channels(
            channel_names,
            BadChannelSpec(policy="exclude", channels=tuple(aliases)),
            provenance,
        )
    return tuple(name for name in channel_names if name.casefold() not in {n.casefold() for n in aliases})


def isolate_reference_sources(reader, selected, *, source_names=None, view=None, bad_spec=None):
    """Remove auxiliary sources before interpolation/CAR; leave underlying source identities intact."""
    source_names = tuple(source_names or reader.names)
    selected = {name.casefold() for name in selected}
    excluded = {i for i, name in enumerate(source_names) if name.casefold() in selected}
    aliases = tuple(reader.names[i] for i in excluded)
    # A source assigned a physiological role is no longer an EEG bad-channel target.
    if bad_spec is not None and selected:
        from dataclasses import replace

        reference_labels = selected | {name.casefold() for name in aliases}
        bad_spec = replace(
            bad_spec,
            channels=tuple(name for name in bad_spec.channels if name.casefold() not in reference_labels),
        ).normalized()
    if view is not None and aliases:
        view = apply_bad_channel_policy_to_montage_view(view, BadChannelSpec(policy="exclude", channels=aliases))
    if excluded:
        reader = BadChannelExcludingSampleReader(reader, [i for i in range(len(reader.names)) if i not in excluded])
    return reader, view, bad_spec
