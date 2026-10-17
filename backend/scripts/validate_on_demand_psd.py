"""Read-only real-recording equivalence and reproducible hour-scale diagnostics.

Run with: uv run --with psutil python scripts/validate_on_demand_psd.py
Optional --recording points to an immutable completed WPF recording. All new
databases/artifacts are disposable and outside the original recording.
"""

from __future__ import annotations

import argparse
import gc
import hashlib
import json
import logging
import sys
import tempfile
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from unittest.mock import patch
from weakref import WeakSet

import numpy as np
import psutil

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tests"))

from on_demand_fixtures import configure_api, psd_config
from app.algorithm_runtime.on_demand import DynamicWindowRequest
from app.bootstrap import build_builtin_registry
from app.models.run import RunCreateRequest
from app.services.on_demand_dynamic import DynamicWindowService
from app.services.recordings import RecordingService
from app.services.results import ResultService
from app.services.run_queue import PersistentRunQueue
from app.services.validations import ValidationService
from app.services.mapped_preprocessing import MappedArrays
import app.services.mapped_preprocessing as preprocessing
from app.services.dynamic_window_artifacts import DynamicWindowArtifactStore
from app.services.scientific_compute_coordinator import ScientificComputeCoordinator


def hashes(root):
    result = {}
    for path in root.iterdir():
        if path.is_file():
            with path.open("rb") as stream:
                result[path.name] = hashlib.file_digest(stream, "sha256").hexdigest()
    return result


def terminal(service, window_id):
    deadline = time.monotonic() + 180
    while time.monotonic() < deadline:
        response = service.get(window_id)
        if response["state"] not in {"queued", "preparing_source", "computing_window"}:
            return response
        time.sleep(0.005)
    raise TimeoutError("window did not finish within the diagnostic deadline")


