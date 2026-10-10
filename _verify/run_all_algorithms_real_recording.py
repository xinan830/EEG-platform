"""Read-only full-recording numerical audit of every runnable official algorithm."""

from __future__ import annotations

import argparse
import gc
import json
import platform
import subprocess
import sys
import tempfile
import time
from pathlib import Path

import numpy as np
import scipy

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "backend"))

from app.algorithms.builtin_modules import build_builtin_modules
from app.models.run import RunCreateRequest
from app.services.recordings import RecordingService
from app.services.runs import RunService

from real_recording_audit import Comparisons, audit_run
from real_recording_reference import preprocess, read_acquisition_sources, source_digests


def _config(algorithm_id: str, names: list[str], mode: str, end_s: float, window_s: float) -> dict:
    def required(name: str) -> str:
        exact = next((item for item in names if item.casefold() == name.casefold()), None)
        if exact is None:
            raise RuntimeError(f"recording has no required acquisition output {name!r}")
        return exact

    config = {
        "algorithm_id": algorithm_id, "channel": required("Fz-REF"), "mode": mode,
        "time": {"start_s": 0.0, "end_s": end_s},
        "dynamic_window_s": window_s, "refresh_step_s": 1.0,
    }
    if algorithm_id == "faa":
        config.update({"channel": required("F3-REF"), "f4_channel": required("F4-REF"),
                       "low_hz": 1.0, "high_hz": 50.0, "notch_hz": 50.0})
    elif algorithm_id == "brainbeat":
        config["secondary_channel"] = required("Pz-REF")
    elif algorithm_id == "peak_frequency":
        config.update({"low_hz": 8.0, "high_hz": 13.0})
    elif algorithm_id == "band_ratio":
        config.update({"numerator_low_hz": 4.0, "numerator_high_hz": 8.0,
                       "denominator_low_hz": 13.0, "denominator_high_hz": 30.0})
    elif algorithm_id in {"psd", "stft"}:
        config.update({"low_hz": 1.0, "high_hz": 50.0, "notch_hz": 50.0})
    return config


def _source_checks(recordings, recording, expected, sfreq, names):
    reader = recordings.open_sample_reader(recording)
    comparisons = Comparisons()
    try:
        assert list(reader.names) == names and list(recording.channels) == names
        assert reader.sfreq == sfreq and reader.sample_count == len(expected)
        for start in range(0, len(expected), 10000):
            stop = min(start + 10000, len(expected))
            actual = reader.read(start, stop)
            np.testing.assert_array_equal(actual, expected[start:stop])
            comparisons.check("acquisition_projection", actual, expected[start:stop], unit="V", atol=0)
    finally:
        reader.close()
    return comparisons.outputs


def _run_case(module, config, independent, sfreq, recording, runs):
    started = time.perf_counter()
    policy = module.manifest.dynamic_policy
    try:
        result = runs.create(RunCreateRequest(recording_id=recording.id,
                                             analysis_type="official_algorithm", config=config))
        entry = audit_run(result, runs, config, independent, sfreq,
                          policy.minimum_window_s, policy.allow_warmup)
    except Exception as exc:
        entry = {"validation": "FAIL", "error_type": type(exc).__name__, "error": str(exc), "config": config}
    entry["elapsed_s"] = round(time.perf_counter() - started, 3)
    return entry


def run(directory: str, end_s: float | None = None, all_static_channels: bool = False) -> dict:
    source_directory = Path(directory).resolve()
    before = source_digests(source_directory)
    projected, sfreq, names = read_acquisition_sources(source_directory)
    duration = len(projected) / sfreq
    selected_end = duration if end_s is None else min(end_s, duration)
    version = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=Path(__file__).resolve().parents[1], text=True).strip()
    report = {
        "scope": "engineering numerical regression, not clinical validation or WPF visual acceptance",
        "source_file_sha256": before, "duration_s": duration, "sfreq_hz": sfreq,
        "sample_count": len(projected), "analysis_outputs": names,
        "tested_range_s": [0.0, selected_end], "results": {},
        "all_single_channel_static_outputs_tested": all_static_channels,
        "environment": {"python": platform.python_version(), "numpy": np.__version__,
                        "scipy": scipy.__version__, "git_head": version, "worktree": "includes existing uncommitted changes"},
    }
    started = time.perf_counter()
    with tempfile.TemporaryDirectory(prefix="all-algorithms-") as temporary:
        root = Path(temporary)
        recordings = RecordingService(root / "stored", root / "catalog.sqlite3")
        recording = recordings.register_wpf_recording(source_directory, signal_mode="acquisition_montage")
        runs = RunService(recordings, recordings.database_path, root / "artifacts")
        try:
            report["raw_channel_count"] = len(recording.raw_channel_labels)
            source = recordings.analysis_signal_provenance(recording)
            report["source_snapshot_sha256"] = source["snapshot_sha256"]
            report["projection_comparisons"] = _source_checks(recordings, recording, projected, sfreq, names)
            filtered = {
                "default": preprocess(projected, sfreq, 1.0, 30.0, None),
                "wide_notched": preprocess(projected, sfreq, 1.0, 50.0, 50.0),
            }
            for module in build_builtin_modules():
                algorithm = module.manifest.algorithm_id
                policy = module.manifest.dynamic_policy
                for mode in module.manifest.supported_modes:
                    candidates = names if all_static_channels and mode == "static" and algorithm not in {"faa", "brainbeat"} else [None]
                    for channel in candidates:
                        config = _config(algorithm, names, mode, selected_end, policy.default_window_s)
                        if channel is not None:
                            config["channel"] = channel
                        selected = [names.index(config["channel"])]
                        if algorithm == "faa":
                            selected.append(names.index(config["f4_channel"]))
                        elif algorithm == "brainbeat":
                            selected.append(names.index(config["secondary_channel"]))
                        independent = filtered["wide_notched" if algorithm in {"psd", "rbp", "stft", "faa"} else "default"][:, selected]
                        label = f"{algorithm}/{mode}" + (f"/{channel}" if channel is not None else "")
                        entry = _run_case(module, config, independent, sfreq, recording, runs)
                        report["results"][label] = entry
                        print(f"{label}: {entry['validation']} ({entry['elapsed_s']:.3f}s)", file=sys.stderr, flush=True)
            report["all_passed"] = all(entry["validation"] == "PASS" for entry in report["results"].values())
        finally:
            recordings._analysis_preprocess_cache.clear_recording(recording.id)
            del runs, recordings
            gc.collect()
    report["raw_files_unchanged"] = before == source_digests(source_directory)
    assert report["raw_files_unchanged"], "raw recording files changed during audit"
    report["elapsed_s"] = round(time.perf_counter() - started, 3)
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory")
    parser.add_argument("--end-s", type=float, help="Optional limited smoke-test range; default is the whole record")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--all-static-channels", action="store_true", help="Also audit each single-channel algorithm on every acquisition output")
    args = parser.parse_args()
    report = run(args.directory, args.end_s, args.all_static_channels)
    payload = json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False)
    if args.output is not None:
        args.output.write_text(payload + "\n", encoding="utf-8")
        print(f"Report: {args.output.resolve()}", file=sys.stderr)
    else:
        print(payload)
    sys.exit(0 if report["all_passed"] else 1)
