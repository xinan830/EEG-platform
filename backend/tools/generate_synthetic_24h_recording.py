"""Generate a deterministic 24-hour WPF-compatible synthetic EEG recording.

This is test data only. It is not a physiological recording and must not be
used for clinical or scientific conclusions.
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
SAMPLE_RATE_HZ = 250
DURATION_S = 24 * 60 * 60
SAMPLE_COUNT = SAMPLE_RATE_HZ * DURATION_S
CHANNEL_COUNT = 3
CHUNK_SAMPLES = 250_000


def utc_ticks(value: datetime) -> int:
    """Convert a UTC datetime to .NET DateTime ticks."""
    epoch = datetime(1, 1, 1, tzinfo=timezone.utc)
    return int((value - epoch).total_seconds() * 10_000_000)


def build_manifest(session_id: str, start: datetime) -> dict[str, object]:
    snapshot = {
        "Id": "synthetic-24h-channel-config",
        "Name": "Synthetic 24h EEG channels",
        "Source": 0,
        "DeviceSignature": "synthetic-24h-250hz",
        "HardwareReferenceElectrodeLocation": "REF",
        "HardwareGroundElectrodeLocation": "GND",
        "Channels": [
            {"NativeChannelIndex": 0, "ExpectedKind": 1, "ElectrodeLabel": "F3", "IsSelectedForDisplay": True, "DisplayOrder": 0},
            {"NativeChannelIndex": 1, "ExpectedKind": 1, "ElectrodeLabel": "F7", "IsSelectedForDisplay": True, "DisplayOrder": 1},
        ],
    }
    montage = {
        "Id": "synthetic-24h-montage",
        "Name": "Synthetic F3/F7 REF",
        "Source": 0,
        "DeviceSignature": "synthetic-24h-250hz",
        "ChannelConfigurationFingerprint": "synthetic-24h-f3-f7",
        "ChannelConfigurationSnapshot": snapshot,
        "DerivedChannels": [
            {"Name": "F3-REF", "PositiveLabel": "F3", "NegativeKind": 0, "NegativeLabels": [], "DisplayOrder": 0},
            {"Name": "F7-REF", "PositiveLabel": "F7", "NegativeKind": 0, "NegativeLabels": [], "DisplayOrder": 1},
        ],
        "AverageReferenceLabels": ["F3", "F7"],
    }
    return {
        "SessionId": session_id,
        "PayloadFormat": "sample_major_float64_v1_with_receive_utc_ticks_and_per_channel_units",
        "EegSignalUnit": "V",
        "DeviceId": "synthetic-24h-250hz",
        "DeviceName": "Synthetic 24h EEG",
        "SamplingRateHz": SAMPLE_RATE_HZ,
        "SampleCounterChannelIndex": 2,
        "RecordingStartUtc": start.isoformat().replace("+00:00", "Z"),
        "Project": {
            "Id": "synthetic-301-project",
            "Number": "SYNTHETIC-24H",
            "Name": "Synthetic 24h EEG test data",
            "DirectoryPath": r"D:\迅雷下载\301项目",
            "SnapshotJson": json.dumps({"Id": "synthetic-301-project", "Name": "Synthetic 24h EEG test data"}, separators=(",", ":")),
        },
        "HardwareConfiguration": {
            "vendor": "synthetic",
            "model": "synthetic-24h",
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
            {"StreamIndex": 0, "NativeChannelIndex": 0, "Label": "F3", "Kind": 1, "Unit": "V"},
            {"StreamIndex": 1, "NativeChannelIndex": 1, "Label": "F7", "Kind": 1, "Unit": "V"},
            {"StreamIndex": 2, "NativeChannelIndex": 2, "Label": "Counter", "Kind": 4, "Unit": "count"},
        ],
    }


def write_recording(destination: Path) -> None:
    if destination.exists():
        raise FileExistsError(f"Refusing to overwrite existing directory: {destination}")
    destination.mkdir(parents=True)
    session_id = "8c7d2e44-7c6c-4b4e-9c8a-24a250000001"
    start = datetime(2026, 10, 18, 0, 0, 0, tzinfo=timezone.utc)
    manifest = build_manifest(session_id, start)
    (destination / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")

    phase = 2.0 * math.pi * np.arange(CHUNK_SAMPLES, dtype=np.float64) / SAMPLE_RATE_HZ
    with (destination / "samples-000001.bin").open("wb") as stream:
        for first in range(0, SAMPLE_COUNT, CHUNK_SAMPLES):
            count = min(CHUNK_SAMPLES, SAMPLE_COUNT - first)
            t = (first + np.arange(count, dtype=np.float64)) / SAMPLE_RATE_HZ
            local_phase = phase[:count] + (2.0 * math.pi * first / SAMPLE_RATE_HZ)
            # Distinct deterministic bands make PSD and window navigation easy to verify.
            f3 = 20e-6 * np.sin(10.0 * local_phase) + 6e-6 * np.sin(6.0 * local_phase) + 1e-6 * np.sin(0.5 * local_phase)
            f7 = 14e-6 * np.sin(6.0 * local_phase + 0.4) + 5e-6 * np.sin(20.0 * local_phase) + 1e-6 * np.cos(0.25 * local_phase)
            matrix = np.column_stack((f3, f7, np.arange(first, first + count, dtype=np.float64)))
            stream.write(HEADER.pack(first, count, CHANNEL_COUNT, utc_ticks(start) + int(first * 10_000_000 / SAMPLE_RATE_HZ)))
            matrix.astype("<f8", copy=False).tofile(stream)

    summary = {
        "status": "completed",
        "effective_sample_count": SAMPLE_COUNT,
        "sampling_rate_hz": SAMPLE_RATE_HZ,
        "effective_duration_seconds": DURATION_S,
        "completed_at_utc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
    }
    (destination / "recording-summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    (destination / "audit.jsonl").write_text(
        json.dumps({"type": "completed", "sample_counter": SAMPLE_COUNT - 1, "occurred_at_utc": summary["completed_at_utc"]}) + "\n",
        encoding="utf-8",
    )
    (destination / "recording-display.json").write_text("{}\n", encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    write_recording(args.destination.expanduser().resolve())
    print(f"Created {args.destination.resolve()}")
    print(f"Samples: {SAMPLE_COUNT:,}; channels: F3, F7, Counter; duration: {DURATION_S}s")


if __name__ == "__main__":
    main()
