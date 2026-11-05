"""Read-only source acceptance: export both formats, independently compare with MNE."""
import argparse
from datetime import datetime
import json
from pathlib import Path
import shutil
from threading import Event
import time

import mne
import numpy as np
import pyedflib

from app.services.recordings import RecordingService
from app.services.recording_samples import WpfSampleReader
from app.services.recording_signal_export import RecordingSignalExporter
from app.services.wpf_recording import inspect_wpf_recording


def validate(source: Path, output: Path) -> dict:
    output.mkdir(parents=True, exist_ok=True)
    facts = inspect_wpf_recording(source)
    service = RecordingService(output / "registry", output / "acceptance.sqlite3")
    recording = service.register_wpf_recording(str(source), signal_mode="raw")
    exporter = RecordingSignalExporter(output / "work")
    result = {"source_sha256": facts.source_sha256, "recording_id": recording.id, "formats": {}}
    for format in ("edf", "bdf"):
        started = time.perf_counter()
        package = exporter.export(recording, format, Event(), "local-export-acceptance")
        try:
            signal = package.directory / f"recording.{format}"
            metadata_path = signal.with_name(signal.name + ".labels.json")
            metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
            source_reader = WpfSampleReader(facts)
            indexes = [facts.eeg_indexes.index(item["stream_index"]) for item in metadata["channels"]]
            errors = np.zeros(len(indexes))
            try:
                read = mne.io.read_raw_edf if format == "edf" else mne.io.read_raw_bdf
                with read(signal, preload=False, verbose="ERROR") as raw:
                    assert raw.n_times == facts.sample_count
                    assert raw.info["sfreq"] == facts.sampling_rate_hz
                    assert raw.ch_names == [item["export_label"] for item in metadata["channels"]]
                    assert len(raw.annotations) == len(metadata["events"])
                    for offset in range(0, facts.sample_count, 65536):
                        stop = min(offset + 65536, facts.sample_count)
                        expected = source_reader.read(offset, stop, indexes) * 1e6
                        actual = raw.get_data(start=offset, stop=stop).T * 1e6
                        errors = np.maximum(errors, np.max(np.abs(actual - expected), axis=0))
                    for annotation, event in zip(raw.annotations, metadata["events"]):
                        assert abs(annotation["onset"] - event["start_sample"] / facts.sampling_rate_hz) <= .000051
                with pyedflib.EdfReader(str(signal)) as header:
                    assert header.starttime_subsecond == datetime.fromisoformat(facts.recording_start_utc).microsecond * 10
                    assert np.all(header.getNSamples() == facts.sample_count)
                    assert np.all(header.getSampleFrequencies() == facts.sampling_rate_hz)
                    steps = np.array([item["quantization_step_uv"] for item in metadata["channels"]])
                    assert np.all(errors <= steps * 1.01 + 1e-8), (errors, steps)
            finally:
                source_reader.close()
            for path in (signal, metadata_path):
                shutil.copy2(path, output / path.name)
            result["formats"][format] = {"sample_count": facts.sample_count, "channels": len(indexes),
                                         "sampling_rate_hz": facts.sampling_rate_hz, "duration_s": facts.duration_s,
                                         "bytes": signal.stat().st_size, "elapsed_s": time.perf_counter() - started,
                                         "maximum_error_uv": float(errors.max()),
                                         "maximum_error_in_quantization_steps": float((errors / steps).max()),
                                         "per_channel_maximum_error_uv": errors.tolist(), "events": len(metadata["events"])}
        finally:
            package.close()
    assert facts.source_sha256 == inspect_wpf_recording(source).source_sha256
    result["source_unchanged"] = True
    (output / "validation.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    arguments = parser.parse_args()
    print(json.dumps(validate(arguments.source, arguments.output), indent=2))