def compare_real(root, scratch):
    before = hashes(root)
    recordings = RecordingService(scratch / "stored", scratch / "catalog.sqlite3")
    recording = recordings.register_wpf_recording(str(root), signal_mode="acquisition_montage")
    # The real combined strategy must use one bounded coordinator. Creating
    # one gate per worker would make fairness and resource measurements false.
    coordinator = ScientificComputeCoordinator()
    runs = PersistentRunQueue(recordings, recordings.database_path, scratch / "artifacts",
                              compute_coordinator=coordinator)
    windows = DynamicWindowService(recordings, build_builtin_registry(), recordings.database_path,
                                   scratch / "artifacts", compute_coordinator=coordinator)
    results = ResultService(runs, ValidationService(recordings.database_path))
    report = {"duration_s": recording.duration_s, "sfreq_hz": recording.sfreq, "channels_tested": 0,
              "windows_tested": 0, "states": {}, "max_abs_error": 0.0, "max_relative_error": 0.0,
              "rtol": 1e-12, "atol_psd": 1e-24, "atol_ratio": 1e-12,
              "source_files_sha256": before, "full_artifacts_sha256": [],
              "reused_on_demand_window_counts": []}
    try:
        for channel in recording.channels[:2]:
            config = psd_config(recording, channel=channel)
            seed = windows.ensure(DynamicWindowRequest(recording_id=recording.id, algorithm_id="psd",
                config=config, cursor_sample=round(5.0 * recording.sfreq), request_id=f"reuse-seed-{channel}"))
            seed_result = terminal(windows, seed["window_id"])
            assert seed_result["state"] == "ready", seed_result
            queued = runs.enqueue(RunCreateRequest(recording_id=recording.id, analysis_type="official_algorithm", config=config))
            full = runs.process_next()
            assert full.run_id == queued.run_id and full.status.value == "completed", full.error
            provenance = full.result_summary.get("execution_provenance", {})
            reused_count = int(provenance.get("reused_on_demand_window_count", 0))
            assert reused_count >= 1, provenance
            report["reused_on_demand_window_counts"].append(reused_count)
            windows.cancel_subscription(seed["subscription_id"])
            artifact = runs.list_artifacts(full.run_id)[0]
            arrays = runs.base.artifacts.read_npz(artifact)
            artifact_path = runs.base.artifacts.root / artifact.relative_path
            artifact_before = hashes(artifact_path.parent)
            with artifact_path.open("rb") as stream:
                report["full_artifacts_sha256"].append(hashlib.file_digest(stream, "sha256").hexdigest())
            frames = full.result_summary["structured"]["windows"]
            window_evidence = json.loads(str(arrays["window_evidence_json"].item()))
            for index, frame in enumerate(frames):
                ensured = windows.ensure(DynamicWindowRequest(recording_id=recording.id, algorithm_id="psd",
                    config=config, cursor_sample=frame["end_sample"], request_id=f"diagnostic-{index}"))
                current = terminal(windows, ensured["window_id"])
                if "identity_sample" not in report:
                    report["identity_sample"] = {key: value for key, value in current["identity"].items()
                        if key not in {"recording_id", "project_scope"}}
                expected = {"Complete": "ready", "Partial": "ready", "Rejected": "rejected", "Unavailable": "unavailable"}[frame["state"]]
                assert current["state"] == expected
                assert current["range"]["start_sample"] == frame["start_sample"]
                assert current["range"]["end_sample"] == frame["end_sample"]
                report["states"][expected] = report["states"].get(expected, 0) + 1
                if expected == "ready":
                    saved = results.window_preview(full.run_id, cursor_sample=frame["end_sample"])
                    np.testing.assert_array_equal(current["result"]["axes"]["frequency_hz"], arrays["axis_frequency_hz"])
                    for name, atol in [("psd", 1e-24), ("band_share", 1e-12)]:
                        actual = np.asarray(current["result"]["arrays"][name])
                        reference = arrays[name][index]
                        np.testing.assert_allclose(actual, reference, rtol=1e-12, atol=atol)
                        difference = np.abs(actual - reference)
                        report["max_abs_error"] = max(report["max_abs_error"], float(difference.max()))
                        report["max_relative_error"] = max(report["max_relative_error"],
                            float((difference / np.maximum(np.abs(reference), atol)).max()))
                    assert current["result"]["array_units"]["psd"] == "V^2/Hz"
                    assert current["result"]["evidence"] == window_evidence[index]
                    assert current["result"]["provenance"]["preprocessing_scope"] == "whole_recording_before_window_slice"
                    assert saved["recording_id"] == recording.id
                else:
                    assert current["failure"]["code"] == frame["failure"]["code"]
                    if expected == "rejected":
                        assert current["failure"]["details"] == frame["failure"]["detail"]
                windows.cancel_subscription(ensured["subscription_id"])
                report["windows_tested"] += 1
            assert hashes(artifact_path.parent) == artifact_before
            report["channels_tested"] += 1
        assert hashes(root) == before
        report["source_and_history_unchanged"] = True
        report["window_evidence_equal"] = True
        report["compute_coordinator"] = coordinator.snapshot()
        return report
    finally:
        windows.close(wait=True)
        coordinator.close()


