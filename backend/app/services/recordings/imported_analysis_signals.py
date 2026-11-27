"""Imported EDF/BDF sidecar channel and montage resolution for offline analysis."""

from __future__ import annotations

import json

from app.models.recording import RecordingSummary
from app.services.recordings.acquisition_analysis_signals import (
    AcquisitionSignalView,
    _catalogue_entry,
    _catalogue_projection_key,
    _channel_kind,
    _field,
    _read_acquisition_signal_view,
)
from app.services.recordings.wpf_recording import WpfRecordingError


def imported_source_channel_labels(recording) -> tuple[str, ...]:
    """Resolve mapped input labels independently of a selected montage's output channels."""
    snapshot = json.loads(recording.import_channel_configuration_snapshot_json)
    mapping = {
        int(item["source_column"]): int(item["native_channel_index"])
        for item in json.loads(recording.import_channel_column_mapping_json)
    }
    configured = {
        int(_field(item, "NativeChannelIndex")): item
        for item in _field(snapshot, "Channels", [])
    }
    return tuple(
        str(_field(configured[native_index], "ElectrodeLabel", "")).strip() or raw_label
        if (native_index := mapping.get(index)) in configured and channel_type.casefold() == "eeg"
        else raw_label
        for index, (raw_label, channel_type) in enumerate(
            zip(recording.raw_channel_labels, recording.channel_types, strict=True)
        )
    )


def resolve_imported_analysis_channels(summary: RecordingSummary) -> tuple[str, ...]:
    """Resolve enabled raw EEG analysis channels in source order (excluding disabled/non-EEG)."""
    channel_metadata_aligned = len(summary.channels) == len(summary.channel_types)
    if summary.import_channel_configuration_snapshot_json:
        if not channel_metadata_aligned:
            raise ValueError("RECORDING_CHANNEL_METADATA_MISMATCH: 导入通道类型与文件通道数量不匹配")
        snapshot = json.loads(summary.import_channel_configuration_snapshot_json)
        configured_channels = _field(snapshot, "Channels", []) or []
        if summary.import_channel_column_mapping_json:
            mapping = {
                int(item["source_column"]): int(item["native_channel_index"])
                for item in json.loads(summary.import_channel_column_mapping_json)
            }
            configured_by_index = {
                int(_field(channel, "NativeChannelIndex")): channel
                for channel in configured_channels
            }
            return tuple(
                label
                for index, (label, kind) in enumerate(zip(summary.channels, summary.channel_types))
                if kind.casefold() == "eeg"
                and (entry := configured_by_index.get(mapping.get(index))) is not None
                and _field(entry, "IsSelectedForDisplay") is True
                and bool(str(_field(entry, "ElectrodeLabel", "")).strip())
            )
        enabled = {
            str(_field(channel, "ElectrodeLabel", "")).strip().casefold()
            for channel in configured_channels
            if _field(channel, "IsSelectedForDisplay") is True
            and str(_field(channel, "ElectrodeLabel", "")).strip()
        }
        return tuple(
            label
            for label, kind in zip(summary.channels, summary.channel_types)
            if kind.casefold() == "eeg" and label.strip().casefold() in enabled
        )
    if channel_metadata_aligned:
        return tuple(
            label for label, kind in zip(summary.channels, summary.channel_types)
            if kind.casefold() == "eeg"
        ) or summary.channels
    return summary.channels


