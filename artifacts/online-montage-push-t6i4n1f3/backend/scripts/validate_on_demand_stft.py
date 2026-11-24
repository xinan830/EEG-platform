"""Read-only real-recording validation for STFT full/on-demand reuse.

Run with: uv run --with psutil python scripts/validate_on_demand_stft.py
The recording is only read. Catalog, run, and artifact state is created in a
temporary directory outside the source recording.
"""

from __future__ import annotations

import argparse
import gc
import hashlib
import json
import sys
import tempfile
import time
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from app.algorithm_runtime.on_demand import DynamicWindowRequest
from app.bootstrap import build_builtin_registry
from app.models.run import RunCreateRequest
from app.services.on_demand_dynamic import DynamicWindowService
from app.services.recordings import RecordingService
from app.services.run_queue import PersistentRunQueue


def file_hashes(root: Path) -> dict[str, str]:
    return {
        path.name: hashlib.sha256(path.read_bytes()).hexdigest()
        for path in root.iterdir()
        if path.is_file()
    }


def wait_window(service: DynamicWindowService, window_id: str) -> dict:
    deadline = time.monotonic() + 300
    while time.monotonic() < deadline:
        result = service.get(window_id)
        if result["state"] not in {"queued", "preparing_source", "computing_window"}:
            return result
        time.sleep(0.01)
    raise TimeoutError("STFT window did not finish within the validation deadline")


def stft_config(recording, channel: str) -> dict:
    return {
        "algorithm_id": "stft",
        "channel": channel,
        "mode": "dynamic",
        "time": {"start_s": 0.0, "end_s": float(recording.duration_s)},
        "dynamic_window_s": 5.0,
        "refresh_step_s": 1.0,
        "low_hz": 1.0,
        "high_hz": 30.0,
        "notch_hz": None,
    }