class Resources:
    def __init__(self, recordings):
        self.recordings = recordings
        self.process = psutil.Process()
        self.base_rss = self.process.memory_info().rss
        self.peak_rss = self.base_rss
        self.peak_temp = 0
        self.owners = WeakSet()
        self.lock = threading.RLock()
        self.phase_ms = {"source_read": [], "continuous_preprocess": [], "artifact_write": [], "artifact_read": []}
        self.stop = threading.Event()
        self.thread = threading.Thread(target=self.sample, daemon=True)

    def sample(self):
        while not self.stop.wait(0.02):
            self.peak_rss = max(self.peak_rss, self.process.memory_info().rss)

    def allocate(self, owner, shape):
        with self.lock:
            array = self.original_allocate(owner, shape)
            self.owners.add(owner)
            # Update at every allocation, including transient filtering maps.
            self.peak_temp = max(self.peak_temp, sum(
                Path(mapped.filename).stat().st_size
                for item in list(self.owners) for mapped in item.maps.values()))
            return array

    def release(self, owner, array):
        with self.lock:
            self.original_release(owner, array)

    def __enter__(self):
        self.original_allocate = MappedArrays.allocate
        self.original_release = MappedArrays.release
        self.allocation_patch = patch.object(MappedArrays, "allocate", lambda owner, shape: self.allocate(owner, shape))
        self.release_patch = patch.object(MappedArrays, "release", lambda owner, array: self.release(owner, array))
        self.allocation_patch.start()
        self.release_patch.start()
        original_read = RecordingService.open_sample_reader
        phases = self.phase_ms
        class TimedReader:
            def __init__(self, reader):
                self.reader = reader
            def __getattr__(self, name):
                return getattr(self.reader, name)
            def read(self, *args, **kwargs):
                started = time.perf_counter()
                result = self.reader.read(*args, **kwargs)
                phases["source_read"].append((time.perf_counter() - started) * 1000)
                return result
        self.reader_patch = patch.object(RecordingService, "open_sample_reader",
            lambda owner, recording: TimedReader(original_read(owner, recording)))
        original_preprocess = preprocessing.preprocess_blocks
        def timed_preprocess(*args, **kwargs):
            started = time.perf_counter()
            result = original_preprocess(*args, **kwargs)
            phases["continuous_preprocess"].append((time.perf_counter() - started) * 1000)
            return result
        self.preprocess_patch = patch.object(preprocessing, "preprocess_blocks", timed_preprocess)
        self.reader_patch.start()
        self.preprocess_patch.start()
        self.artifact_patches = []
        for method, key in [("write", "artifact_write"), ("read", "artifact_read")]:
            original = getattr(DynamicWindowArtifactStore, method)
            def timed(owner, *args, _original=original, _key=key, **kwargs):
                started = time.perf_counter()
                result = _original(owner, *args, **kwargs)
                phases[_key].append((time.perf_counter() - started) * 1000)
                return result
            current_patch = patch.object(DynamicWindowArtifactStore, method, timed)
            current_patch.start()
            self.artifact_patches.append(current_patch)
        self.thread.start()
        return self

    def __exit__(self, *_):
        self.stop.set()
        self.thread.join()
        self.release_patch.stop()
        self.allocation_patch.stop()
        self.reader_patch.stop()
        self.preprocess_patch.stop()
        for current_patch in self.artifact_patches:
            current_patch.stop()


class PreparationTiming(logging.Handler):
    def __init__(self):
        super().__init__()
        self.values = []
        self.computations = []

    def emit(self, record):
        if record.msg.startswith("dynamic_window_source_prepared"):
            self.values.append(float(record.args[-1]))
        elif record.msg.startswith("dynamic_window_computed"):
            self.computations.append(float(record.args[-1]))


def distribution(values):
    if not values:
        return {"count": 0, "p50_ms": None, "p95_ms": None, "max_ms": None}
    return {"count": len(values), "p50_ms": float(np.percentile(values, 50)),
            "p95_ms": float(np.percentile(values, 95)), "max_ms": float(max(values))}


def benchmark(scratch, duration_s, gap):
    registration_ms = []
    original_register = RecordingService.register_wpf_recording
    def timed_register(owner, *args, **kwargs):
        started = time.perf_counter()
        result = original_register(owner, *args, **kwargs)
        registration_ms.append((time.perf_counter() - started) * 1000)
        return result
    with patch.object(RecordingService, "register_wpf_recording", timed_register):
        return _benchmark(scratch, duration_s, gap, registration_ms)


