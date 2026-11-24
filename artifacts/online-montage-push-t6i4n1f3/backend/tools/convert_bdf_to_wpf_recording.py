"""Convert a BDF file into the immutable local WPF recording format.

The BDF remains untouched. MNE returns EEG samples in volts; the generated
payload stores sample-major little-endian float64 values and appends the
required monotonic sample-counter stream.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import struct
from datetime import datetime, timezone
from pathlib import Path
from uuid import uuid4

import mne
import numpy as np


HEADER = struct.Struct("<qiiq")
CHANNEL_KIND_EEG = 1
CHANNEL_KIND_COUNTER = 4
CHUNK_SAMPLES = 200_000
ANT_NATIVE_INDEX_BY_LABEL = {
    label: index for index, label in enumerate(
        ["Fp1", "Fp2", "F7", "F3", "Fz", "F4", "F8", "T3", "C3", "Cz",
         "C4", "T4", "M1", "T5", "P3", "Pz", "P4", "T6", "M2", "O1", "", "O2", "", ""]
    ) if label
}
ANT_DEVICE_SIGNATURE = "c18b55312e23a05fd80b699db434c377fac8bc4579195abd3696fe772e6a8f10"


def utc_ticks(value: datetime) -> int:
    epoch = datetime(1, 1, 1, tzinfo=timezone.utc)
    return int((value - epoch).total_seconds() * 10_000_000)


def _channel_fingerprint(snapshot: dict[str, object]) -> str:
    canonical = f"{snapshot['DeviceSignature'].strip()}|{snapshot['HardwareReferenceElectrodeLocation'].strip()}|{snapshot['HardwareGroundElectrodeLocation'].strip()}"
    for channel in sorted(snapshot["Channels"], key=lambda item: item["NativeChannelIndex"]):
        kind = {1: "Reference", 2: "Bipolar"}[channel["ExpectedKind"]]
        canonical += f"|{channel['NativeChannelIndex']}:{kind}:{channel['ElectrodeLabel'].strip().upper()}:{'1' if channel['IsSelectedForDisplay'] else '0'}:{channel['DisplayOrder']}"
    return hashlib.sha256(canonical.encode()).hexdigest()


def _montage_fingerprint(channel_fingerprint: str, derived: list[dict[str, object]]) -> str:
    canonical = channel_fingerprint
    kinds = {0: "OriginalHardwareReference", 1: "Channel", 2: "Mean", 3: "SpecifiedPair"}
    for channel in sorted(derived, key=lambda item: item["DisplayOrder"]):
        canonical += f"|{channel['DisplayOrder']}:{channel['Name'].strip().upper()}:{channel['PositiveLabel'].strip().upper()}:{kinds[channel['NegativeKind']]}:{','.join(label.strip().upper() for label in channel['NegativeLabels'])}"
    canonical += "|AVG:"
    return hashlib.sha256(canonical.encode()).hexdigest()


def build_manifest(session_id: str, start: datetime, sfreq: int, labels: list[str]) -> dict[str, object]:
    channels = [
        {"StreamIndex": index, "NativeChannelIndex": ANT_NATIVE_INDEX_BY_LABEL.get(label, index), "Label": label,
         "Kind": CHANNEL_KIND_EEG, "Unit": "V"}
        for index, label in enumerate(labels)
    ]
    counter_index = len(labels)
    channels.append({
        "StreamIndex": counter_index, "NativeChannelIndex": counter_index,
        "Label": "Counter", "Kind": CHANNEL_KIND_COUNTER, "Unit": "count",
    })
    native_labels = {ANT_NATIVE_INDEX_BY_LABEL[label]: label for label in labels if label in ANT_NATIVE_INDEX_BY_LABEL}
    channel_snapshot = {
        "Id": "system-ant-standard-28-input", "Name": "ANT默认通道配置",
        "Description": "系统默认；ANT/eego 的 24 Reference + 4 Bipolar 物理输入布局。",
        "Source": 0, "DeviceSignature": ANT_DEVICE_SIGNATURE,
        "Channels": [
            {"NativeChannelIndex": index, "ExpectedKind": 1 if index < 24 else 2,
             "ElectrodeLabel": native_labels.get(index, ""),
             "IsSelectedForDisplay": index in native_labels, "DisplayOrder": index}
            for index in range(28)
        ],
        "CreatedAtUtc": "1970-01-01T00:00:00+00:00", "UpdatedAtUtc": "1970-01-01T00:00:00+00:00",
        "DeviceDisplayName": "ANT/eego", "DeviceDriverId": "ant-eego", "DeviceModel": "",
        "HardwareReferenceElectrodeLocation": "REF", "HardwareGroundElectrodeLocation": "GND",
    }
    channel_fingerprint = _channel_fingerprint(channel_snapshot)
    derived = [{"Name": f"{label}-REF", "PositiveLabel": label, "NegativeKind": 0,
                "NegativeLabels": [], "DisplayOrder": index}
               for index, label in enumerate(labels)]
    montage = {
        "Id": "system-ant-ref", "Name": "ANT默认通道配置 REF参考",
        "Description": "ANT 默认通道配置的原始 EEG 输出，使用硬件 REF，不执行软件重参考。",
        "Source": 0, "DeviceSignature": ANT_DEVICE_SIGNATURE,
        "ChannelConfigurationFingerprint": channel_fingerprint,
        "ChannelConfigurationSnapshot": channel_snapshot, "DerivedChannels": derived,
        "Revision": 1, "CreatedAtUtc": "1970-01-01T00:00:00+00:00", "UpdatedAtUtc": "1970-01-01T00:00:00+00:00",
        "Fingerprint": _montage_fingerprint(channel_fingerprint, derived), "AverageReferenceLabels": [],
    }
    hardware = {
        "source_format": "BDF", "source_name": "Imported BDF recording", "channel_count": str(len(labels)),
        "DeviceDisplayName": "ANT/eego", "DeviceDriverId": "ant-eego", "DeviceSignature": ANT_DEVICE_SIGNATURE,
        "HardwareReferenceElectrodeLocation": "REF", "HardwareGroundElectrodeLocation": "GND",
        "channel_configuration_snapshot_json": json.dumps(channel_snapshot, ensure_ascii=False, separators=(",", ":")),
        "montage_configuration_id": montage["Id"], "montage_configuration_name": montage["Name"],
        "montage_configuration_snapshot_json": json.dumps(montage, ensure_ascii=False, separators=(",", ":")),
        "montage_catalogue_snapshot_json": json.dumps([montage], ensure_ascii=False, separators=(",", ":")),
    }
    return {
        "SessionId": session_id,
        "PayloadFormat": "sample_major_float64_v1_with_receive_utc_ticks_and_per_channel_units",
        "EegSignalUnit": "V",
        "DeviceId": "bdf-import",
        "DeviceName": "Imported BDF recording",
        "SamplingRateHz": sfreq,
        "SampleCounterChannelIndex": counter_index,
        "RecordingStartUtc": start.isoformat().replace("+00:00", "Z"),
        "Channels": channels,
        "Project": {
            "Id": "bdf-import",
            "Number": "BDF-IMPORT",
            "Name": "Imported BDF recording",
            "DirectoryPath": "",
            "SnapshotJson": "{\"Id\":\"bdf-import\",\"Name\":\"Imported BDF recording\"}",
        },
        "HardwareConfiguration": hardware,
    }


def convert(source: Path, destination: Path) -> tuple[int, int]:
    if destination.exists():
        raise FileExistsError(f"Refusing to overwrite existing directory: {destination}")
    raw = mne.io.read_raw_bdf(str(source), preload=True, verbose="ERROR")
    sfreq = float(raw.info["sfreq"])
    if sfreq <= 0 or sfreq != int(sfreq):
        raise ValueError(f"BDF sampling rate must be a positive integer, got {sfreq}")
    start = raw.info.get("meas_date") or datetime.now(timezone.utc)
    if start.tzinfo is None:
        start = start.replace(tzinfo=timezone.utc)
    start = start.astimezone(timezone.utc)
    labels = [str(name) for name in raw.ch_names]
    values = np.asarray(raw.get_data(), dtype=np.float64).T
    sample_count = values.shape[0]
    channel_count = values.shape[1] + 1
    destination.mkdir(parents=True)
    session_id = str(uuid4())
    (destination / "manifest.json").write_text(
        json.dumps(build_manifest(session_id, start, int(sfreq), labels), ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    ticks = utc_ticks(start)
    with (destination / "samples-000001.bin").open("wb") as stream:
        for first in range(0, sample_count, CHUNK_SAMPLES):
            count = min(CHUNK_SAMPLES, sample_count - first)
            matrix = np.empty((count, channel_count), dtype="<f8")
            matrix[:, :-1] = values[first:first + count]
            matrix[:, -1] = np.arange(first, first + count, dtype=np.float64)
            stream.write(HEADER.pack(first, count, channel_count,
                                     ticks + int(first * 10_000_000 / sfreq)))
            matrix.tofile(stream)
    completed = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    (destination / "recording-summary.json").write_text(json.dumps({
        "status": "completed", "effective_sample_count": sample_count,
        "sampling_rate_hz": int(sfreq),
        "effective_duration_seconds": sample_count / sfreq,
        "completed_at_utc": completed,
    }, ensure_ascii=False, indent=2), encoding="utf-8")
    (destination / "audit.jsonl").write_text(json.dumps({
        "type": "completed", "sample_counter": sample_count - 1,
        "occurred_at_utc": completed,
        "source": "bdf-import",
    }, ensure_ascii=False) + "\n", encoding="utf-8")
    (destination / "recording-display.json").write_text("{}\n", encoding="utf-8")
    return sample_count, channel_count


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    samples, channels = convert(args.source.expanduser().resolve(), args.destination.expanduser().resolve())
    print(f"Created {args.destination.resolve()}")
    print(f"Samples: {samples:,}; channels: {channels - 1} EEG + Counter")


if __name__ == "__main__":
    main()
