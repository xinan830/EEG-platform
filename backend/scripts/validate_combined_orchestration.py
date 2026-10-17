"""Measure one shared coordinator under concurrent full and preview work."""

from __future__ import annotations

import argparse
import gc
import json
import sys
import tempfile
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import psutil

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tests"))

from on_demand_fixtures import psd_config, write_recording
from app.algorithm_runtime.on_demand import DynamicWindowRequest
from app.models.run import RunCreateRequest
from app.services.scientific_compute_coordinator import ScientificComputeCoordinator
from app.services.run_queue import PersistentRunQueue
from app.services.on_demand_dynamic import DynamicWindowService


PENDING = {"queued", "preparing_source", "computing_window"}


def wait_window(service: DynamicWindowService, window_id: str, timeout_s: float = 180.0) -> dict:
    deadline = time.monotonic() + timeout_s
    while time.monotonic() < deadline:
        value = service.get(window_id)
        if value["state"] not in PENDING:
            return value
        time.sleep(0.005)
    raise TimeoutError(f"window {window_id} did not finish")


def run(recording_path: Path, duration_s: float) -> dict:
    process = psutil.Process()
    with tempfile.TemporaryDirectory(prefix="brain-combined-validation-") as scratch_name:
        scratch = Path(scratch_name)
        from app.services.recordings import RecordingService
        from app.bootstrap import build_builtin_registry

        recordings = RecordingService(scratch / "combined-recordings", scratch / "combined.sqlite3")
        if recording_path:
            recording = recordings.register_wpf_recording(str(recording_path), signal_mode="acquisition_montage")
        else:
            root = scratch / "fixture-recording"
            write_recording(root, duration_s=duration_s)
            recording = recordings.register_wpf_recording(str(root), signal_mode="acquisition_montage")
        coordinator = ScientificComputeCoordinator(max_active=1, interactive_burst_limit=4, wait_poll_s=0.005)
        runs = PersistentRunQueue(recordings, recordings.database_path, scratch / "combined-artifacts",
                                  compute_coordinator=coordinator)
        windows = DynamicWindowService(recordings, build_builtin_registry(), recordings.database_path,
                                       scratch / "combined-artifacts", compute_coordinator=coordinator)
        config = psd_config(recording)
        queued = runs.enqueue(RunCreateRequest(recording_id=recording.id, analysis_type="official_algorithm", config=config))
        rss_peak = process.memory_info().rss
        preview_latencies: list[float] = []
        preview_states: dict[str, int] = {}
        full_holder: list[object] = []

        def full_worker() -> None:
            full_holder.append(runs.process_next())

        try:
            with ThreadPoolExecutor(max_workers=1) as pool:
                future = pool.submit(full_worker)
                for index, cursor_s in enumerate(range(5, min(int(recording.duration_s) - 1, 35))):
                    started = time.perf_counter()
                    response = windows.ensure(DynamicWindowRequest(
                        recording_id=recording.id, algorithm_id="psd", config=config,
                        cursor_sample=int(cursor_s * recording.sfreq), request_id=f"pressure-{index}",
                    ))
                    result = wait_window(windows, response["window_id"])
                    preview_latencies.append((time.perf_counter() - started) * 1000.0)
                    preview_states[result["state"]] = preview_states.get(result["state"], 0) + 1
                    windows.cancel_subscription(response["subscription_id"])
                    rss_peak = max(rss_peak, process.memory_info().rss)
                full = future.result(timeout=300)
            if full is None:
                full = runs.get(queued.run_id)
            if full is None:
                raise RuntimeError("full run disappeared before pressure measurement completed")
            metrics = coordinator.snapshot()
            return {
                "recording_duration_s": recording.duration_s,
                "queued_run_id": queued.run_id,
                "full_status": full.status.value,
                "full_progress": [full.progress_completed, full.progress_total],
                "preview_count": len(preview_latencies),
                "preview_p50_ms": _percentile(preview_latencies, 50),
                "preview_p95_ms": _percentile(preview_latencies, 95),
                "preview_max_ms": max(preview_latencies, default=None),
                "preview_states": preview_states,
                "rss_peak_bytes": rss_peak,
                "coordinator": metrics,
            }
        finally:
            windows.close(wait=True)
            coordinator.close()
            del runs, windows, recordings
            # SQLite connection contexts may still await GC on Windows; the
            # existing on-demand diagnostic script needs the same cleanup.
            gc.collect()


def _percentile(values: list[float], percentile: int) -> float | None:
    if not values:
        return None
    ordered = sorted(values)
    index = min(len(ordered) - 1, round((percentile / 100) * (len(ordered) - 1)))
    return float(ordered[index])


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--recording", type=Path)
    parser.add_argument("--duration-s", type=float, default=60)
    args = parser.parse_args()
    print(json.dumps(run(args.recording, args.duration_s), ensure_ascii=True, indent=2))


if __name__ == "__main__":
    main()