def read_imported_acquisition_signal_view(
    recording, montage_id: str | None = None,
) -> AcquisitionSignalView:
    """Resolve an imported EDF/BDF montage from its hash-bound sidecar snapshots."""
    try:
        if not recording.import_channel_configuration_snapshot_json or not recording.import_channel_column_mapping_json:
            raise ValueError("recording has no verified channel binding")
        snapshot = json.loads(recording.import_channel_configuration_snapshot_json)
        mapping = {
            int(item["source_column"]): int(item["native_channel_index"])
            for item in json.loads(recording.import_channel_column_mapping_json)
        }
        configured = {
            int(_field(item, "NativeChannelIndex")): item
            for item in _field(snapshot, "Channels", [])
        }
        source_channels = []
        source_labels = imported_source_channel_labels(recording)
        for index, (_label, channel_type) in enumerate(
            zip(recording.raw_channel_labels, recording.channel_types, strict=True)
        ):
            entry = configured.get(mapping.get(index)) if channel_type.casefold() == "eeg" else None
            source_channels.append({
                "Label": source_labels[index],
                "StreamIndex": index,
                "NativeChannelIndex": mapping.get(index, index),
                "Kind": _channel_kind(_field(entry, "ExpectedKind")) if entry else channel_type,
                "Unit": "V",
            })
        hardware = {
            "channel_configuration_snapshot_json": recording.import_channel_configuration_snapshot_json,
            "acquisition_montage_snapshot_json": recording.import_acquisition_montage_snapshot_json,
            "montage_configuration_snapshot_json": recording.import_montage_configuration_snapshot_json,
            "montage_catalogue_snapshot_json": recording.import_montage_catalogue_snapshot_json,
        }
        manifest = {"HardwareConfiguration": hardware, "Channels": source_channels}
        return _read_acquisition_signal_view(manifest, montage_id)
    except (OSError, KeyError, TypeError, ValueError, AttributeError) as exc:
        raise WpfRecordingError("ACQUISITION_MONTAGE_UNVERIFIABLE", f"导入记录的导联快照无法验证，不能用于分析：{exc}") from exc


def imported_montage_catalogue(recording) -> tuple[dict[str, object], ...]:
    """Describe only montage projections that were validated at import time."""
    if not recording.import_montage_configuration_snapshot_json:
        return ()
    profiles = json.loads(recording.import_montage_catalogue_snapshot_json or "[]")
    profile_ids = {str(_field(profile, "Id", "")) for profile in profiles}
    base = json.loads(recording.import_montage_configuration_snapshot_json)
    enabled_labels = {label.strip().casefold() for label in resolve_imported_analysis_channels(recording)}
    default_id = _field(json.loads(recording.import_acquisition_montage_snapshot_json or "{}"), "Id")
    default_id = default_id or _field(base, "Id")
    if str(_field(base, "Id", "")) not in profile_ids:
        profiles = [base, *profiles]
    entries = []
    seen = set()
    for profile in profiles:
        view = read_imported_acquisition_signal_view(recording, str(_field(profile, "Id", "")))
        entry = _catalogue_entry(view)
        for output in entry["outputs"]:
            dependencies = [output["formula"]["positive"], *output["formula"]["negative_labels"]]
            if any(label.casefold() not in enabled_labels for label in dependencies):
                output.update(available=False, unavailable_reason="required_input_channel_disabled")
        if entry["outputs"] and not any(output.get("available") is True for output in entry["outputs"]):
            first_reason = next(
                (output.get("unavailable_reason") for output in entry["outputs"] if output.get("unavailable_reason")),
                "required_input_channel_disabled",
            )
            entry.update(available=False, unavailable_reason=first_reason)
        entry["is_acquisition_default"] = entry["id"] == default_id
        key = _catalogue_projection_key(entry)
        if key not in seen:
            seen.add(key)
            entries.append(entry)
    return tuple(entries)


def enrich_imported_recording_summary(summary: RecordingSummary) -> RecordingSummary:
    """Attach enabled raw EEG analysis_channels and validated imported montage_catalogue."""
    analysis_channels = resolve_imported_analysis_channels(summary)
    enriched = RecordingSummary(**{**summary.__dict__, "analysis_channels": analysis_channels})
    if not enriched.import_montage_configuration_snapshot_json:
        return enriched
    catalogue = imported_montage_catalogue(enriched)
    return RecordingSummary(**{**enriched.__dict__, "montage_catalogue": catalogue})
