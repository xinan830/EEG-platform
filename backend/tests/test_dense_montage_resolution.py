"""Dense CAR must keep strict bindings without rescanning each reference pool."""
from copy import deepcopy
import json

import pytest

from app.services.recordings import acquisition_analysis_signals as signals


def dense_manifest(count=128):
    labels = [f"Dense_{i:03}" for i in range(count)]
    snapshot = {
        "Id": "dense-source", "DeviceSignature": "fixture",
        "HardwareReferenceElectrodeLocation": "REF", "HardwareGroundElectrodeLocation": "GND",
        "Channels": [dict(NativeChannelIndex=i, ElectrodeLabel=label, ExpectedKind=1)
                     for i, label in enumerate(labels)],
    }
    profile = {
        "Id": "dense-car", "DeviceSignature": "fixture", "ChannelConfigurationSnapshot": deepcopy(snapshot),
        "AverageReferenceLabels": labels,
        "DerivedChannels": [dict(Name=f"{label}-AVG", PositiveLabel=label, NegativeKind=2,
                                 NegativeLabels=labels, DisplayOrder=i) for i, label in enumerate(labels)],
    }
    return {
        "HardwareConfiguration": {
            "channel_configuration_snapshot_json": json.dumps(snapshot),
            "montage_configuration_snapshot_json": json.dumps(profile),
        },
        "Channels": [dict(StreamIndex=i, NativeChannelIndex=i, Label=label, Kind="Reference", Unit="V")
                     for i, label in enumerate(labels)],
    }


@pytest.mark.parametrize("count", [21, 128])
def test_dense_car_validates_distinct_sources_once(monkeypatch, count):
    manifest = dense_manifest(count)
    original, calls = signals._field, 0

    def tracked(*args, **kwargs):
        nonlocal calls
        calls += 1
        return original(*args, **kwargs)

    monkeypatch.setattr(signals, "_field", tracked)
    monkeypatch.setattr(signals, "_materialize_template_positions", lambda _: None)
    view = signals._read_acquisition_signal_view(manifest)
    assert len(view.signals) == count
    assert len(view.provenance["source_mapping"]) == count
    assert all(len(item.negatives) == count for item in view.signals)
    # Work spent on mapping lookups scales with source/output count, not pool reuse.
    assert calls < count * 70
    assert list(view.provenance["source_mapping"]) == [f"Dense_{i:03}" for i in range(count)]
    assert view.provenance["source_mapping"]["Dense_000"]["stream_index"] == 0


@pytest.mark.parametrize("defect", ["bound_duplicate", "source_duplicate", "history_duplicate",
                                   "missing", "native", "kind", "unit", "history_label"])
def test_indexed_resolution_keeps_ambiguous_and_invalid_binding_rejection(defect):
    manifest = dense_manifest(4)
    hardware = manifest["HardwareConfiguration"]
    recorded = json.loads(hardware["channel_configuration_snapshot_json"])
    profile = json.loads(hardware["montage_configuration_snapshot_json"])
    if defect == "bound_duplicate":
        profile["ChannelConfigurationSnapshot"]["Channels"].append(
            deepcopy(profile["ChannelConfigurationSnapshot"]["Channels"][0]))
    elif defect == "source_duplicate":
        manifest["Channels"].append(deepcopy(manifest["Channels"][0]))
    elif defect == "history_duplicate":
        recorded["Channels"].append(deepcopy(recorded["Channels"][0]))
    elif defect == "missing":
        manifest["Channels"].pop()
    elif defect == "native":
        manifest["Channels"][0]["NativeChannelIndex"] = 100
    elif defect == "kind":
        manifest["Channels"][0]["Kind"] = "Unknown"
    elif defect == "unit":
        manifest["Channels"][0]["Unit"] = "uV"
    else:
        recorded["Channels"][0]["ElectrodeLabel"] = "wrong"
    hardware["channel_configuration_snapshot_json"] = json.dumps(recorded)
    hardware["montage_configuration_snapshot_json"] = json.dumps(profile)
    with pytest.raises(ValueError, match="missing/ambiguous|historical/native/type/unit"):
        signals._read_acquisition_signal_view(manifest)
