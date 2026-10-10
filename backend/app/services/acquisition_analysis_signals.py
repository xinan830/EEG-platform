"""Immutable acquisition-montage signal view; raw recordings remain unchanged."""

from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass
from pathlib import Path

import numpy as np

from app.services.wpf_recording import WpfRecordingError


def _field(item: dict, name: str, default=None):
    return next((value for key, value in item.items() if key.casefold() == name.casefold()), default)


def _kind(value) -> str:
    return {"0": "originalhardwarereference", "1": "channel", "2": "mean", "3": "specifiedpair"}.get(
        str(value), str(value).casefold())


def _channel_kind(value) -> str:
    return {"1": "reference", "2": "bipolar"}.get(str(value), str(value).casefold())


@dataclass(frozen=True)
class AcquisitionSignal:
    name: str
    positive: str
    negatives: tuple[str, ...]
    kind: str


@dataclass(frozen=True)
class AcquisitionSignalView:
    signals: tuple[AcquisitionSignal, ...]
    provenance: dict


def read_acquisition_signal_view(directory: str | Path) -> AcquisitionSignalView:
    try:
        manifest = json.loads((Path(directory) / "manifest.json").read_text(encoding="utf-8"))
        hardware = _field(manifest, "HardwareConfiguration", {})
        montage = json.loads(hardware["montage_configuration_snapshot_json"])
        recorded = json.loads(hardware["channel_configuration_snapshot_json"])
        bound = _field(montage, "ChannelConfigurationSnapshot")
        rows = _field(montage, "DerivedChannels")
        if not bound or not rows or not _field(recorded, "Channels"):
            raise ValueError("snapshot is empty")
        if (not _field(recorded, "DeviceSignature")
            or _field(montage, "DeviceSignature") != _field(recorded, "DeviceSignature")
            or _field(bound, "DeviceSignature") != _field(recorded, "DeviceSignature")):
            raise ValueError("device layout mismatch")
        for key in ("HardwareReferenceElectrodeLocation", "HardwareGroundElectrodeLocation"):
            if not _field(recorded, key) or _field(bound, key) != _field(recorded, key):
                raise ValueError("hardware reference/ground mismatch")
        bound_channels = _field(bound, "Channels")
        manifest_channels = _field(manifest, "Channels")
        average = tuple(str(label).strip() for label in _field(montage, "AverageReferenceLabels", []) or [])
        signals = []
        sources = {}
        names, orders = set(), set()
        for row in sorted(rows, key=lambda item: int(_field(item, "DisplayOrder"))):
            name = str(_field(row, "Name", "")).strip()
            positive = str(_field(row, "PositiveLabel", "")).strip()
            negatives = tuple(str(label).strip() for label in _field(row, "NegativeLabels", []))
            kind = _kind(_field(row, "NegativeKind"))
            order = int(_field(row, "DisplayOrder"))
            if not name or name.casefold() in names or order in orders:
                raise ValueError("duplicate output name/order")
            names.add(name.casefold())
            orders.add(order)
            required_count = {"originalhardwarereference": 0, "channel": 1, "specifiedpair": 2}
            if kind not in {*required_count, "mean"}:
                raise ValueError("unsupported reference kind")
            if kind == "mean":
                if len(negatives) < 2 or {x.casefold() for x in negatives} != {x.casefold() for x in average}:
                    raise ValueError("mean reference group mismatch")
            elif len(negatives) != required_count[kind]:
                raise ValueError("invalid reference dependency count")
            suffix = {"originalhardwarereference": "REF", "channel": negatives[0] if negatives else "", "mean": "AVG", "specifiedpair": "PAIR"}[kind]
            if not positive or name.casefold() != f"{positive}-{suffix}".casefold():
                raise ValueError("output name does not describe its source formula")
            if len({x.casefold() for x in negatives}) != len(negatives):
                raise ValueError("duplicate reference source")
            if kind == "channel" and positive.casefold() == negatives[0].casefold():
                raise ValueError("positive and negative sources are identical")
            for label in (positive, *negatives):
                candidates = [ch for ch in bound_channels if str(_field(ch, "ElectrodeLabel", "")).strip().casefold() == label.casefold()]
                actual = [ch for ch in manifest_channels if str(_field(ch, "Label", "")).strip().casefold() == label.casefold()]
                if len(candidates) != 1 or len(actual) != 1:
                    raise ValueError(f"source {label}: missing/ambiguous mapping")
                target, source = candidates[0], actual[0]
                native = int(_field(target, "NativeChannelIndex"))
                history = [ch for ch in _field(recorded, "Channels") if int(_field(ch, "NativeChannelIndex")) == native]
                if len(history) != 1:
                    raise ValueError(f"source {label}: missing/ambiguous historical input")
                historical = history[0]
                expected_kind = _channel_kind(_field(target, "ExpectedKind"))
                if (expected_kind not in {"reference", "bipolar"}
                    or _channel_kind(_field(historical, "ExpectedKind")) != expected_kind
                    or str(_field(historical, "ElectrodeLabel")).strip().casefold() != label.casefold()
                    or int(_field(source, "NativeChannelIndex")) != native
                    or _channel_kind(_field(source, "Kind")) != expected_kind
                    or _field(source, "Unit") != "V"):
                    raise ValueError(f"source {label}: historical/native/type/unit mismatch")
                sources[label] = {"native_channel_index": native, "stream_index": int(_field(source, "StreamIndex")), "unit": "V", "kind": expected_kind}
            signals.append(AcquisitionSignal(name, positive, negatives, kind))
        canonical = json.dumps({"montage": montage, "channel_snapshot": recorded}, sort_keys=True, ensure_ascii=False)
        return AcquisitionSignalView(tuple(signals), {
            "signal_mode": "acquisition_montage", "projection_version": "acquisition-montage-v1",
            "analysis_reference": "acquisition_montage_snapshot",
            "snapshot_sha256": hashlib.sha256(canonical.encode()).hexdigest(),
            "montage_id": _field(montage, "Id"), "source_mapping": sources,
            "hardware_reference": _field(recorded, "HardwareReferenceElectrodeLocation"),
            "hardware_ground": _field(recorded, "HardwareGroundElectrodeLocation"),
            "formulas": [{"name": s.name, "positive": s.positive, "negative_kind": s.kind, "negative_labels": list(s.negatives)} for s in signals],
        })
    except (OSError, KeyError, TypeError, ValueError, AttributeError) as exc:
        raise WpfRecordingError("ACQUISITION_MONTAGE_UNVERIFIABLE", f"采集导联快照无法验证，不能用于分析：{exc}") from exc


