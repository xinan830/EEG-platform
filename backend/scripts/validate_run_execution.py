"""Run isolated real-recording checks without modifying application storage."""

from __future__ import annotations

import argparse
import gc
import json
from pathlib import Path
import tempfile
from time import perf_counter

import numpy as np

from app.algorithms.faa.official import compute_faa
from app.algorithms.faa.runner import FaaAlgorithm
from app.models.run import RunCreateRequest, RunStatus
from app.services.recordings import RecordingService
from app.services.recording_samples import WpfSampleReader
from app.services.run_queue import PersistentRunQueue
from tests.test_faa_continuous import independent_faa, reference_filter


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
    report = {"runs": [], "cancellations": [], "faa": {}}
    with tempfile.TemporaryDirectory(prefix="brain-run-validation-", ignore_cleanup_errors=True) as temporary:
        root = Path(temporary)
        recordings = RecordingService(root / "recordings", root / "analysis.sqlite3")
        recording = recordings.register_wpf_recording(str(directory))
        report["source"] = {"sha256": recording.source_sha256, "duration_s": recording.duration_s,
                            "sfreq_hz": recording.sfreq, "channel_count": len(recording.channels)}
        queue = PersistentRunQueue(recordings, recordings.database_path, root / "artifacts")
        faa_dynamic = None
        algorithms = [module.manifest.algorithm_id for module in queue.base.algorithm_runtime_registry.list()]
        for algorithm in algorithms:
            for dynamic in (False, True):
                run = queue.enqueue(request(recording, algorithm, dynamic))
                progress = []
                update = queue.repository.update_progress
                def observed(*args, **kwargs):
                    value = update(*args, **kwargs)
                    progress.append(value.progress_completed)
                    return value
                queue.repository.update_progress = observed
                start = perf_counter()
                final = queue.process_next()
                elapsed = perf_counter() - start
                queue.repository.update_progress = update
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
                    "scientific_version": final.scientific_version})
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
                    queue.cancel(run.run_id)
                return result
            queue.repository.update_progress = cancel_after_window
            final = queue.process_next()
            queue.repository.update_progress = update
            assert final.status is RunStatus.CANCELLED
            assert final.progress_completed == 1
            assert not queue.list_artifacts(run.run_id)
            report["cancellations"].append({"algorithm": algorithm, "completed": 1, "total": final.progress_total})

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
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    arguments = parser.parse_args()
    print(json.dumps(validate(arguments.directory), ensure_ascii=False, indent=2))
