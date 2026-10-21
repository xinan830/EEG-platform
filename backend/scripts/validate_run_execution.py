"""Run isolated real-recording checks without modifying application storage."""

from __future__ import annotations

import argparse
import gc
import json
import ctypes
import threading
from pathlib import Path
import tempfile
from ctypes import wintypes
from time import perf_counter, sleep

import numpy as np

from app.algorithm_runtime.on_demand import DynamicWindowRequest
from app.algorithms.faa.official import compute_faa
from app.algorithms.faa.runner import FaaAlgorithm
from app.bootstrap import build_builtin_registry
from app.models.run import RunCreateRequest, RunStatus
from app.services.on_demand_dynamic import DynamicWindowService
from app.services.recording_samples import WpfSampleReader
from app.services.recordings import RecordingService
from app.services.run_queue import PersistentRunQueue
from tests.test_faa_continuous import independent_faa, reference_filter

class PROCESS_MEMORY_COUNTERS(ctypes.Structure):
    _fields_ = [("cb", wintypes.DWORD), ("PageFaultCount", wintypes.DWORD),
                ("PeakWorkingSetSize", ctypes.c_size_t), ("WorkingSetSize", ctypes.c_size_t),
                ("QuotaPeakPagedPoolUsage", ctypes.c_size_t), ("QuotaPagedPoolUsage", ctypes.c_size_t),
                ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t), ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
                ("PagefileUsage", ctypes.c_size_t), ("PeakPagefileUsage", ctypes.c_size_t)]

def working_set_bytes():
    counters = PROCESS_MEMORY_COUNTERS()
    counters.cb = ctypes.sizeof(counters)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    psapi = ctypes.WinDLL("psapi", use_last_error=True)
    kernel32.GetCurrentProcess.restype = wintypes.HANDLE
    psapi.GetProcessMemoryInfo.argtypes = [wintypes.HANDLE, ctypes.POINTER(PROCESS_MEMORY_COUNTERS), wintypes.DWORD]
    psapi.GetProcessMemoryInfo.restype = wintypes.BOOL
    handle = kernel32.GetCurrentProcess()
    if not psapi.GetProcessMemoryInfo(handle, ctypes.byref(counters), counters.cb):
        raise OSError("GetProcessMemoryInfo failed")
    return int(counters.WorkingSetSize)

def request(recording, algorithm, dynamic):
    config = {"algorithm_id": algorithm, "channel": "F3", "mode": "dynamic" if dynamic else "static",
              "time": {"start_s": 0, "end_s": recording.duration_s},
              "dynamic_window_s": 20 if algorithm == "faa" else 5, "refresh_step_s": 1}
    if algorithm in {"psd", "stft", "faa"}:
        config.update(low_hz=1, high_hz=50, notch_hz=50)
    if algorithm == "faa":
        config["f4_channel"] = "F4"
    if algorithm == "brainbeat":
        config.update(channel="Fz", secondary_channel="Pz")
    if algorithm == "band_ratio":
        config.update(numerator_low_hz=4, numerator_high_hz=8, denominator_low_hz=13, denominator_high_hz=30)
    return RunCreateRequest(recording_id=recording.id, analysis_type="official_algorithm", config=config)


