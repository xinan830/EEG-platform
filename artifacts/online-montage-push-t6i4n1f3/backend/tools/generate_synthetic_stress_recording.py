"""Generate a large deterministic WPF-compatible recording for performance tests.

This creates synthetic data only. It is not a physiological or clinical
recording. The default profile is 72 hours at 250 Hz with 24 EEG channels.
"""

from __future__ import annotations

import argparse
import json
import math
import struct
from datetime import datetime, timezone
from pathlib import Path

import numpy as np


HEADER = struct.Struct("<qiiq")
DEFAULT_SAMPLE_RATE_HZ = 250
DEFAULT_DURATION_HOURS = 72
DEFAULT_EEG_COUNT = 24
DEFAULT_CHUNK_SAMPLES = 250_000
LABELS = (
    "Fp1", "Fp2", "F3", "F4", "C3", "C4", "P3", "P4", "O1", "O2",
    "F7", "F8", "T3", "T4", "T5", "T6", "Fz", "Cz", "Pz", "M1",
    "M2", "FC1", "FC2", "CP1",
)


def utc_ticks(value: datetime) -> int:
    epoch = datetime(1, 1, 1, tzinfo=timezone.utc)
    return int((value - epoch).total_seconds() * 10_000_000)


def build_manifest(session_id: str, start: datetime, sample_rate_hz: int, eeg_count: int) -> dict[str, object]:
    labels = LABELS[:eeg_count]
    channels = [
        {"NativeChannelIndex": index, "ExpectedKind": 1, "ElectrodeLabel": label,
         "IsSelectedForDisplay": True, "DisplayOrder": index}
        for index, label in enumerate(labels)
    ]
    snapshot = {
        "Id": "synthetic-stress-channel-config",
        "Name": f"Synthetic {eeg_count}-channel EEG",
        "Source": 0,
        "DeviceSignature": "synthetic-stress-250hz",
        "HardwareReferenceElectrodeLocation": "REF",
        "HardwareGroundElectrodeLocation": "GND",
        "Channels": channels,
    }
    montage = {
        "Id": "synthetic-stress-montage",
        "Name": f"Synthetic {eeg_count}-channel REF",
        "Source": 0,
        "DeviceSignature": "synthetic-stress-250hz",
        "ChannelConfigurationFingerprint": "synthetic-stress-f3-f7",
        "ChannelConfigurationSnapshot": snapshot,
        "DerivedChannels": [
            {"Name": f"{label}-REF", "PositiveLabel": label, "NegativeKind": 0,
             "NegativeLabels": [], "DisplayOrder": index}
            for index, label in enumerate(labels)
        ],
        "AverageReferenceLabels": list(labels),
    }
    return {
        "SessionId": session_id,
        "PayloadFormat": "sample_major_float64_v1_with_receive_utc_ticks_and_per_channel_units",
        "EegSignalUnit": "V",
        "DeviceId": "synthetic-stress-250hz",
        "DeviceName": "Synthetic stress recording",
        "SamplingRateHz": sample_rate_hz,
        "SampleCounterChannelIndex": eeg_count,
        "RecordingStartUtc": start.isoformat().replace("+00:00", "Z"),
        "Project": {
            "Id": "synthetic-stress-project",
            "Number": "SYNTHETIC-STRESS",
            "Name": "Synthetic stress test data",
            "DirectoryPath": r"D:\迅雷下载\301项目",
            "SnapshotJson": json.dumps({"Id": "synthetic-stress-project", "Name": "Synthetic stress test data"}, separators=(",", ":")),
        },
        "HardwareConfiguration": {
            "vendor": "synthetic",
            "model": "synthetic-stress",
            "hardware_reference_role": "REF",
            "hardware_ground_role": "GND",
            "hardware_reference_electrode_location": "REF",
            "hardware_ground_electrode_location": "GND",
            "channel_configuration_id": snapshot["Id"],
            "channel_configuration_name": snapshot["Name"],
            "channel_configuration_snapshot_json": json.dumps(snapshot, separators=(",", ":")),
            "montage_configuration_id": montage["Id"],
            "montage_configuration_name": montage["Name"],
            "montage_configuration_snapshot_json": json.dumps(montage, separators=(",", ":")),
        },
        "Channels": [
            {"StreamIndex": index, "NativeChannelIndex": index, "Label": label, "Kind": 1, "Unit": "V"}
            for index, label in enumerate(labels)
        ] + [{"StreamIndex": eeg_count, "NativeChannelIndex": eeg_count, "Label": "Counter", "Kind": 4, "Unit": "count"}],
    }