def _benchmark(scratch, duration_s, gap, registration_ms):
    client, recording, service, _, root = configure_api(scratch, gap=gap, duration_s=duration_s)
    before = hashes(root)
    monitor = Resources(service.recordings)
    timing = PreparationTiming()
    logger = logging.getLogger("app.services.on_demand_worker")
    old_level = logger.level
    logger.setLevel(logging.INFO)
    logger.addHandler(timing)
    monitor.__enter__()
    cpu_before = monitor.process.cpu_times()
    http_requests = 0
    states = {}
    request_lock = threading.Lock()
    def send(method, path, **kwargs):
        nonlocal http_requests
        response = client.request(method, path, **kwargs)
        with request_lock:
            http_requests += 1
        return response
    latencies = []
    config = psd_config(recording)
    benchmark_started = time.perf_counter()
    def ensure(cursor, channel="F3-REF"):
        started = time.perf_counter()
        response = send("POST", "/api/analysis-windows/ensure", json={"recording_id": recording.id,
            "algorithm_id": "psd", "config": {**config, "channel": channel}, "cursor_sample": int(cursor * recording.sfreq)})
        assert response.status_code in {200, 202}, response.text
        value = response.json()
        deadline = time.monotonic() + 180
        while value["state"] in {"queued", "preparing_source", "computing_window"}:
            if time.monotonic() > deadline:
                raise TimeoutError("diagnostic window wait exceeded 180 seconds")
            time.sleep(0.02)
            polled = send("GET", f"/api/analysis-windows/{value['window_id']}")
            assert polled.status_code == 200, polled.text
            value = polled.json()
        assert value["state"] in {"ready", "rejected"}, value
        with request_lock:
            states[value["state"]] = states.get(value["state"], 0) + 1
        released = send("DELETE", f"/api/analysis-windows/subscriptions/{response.json()['subscription_id']}")
        assert released.status_code == 200, released.text
        return (time.perf_counter() - started) * 1000
    try:
        registration_start = time.perf_counter()
        service.recordings.register_wpf_recording(str(root), signal_mode="acquisition_montage")
        hot_registration_ms = (time.perf_counter() - registration_start) * 1000
        cold_ms = ensure(5)
        for cursor in range(6, 26):
            latencies.append(ensure(cursor))
        rng = np.random.default_rng(42)
        for cursor in rng.integers(26, int(duration_s), 24):
            latencies.append(ensure(int(cursor)))
        with ThreadPoolExecutor(max_workers=4) as pool:
            concurrent_start = min(100, int(duration_s) - 8)
            concurrent = list(pool.map(lambda cursor: ensure(cursor, "F7-REF"), range(concurrent_start, concurrent_start + 8)))
        assert hashes(root) == before
        cpu = monitor.process.cpu_times()
        return {"duration_s": duration_s, "gap": gap, "source_channels": 2,
                "cold_registration_ms": registration_ms[0],
                "hot_registration_ms": hot_registration_ms, "cold_first_window_total_ms": cold_ms,
                "hot_sequential_random_total": distribution(latencies), "concurrent_total": distribution(concurrent),
                "source_preparation_or_cache_resolution": distribution(timing.values),
                "source_read_blocks": distribution(monitor.phase_ms["source_read"]),
                "continuous_preprocessing": distribution(monitor.phase_ms["continuous_preprocess"]),
                "window_computation": distribution(timing.computations),
                "artifact_write_and_verification": distribution(monitor.phase_ms["artifact_write"]),
                "artifact_reads_including_write_verification": distribution(monitor.phase_ms["artifact_read"]),
                "http_requests": http_requests, "states": states,
                "total_elapsed_ms": (time.perf_counter() - benchmark_started) * 1000,
                "cpu_seconds": cpu.user + cpu.system - cpu_before.user - cpu_before.system,
                "rss_baseline_bytes": monitor.base_rss, "rss_peak_bytes": monitor.peak_rss,
                "temporary_map_peak_bytes": monitor.peak_temp,
                "artifact_bytes": sum(path.stat().st_size for path in service.artifacts_dir.iterdir()),
                "source_unchanged": True}
    finally:
        logger.removeHandler(timing)
        logger.setLevel(old_level)
        service.close(wait=True)
        client.close()
        monitor.__exit__()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--recording", type=Path)
    parser.add_argument("--duration-s", type=float, default=3600)
    args = parser.parse_args()
    report = {"schema": "on-demand-psd-diagnostic-v2", "real_recording": None}
    with tempfile.TemporaryDirectory(prefix="brain-window-validation-") as temporary:
        scratch = Path(temporary)
        if args.recording:
            (scratch / "real").mkdir()
            report["real_recording"] = compare_real(args.recording, scratch / "real")
        for name, duration, gap in [("short", 60, False), ("continuous", args.duration_s, False), ("gap", args.duration_s, True)]:
            (scratch / name).mkdir()
            report[name] = benchmark(scratch / name, duration, gap)
        # sqlite connection contexts commit/rollback but handles may await GC.
        # All workers have joined, so collect unreachable handles before Windows cleanup.
        gc.collect()
    print(json.dumps(report, ensure_ascii=True, indent=2))


if __name__ == "__main__":
    main()