def validate(directory):
    memory = {"baseline_working_set_bytes": working_set_bytes(), "peak_working_set_bytes": 0}
    stop_sampling = threading.Event()
    def sample_memory():
        while not stop_sampling.wait(0.02):
            memory["peak_working_set_bytes"] = max(memory["peak_working_set_bytes"], working_set_bytes())
    sampler = threading.Thread(target=sample_memory, daemon=True)
    sampler.start()
    memory["peak_working_set_bytes"] = memory["baseline_working_set_bytes"]
    report = {"runs": [], "cancellations": [], "faa": {}, "memory": memory}
    with tempfile.TemporaryDirectory(prefix="brain-run-validation-", ignore_cleanup_errors=True) as temporary:
        root = Path(temporary)
        recordings = RecordingService(root / "recordings", root / "analysis.sqlite3")
        recording = recordings.register_wpf_recording(str(directory))
        report["source"] = {"sha256": recording.source_sha256, "duration_s": recording.duration_s,
                            "sfreq_hz": recording.sfreq, "channel_count": len(recording.channels)}
        queue = PersistentRunQueue(recordings, recordings.database_path, root / "artifacts")
        windows = DynamicWindowService(recordings, build_builtin_registry(), recordings.database_path,
                                       root / "artifacts", compute_coordinator=queue.compute_coordinator)
        faa_dynamic = None
        algorithms = [module.manifest.algorithm_id for module in queue.base.algorithm_runtime_registry.list()]
        for algorithm in algorithms:
            for dynamic in (False, True):
                payload = request(recording, algorithm, dynamic)
                preview = None
                if dynamic:
                    cursor = int(payload.config["dynamic_window_s"] * recording.sfreq)
                    started = perf_counter()
                    preview = windows.ensure(DynamicWindowRequest(
                        recording_id=recording.id, algorithm_id=algorithm, config=payload.config,
                        cursor_sample=cursor, request_id=f"pressure-preview-{algorithm}",
                    ))
                    subscription_id = preview["subscription_id"]
                    while preview["state"] in {"queued", "preparing_source", "computing_window"}:
                        sleep(0.01)
                        preview = windows.get(preview["window_id"])
                    preview_elapsed_s = perf_counter() - started
                run = queue.enqueue(payload)
                progress = []
                window_ms = []
                progress_write_ms = []
                last_progress_at = [perf_counter()]
                detailed_frame_ms = []
                original_execute_static = None
                if algorithm == "faa" and dynamic:
                    from app.algorithms.faa.runner import FaaAlgorithm
                    original_execute_static = FaaAlgorithm.execute_static
                    def timed_faa_frame(instance, *args, **kwargs):
                        started_frame = perf_counter()
                        try:
                            return original_execute_static(instance, *args, **kwargs)
                        finally:
                            detailed_frame_ms.append((perf_counter() - started_frame) * 1000)
                    FaaAlgorithm.execute_static = timed_faa_frame
                update = queue.repository.update_progress
                def observed(*args, **kwargs):
                    started_write = perf_counter()
                    value = update(*args, **kwargs)
                    write_elapsed = (perf_counter() - started_write) * 1000
                    if value.progress_completed > (progress[-1] if progress else 0):
                        now = perf_counter()
                        window_ms.append((now - last_progress_at[0]) * 1000)
                        progress_write_ms.append(write_elapsed)
                        last_progress_at[0] = now
                        progress.append(value.progress_completed)
                    return value
                queue.repository.update_progress = observed
                start = perf_counter()
                final = queue.process_next()
                elapsed = perf_counter() - start
                queue.repository.update_progress = update
                if original_execute_static is not None:
                    from app.algorithms.faa.runner import FaaAlgorithm
                    FaaAlgorithm.execute_static = original_execute_static
                assert final.run_id == run.run_id
                assert final.status is RunStatus.COMPLETED, final.error
                assert final.progress_completed == final.progress_total
                assert progress == sorted(progress)
                artifacts = queue.list_artifacts(final.run_id)
                assert len(artifacts) == 1
                arrays = queue.base.artifacts.read_npz(artifacts[0])
                summary = final.result_summary or {}
                metric = summary.get("metric", {})
                states = metric.get("window_state_counts", {})
                if "series" in metric:
                    states = {state: sum(point["analysis_state"] == state for point in metric["series"])
                              for state in {point["analysis_state"] for point in metric["series"]}}
                report["runs"].append({"algorithm": algorithm, "mode": "dynamic" if dynamic else "static",
                    "elapsed_s": round(elapsed, 4), "status": final.status.value,
                    "processed_windows": final.progress_completed, "total_windows": final.progress_total,
                    "artifact_verified": bool(arrays), "window_states": states,
                    "scientific_version": final.scientific_version,
                    "per_window_ms": ({"p50": round(float(np.percentile(window_ms, 50)), 3),
                                       "p95": round(float(np.percentile(window_ms, 95)), 3),
                                       "max": round(max(window_ms), 3), "count": len(window_ms)}
                                      if dynamic and window_ms else None),
                    "progress_write_ms": ({"p50": round(float(np.percentile(progress_write_ms, 50)), 3),
                                           "p95": round(float(np.percentile(progress_write_ms, 95)), 3),
                                           "max": round(max(progress_write_ms), 3), "count": len(progress_write_ms)}
                                          if dynamic and progress_write_ms else None),
                    "faa_frame_compute_ms": ({"p50": round(float(np.percentile(detailed_frame_ms, 50)), 3),
                                               "p95": round(float(np.percentile(detailed_frame_ms, 95)), 3),
                                               "max": round(max(detailed_frame_ms), 3), "count": len(detailed_frame_ms)}
                                              if detailed_frame_ms else None),
                    "faa_frame_outliers": ([{"window_index": index, "compute_ms": round(value, 3)}
                                             for index, value in enumerate(detailed_frame_ms)
                                             if value > 100] if detailed_frame_ms else []),
                    "preview_seed_elapsed_s": round(preview_elapsed_s, 4) if dynamic else None,
                    "preview_seed_state": preview["state"] if dynamic else None,
                    "reused_on_demand_window_count": int((summary.get("execution_provenance") or {}).get(
                        "reused_on_demand_window_count", 0))})
                if preview is not None:
                    try:
                        windows.cancel_subscription(subscription_id)
                    except KeyError:
                        pass
                print(json.dumps(report["runs"][-1], ensure_ascii=False), flush=True)
                if algorithm == "faa" and dynamic:
                    faa_dynamic = final

        # Different ranges avoid reusing completed results when testing cancellation.
        for algorithm in algorithms:
            payload = request(recording, algorithm, True)
            payload.config["time"]["end_s"] -= 1
            run = queue.enqueue(payload)
            update = queue.repository.update_progress
            def cancel_after_window(*args, **kwargs):
                result = update(*args, **kwargs)
                if result.progress_completed == 1:
                    cancel_started[0] = perf_counter()
                    queue.cancel(run.run_id)
                return result
            cancel_started = [None]
            queue.repository.update_progress = cancel_after_window
            cancel_call_started = perf_counter()
            final = queue.process_next()
            cancel_latency_ms = (perf_counter() - cancel_started[0]) * 1000 if cancel_started[0] else None
            queue.repository.update_progress = update
            assert final.status is RunStatus.CANCELLED
            assert final.progress_completed == 1
            assert not queue.list_artifacts(run.run_id)
            report["cancellations"].append({"algorithm": algorithm, "completed": 1, "total": final.progress_total,
                                             "cancel_to_terminal_ms": round(cancel_latency_ms, 3),
                                             "cancel_phase_elapsed_ms": round((perf_counter() - cancel_call_started) * 1000, 3)})

        assert faa_dynamic is not None
        reader = WpfSampleReader(directory)
        indexes = [reader.names.index(name) for name in ("F3", "F4")]
        raw = reader.read(0, reader.sample_count, indexes)
        sfreq = reader.sfreq
        reader.close()
        filtered = reference_filter(raw, sfreq, 50)
        series = faa_dynamic.result_summary["metric"]["series"]
        errors, legacy_changes = [], []
        legacy_quality_differences = 0
        quality_matches = 0
        for point in series:
            bounds = {"start_s": point["window_start_s"], "end_s": point["window_end_s"]}
            start, stop = int(np.floor(bounds["start_s"] * sfreq)), int(round(bounds["end_s"] * sfreq))
            expected, clean = independent_faa(filtered[start:stop], sfreq)
            actual = point["value"]
            assert (actual is None) == (expected is None)
            assert point["source_quality"]["clean_segments"] == clean
            quality_matches += 1
            old_pair = reference_filter(raw[start:stop], sfreq, 50)
            old_report = compute_faa(old_pair[:, 0], old_pair[:, 1], sfreq)
            if old_report["clean_epochs"] != clean or (old_report["faa"] is None) != (actual is None):
                legacy_quality_differences += 1
            if actual is not None:
                assert np.isclose(actual, expected, rtol=1e-11, atol=1e-12)
                errors.append(abs(actual - expected))
                legacy = old_report["faa"]
                if legacy is not None:
                    legacy_changes.append(abs(actual - legacy))
        report["faa"] = {"version": FaaAlgorithm.manifest.scientific_version,
            "checked_windows": len(series), "availability_matches": quality_matches,
            "available_windows": len(errors), "max_independent_absolute_error": max(errors, default=None),
            "max_legacy_absolute_difference": max(legacy_changes, default=None),
            "legacy_quality_different_windows": legacy_quality_differences,
            "rtol": 1e-11, "atol": 1e-12}
        recordings._analysis_preprocess_cache.clear_recording(recording.id)
        gc.collect()
    stop_sampling.set()
    sampler.join()
    windows.close(wait=True)
    report["memory"]["peak_working_set_bytes"] = max(report["memory"]["peak_working_set_bytes"], working_set_bytes())
    report["memory"]["peak_delta_bytes"] = report["memory"]["peak_working_set_bytes"] - report["memory"]["baseline_working_set_bytes"]
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--output", type=Path, help="write the sanitized JSON report to this path")
    arguments = parser.parse_args()
    rendered = json.dumps(validate(arguments.directory), ensure_ascii=False, indent=2)
    if arguments.output:
        arguments.output.parent.mkdir(parents=True, exist_ok=True)
        arguments.output.write_text(rendered + "\n", encoding="utf-8")
    print(rendered)
