"""Run all official dynamic Runs against controlled one-hour WPF recordings.

The inputs are deterministic synthetic EEG fixtures, not real patient data.
Each algorithm gets its required channels and an isolated disposable database.
"""

from __future__ import annotations

import argparse
import gc
import hashlib
import json
import platform
import sys
import tempfile
import threading
from pathlib import Path
from time import perf_counter

import numpy as np
import scipy

from app.models.run import RunCreateRequest, RunStatus
from app.services.recordings import RecordingService
from app.services.run_queue import PersistentRunQueue
from validate_run_execution import working_set_bytes
from tests.on_demand_fixtures import write_recording


ALGORITHMS = (
    "band_ratio", "brainbeat", "faa", "iapf", "peak_frequency",
    "psd", "rbp", "stft", "theta_beta",
)


def request(recording, algorithm: str, duration_s: float) -> RunCreateRequest:
    channels = {"brainbeat": ("Fz-REF", "Pz-REF")}.get(algorithm, ("F3-REF", "F4-REF"))
    config: dict[str, object] = {
        "algorithm_id": algorithm,
        "channel": channels[0],
        "mode": "dynamic",
        "time": {"start_s": 0.0, "end_s": duration_s},
        "dynamic_window_s": 20.0 if algorithm == "faa" else 5.0,
        "refresh_step_s": 1.0,
    }
    if algorithm in {"psd", "stft", "faa"}:
        config.update(low_hz=1.0, high_hz=50.0, notch_hz=50.0)
    if algorithm == "faa":
        config["f4_channel"] = channels[1]
    if algorithm == "brainbeat":
        config["secondary_channel"] = channels[1]
    if algorithm == "band_ratio":
        config.update(numerator_low_hz=4.0, numerator_high_hz=8.0,
                      denominator_low_hz=13.0, denominator_high_hz=30.0)
    return RunCreateRequest(recording_id=recording.id,
                            analysis_type="official_algorithm", config=config)


def run_algorithm(root: Path, algorithm: str, duration_s: float) -> dict[str, object]:
    labels = ["Fz", "Pz"] if algorithm == "brainbeat" else ["F3", "F4"]
    source = root / f"source-{algorithm}"
    write_recording(source, duration_s=duration_s, labels=labels)
    source_hash = hashlib.sha256((source / "samples-000001.bin").read_bytes()).hexdigest()
    recordings = RecordingService(root / f"stored-{algorithm}", root / f"{algorithm}.sqlite3")
    recording = recordings.register_wpf_recording(str(source), signal_mode="acquisition_montage")
    queue = PersistentRunQueue(recordings, recordings.database_path, root / f"artifacts-{algorithm}")
    run = queue.enqueue(request(recording, algorithm, duration_s))
    progress_times: list[float] = []
    previous = [perf_counter()]
    original_update = queue.repository.update_progress

    def timed_update(*args, **kwargs):
        value = original_update(*args, **kwargs)
        if value.progress_completed > (len(progress_times)):
            current = perf_counter()
            progress_times.append((current - previous[0]) * 1000.0)
            previous[0] = current
        return value

    queue.repository.update_progress = timed_update
    started = perf_counter()
    try:
        final = queue.process_next()
    finally:
        elapsed_s = perf_counter() - started
        queue.repository.update_progress = original_update

    if final is None or final.run_id != run.run_id or final.status is not RunStatus.COMPLETED:
        raise RuntimeError(f"{algorithm} hour-scale Run failed: {None if final is None else final.error}")
    if final.progress_completed != final.progress_total:
        raise RuntimeError(f"{algorithm} did not complete all planned windows")
    artifacts = queue.list_artifacts(final.run_id)
    if len(artifacts) != 1:
        raise RuntimeError(f"{algorithm} artifact count mismatch: {len(artifacts)}")
    queue.base.artifacts.verify(artifacts[0])
    states = (final.result_summary or {}).get("metric", {}).get("window_state_counts", {})
    artifact_path = queue.base.artifacts.root / artifacts[0].relative_path
    result = {
        "algorithm": algorithm,
        "scientific_version": final.scientific_version,
        "duration_s": recording.duration_s,
        "sampling_rate_hz": recording.sfreq,
        "channels": len(recording.channels),
        "status": final.status.value,
        "processed_windows": final.progress_completed,
        "elapsed_s": round(elapsed_s, 3),
        "windows_per_second": round(final.progress_completed / elapsed_s, 3),
        "window_interval_ms": {
            "p50": round(float(np.percentile(progress_times, 50)), 3),
            "p95": round(float(np.percentile(progress_times, 95)), 3),
            "max": round(max(progress_times), 3),
            "count": len(progress_times),
        },
        "window_states": states,
        "artifact_bytes": artifact_path.stat().st_size,
        "artifact_sha256": artifacts[0].sha256,
        "source_sha256": source_hash,
    }
    queue.compute_coordinator.close()
    recordings._analysis_preprocess_cache.clear_recording(recording.id)
    del queue, recordings, recording, final
    gc.collect()
    return result


def validate(duration_s: float) -> dict[str, object]:
    if duration_s < 3600:
        raise ValueError("hour-scale duration must be at least 3600 seconds")
    memory = {"baseline_working_set_bytes": working_set_bytes(), "peak_working_set_bytes": 0}
    stop = threading.Event()

    def sample_memory() -> None:
        while not stop.wait(0.05):
            memory["peak_working_set_bytes"] = max(memory["peak_working_set_bytes"], working_set_bytes())

    sampler = threading.Thread(target=sample_memory, daemon=True)
    sampler.start()
    memory["peak_working_set_bytes"] = memory["baseline_working_set_bytes"]
    results = []
    try:
        with tempfile.TemporaryDirectory(prefix="brain-hour-scale-", ignore_cleanup_errors=True) as temporary:
            root = Path(temporary)
            for algorithm in ALGORITHMS:
                result = run_algorithm(root, algorithm, duration_s)
                results.append(result)
                print(json.dumps(result, ensure_ascii=False), flush=True)
    finally:
        stop.set()
        sampler.join()
    memory["peak_working_set_bytes"] = max(memory["peak_working_set_bytes"], working_set_bytes())
    memory["peak_delta_bytes"] = memory["peak_working_set_bytes"] - memory["baseline_working_set_bytes"]
    return {
        "schema": "official-algorithm-hour-scale-v1",
        "input_kind": "deterministic_synthetic_wpf_recording",
        "environment": {"python": sys.version.split()[0], "platform": platform.platform(),
                        "numpy": np.__version__, "scipy": scipy.__version__},
        "duration_s": duration_s,
        "sampling_rate_hz": 200,
        "algorithm_count": len(results),
        "memory": memory,
        "runs": results,
        "limitations": [
            "Synthetic controlled signal only; this does not establish behavior on hour-scale real EEG.",
            "Per-window interval includes queue scheduling and persistence overhead.",
            "Cancellation and preview/full reuse are covered by the separate real-recording report.",
        ],
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--duration-s", type=float, default=3600.0)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    report = validate(args.duration_s)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