class AcquisitionMontageSampleReader:
    """Project bounded V blocks before scientific preprocessing; NaN gaps survive."""

    def __init__(self, raw, view: AcquisitionSignalView):
        self.raw, self.view = raw, view
        self.sfreq, self.sample_count = raw.sfreq, raw.sample_count
        self.names = tuple(signal.name for signal in view.signals)
        self._lookup = {name.casefold(): index for index, name in enumerate(raw.names)}

    def read(self, start: int, stop: int, channels: list[int] | None = None) -> np.ndarray:
        selected = list(range(len(self.names))) if channels is None else channels
        signals = [self.view.signals[index] for index in selected]
        labels = list(dict.fromkeys(label.casefold() for signal in signals for label in (signal.positive, *signal.negatives)))
        source = self.raw.read(start, stop, [self._lookup[label] for label in labels])
        indexes = {label: index for index, label in enumerate(labels)}
        output = np.empty((stop - start, len(signals)), dtype=np.float64)
        for index, signal in enumerate(signals):
            positive = source[:, indexes[signal.positive.casefold()]]
            if signal.negatives:
                reference = source[:, [indexes[label.casefold()] for label in signal.negatives]].mean(axis=1)
                output[:, index] = positive - reference
            else:
                output[:, index] = positive
        return output

    def close(self) -> None:
        self.raw.close()