def validate(recording_root: Path, channel: str) -> dict:
    before = file_hashes(recording_root)
    with tempfile.TemporaryDirectory(prefix="brain-stft-validation-") as scratch_name:
        scratch = Path(scratch_name)
        recordings = RecordingService(scratch / "stored", scratch / "catalog.sqlite3")
        recording = recordings.register_wpf_recording(
            str(recording_root), signal_mode="acquisition_montage"
        )
        selected = channel
        if selected.casefold() not in {item.casefold() for item in recording.channels}:
            raise ValueError(f"channel {channel!r} is not in recording channels: {recording.channels}")

        runs = PersistentRunQueue(recordings, recordings.database_path, scratch / "artifacts")
        windows = DynamicWindowService(
            recordings,
            build_builtin_registry(),
            recordings.database_path,
            scratch / "artifacts",
        )
        config = stft_config(recording, selected)
        request = RunCreateRequest(
            recording_id=recording.id,
            analysis_type="official_algorithm",
            config=config,
        )
        report = {
            "schema": "on-demand-stft-diagnostic-v1",
            "channel": selected,
            "duration_s": float(recording.duration_s),
            "sfreq_hz": float(recording.sfreq),
            "source_files_sha256": before,
            "reused_on_demand_window_count": 0,
            "max_abs_error_power_linear": 0.0,
            "max_abs_error_power_db": 0.0,
            "relative_time_axis": None,
            "frequency_axis_equal": False,
            "matrix_shape_equal": False,
        }
        ensured = None
        try:
            # Establish an independent full execution before creating any
            # on-demand artifact. This prevents the parity check from merely
            # reading the same cached matrix twice.
            resolved = runs.base._resolve_request(request, recording)
            independent_result, independent_arrays, _ = runs.base.executor.execute(
                request.analysis_type, recording, resolved,
            )
            ensured = windows.ensure(DynamicWindowRequest(
                recording_id=recording.id,
                algorithm_id="stft",
                config=config,
                cursor_sample=round(65.0 * recording.sfreq),
                request_id="stft-real-reuse-seed",
            ))
            on_demand = wait_window(windows, ensured["window_id"])
            if on_demand["state"] != "ready":
                raise AssertionError(on_demand)
            on_demand_result = on_demand["result"]
            report["relative_time_axis"] = on_demand_result["provenance"].get("inner_time_axis")
            if report["relative_time_axis"] != "relative_window_time_center_s":
                raise AssertionError(on_demand_result["provenance"])

            queued = runs.enqueue(request)
            full = runs.process_next()
            if full.run_id != queued.run_id or full.status.value != "completed":
                raise AssertionError(full.error)
            provenance = full.result_summary["execution_provenance"]
            report["reused_on_demand_window_count"] = int(
                provenance.get("reused_on_demand_window_count", 0)
            )
            if report["reused_on_demand_window_count"] < 1:
                raise AssertionError(provenance)

            artifact = runs.list_artifacts(full.run_id)[0]
            arrays = runs.base.artifacts.read_npz(artifact)
            structured = full.result_summary["structured"]
            target = next(
                item for item in structured["windows"]
                if item["start_sample"] == on_demand["range"]["start_sample"]
                and item["end_sample"] == on_demand["range"]["end_sample"]
            )
            index = structured["windows"].index(target)
            full_time = np.asarray(arrays["axis_time_center_s"], dtype=float)
            on_demand_time = np.asarray(on_demand_result["axes"]["time_center_s"], dtype=float)
            full_frequency = np.asarray(arrays["axis_frequency_hz"], dtype=float)
            on_demand_frequency = np.asarray(on_demand_result["axes"]["frequency_hz"], dtype=float)
            linear_full = np.asarray(arrays["power_linear"][index], dtype=float)
            linear_on_demand = np.asarray(on_demand_result["arrays"]["power_linear"], dtype=float)
            db_full = np.asarray(arrays["power_db"][index], dtype=float)
            db_on_demand = np.asarray(on_demand_result["arrays"]["power_db"], dtype=float)
            independent_windows = independent_result["structured"]["windows"]
            independent_index = next(
                candidate for candidate, item in enumerate(independent_windows)
                if item["start_sample"] == on_demand["range"]["start_sample"]
                and item["end_sample"] == on_demand["range"]["end_sample"]
            )
            np.testing.assert_allclose(
                linear_full,
                np.asarray(independent_arrays["power_linear"][independent_index], dtype=float),
                rtol=1e-12,
                atol=1e-24,
            )
            np.testing.assert_allclose(
                db_full,
                np.asarray(independent_arrays["power_db"][independent_index], dtype=float),
                rtol=1e-12,
                atol=1e-12,
            )
            np.testing.assert_array_equal(full_time, on_demand_time)
            np.testing.assert_array_equal(full_frequency, on_demand_frequency)
            np.testing.assert_array_equal(linear_full.shape, linear_on_demand.shape)
            np.testing.assert_array_equal(db_full.shape, db_on_demand.shape)
            np.testing.assert_allclose(linear_full, linear_on_demand, rtol=1e-12, atol=1e-24)
            np.testing.assert_allclose(db_full, db_on_demand, rtol=1e-12, atol=1e-12)
            report["frequency_axis_equal"] = True
            report["matrix_shape_equal"] = True
            report["max_abs_error_power_linear"] = float(np.max(np.abs(linear_full - linear_on_demand)))
            report["max_abs_error_power_db"] = float(np.max(np.abs(db_full - db_on_demand)))
            report["time_center_s"] = full_time.tolist()
            report["frequency_points"] = int(full_frequency.size)
            report["matrix_shape"] = list(linear_full.shape)
            report["output_units"] = on_demand_result["array_units"]
            report["window_range_s"] = on_demand["range"]
            report["source_and_history_unchanged"] = file_hashes(recording_root) == before
            if not report["source_and_history_unchanged"]:
                raise AssertionError("recording files changed during validation")
            return report
        finally:
            if ensured is not None:
                windows.cancel_subscription(ensured["subscription_id"])
            windows.close(wait=True)
            recordings._analysis_preprocess_cache.clear_recording(recording.id)
            gc.collect()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--recording", type=Path, required=True)
    parser.add_argument("--channel", default="F3-REF")
    args = parser.parse_args()
    print(json.dumps(validate(args.recording, args.channel), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