def write_recording(destination: Path, *, duration_hours: int = DEFAULT_DURATION_HOURS,
                    sample_rate_hz: int = DEFAULT_SAMPLE_RATE_HZ,
                    eeg_count: int = DEFAULT_EEG_COUNT,
                    chunk_samples: int = DEFAULT_CHUNK_SAMPLES) -> tuple[int, int]:
    if destination.exists():
        raise FileExistsError(f"Refusing to overwrite existing directory: {destination}")
    if eeg_count < 1 or eeg_count > len(LABELS):
        raise ValueError(f"eeg_count must be between 1 and {len(LABELS)}")
    if duration_hours < 1 or sample_rate_hz < 1 or chunk_samples < 1:
        raise ValueError("duration, sample rate and chunk size must be positive")
    destination.mkdir(parents=True)
    sample_count = sample_rate_hz * duration_hours * 60 * 60
    channel_count = eeg_count + 1
    session_id = "8c7d2e44-7c6c-4b4e-9c8a-72a250000001"
    start = datetime(2026, 10, 19, 0, 0, 0, tzinfo=timezone.utc)
    manifest = build_manifest(session_id, start, sample_rate_hz, eeg_count)
    (destination / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")

    base = np.arange(chunk_samples, dtype=np.float64) / sample_rate_hz
    with (destination / "samples-000001.bin").open("wb") as stream:
        for first in range(0, sample_count, chunk_samples):
            count = min(chunk_samples, sample_count - first)
            t = (first / sample_rate_hz) + base[:count]
            matrix = np.empty((count, channel_count), dtype=np.float64)
            for index in range(eeg_count):
                frequency = 4.0 + float(index % 20)
                matrix[:, index] = (
                    (12e-6 + index * 0.15e-6) * np.sin(2.0 * math.pi * frequency * t)
                    + 2e-6 * np.sin(2.0 * math.pi * (frequency + 1.0) * t + index * 0.17)
                )
            matrix[:, eeg_count] = np.arange(first, first + count, dtype=np.float64)
            stream.write(HEADER.pack(first, count, channel_count,
                                     utc_ticks(start) + int(first * 10_000_000 / sample_rate_hz)))
            matrix.tofile(stream)

    completed = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    (destination / "recording-summary.json").write_text(json.dumps({
        "status": "completed", "effective_sample_count": sample_count,
        "sampling_rate_hz": sample_rate_hz,
        "effective_duration_seconds": duration_hours * 60 * 60,
        "completed_at_utc": completed,
    }, indent=2), encoding="utf-8")
    (destination / "audit.jsonl").write_text(json.dumps({
        "type": "completed", "sample_counter": sample_count - 1, "occurred_at_utc": completed,
    }) + "\n", encoding="utf-8")
    (destination / "recording-display.json").write_text("{}\n", encoding="utf-8")
    return sample_count, channel_count


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("destination", type=Path)
    parser.add_argument("--duration-hours", type=int, default=DEFAULT_DURATION_HOURS)
    parser.add_argument("--sample-rate-hz", type=int, default=DEFAULT_SAMPLE_RATE_HZ)
    parser.add_argument("--eeg-count", type=int, default=DEFAULT_EEG_COUNT)
    args = parser.parse_args()
    sample_count, channel_count = write_recording(
        args.destination.expanduser().resolve(), duration_hours=args.duration_hours,
        sample_rate_hz=args.sample_rate_hz, eeg_count=args.eeg_count,
    )
    print(f"Created {args.destination.resolve()}")
    print(f"Samples: {sample_count:,}; channels: {channel_count - 1} EEG + Counter; duration: {args.duration_hours}h")


if __name__ == "__main__":
    main()
