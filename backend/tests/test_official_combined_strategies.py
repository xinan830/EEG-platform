"""Replay combined lifecycles against uncached full and isolated window science."""

import hashlib
import json
import threading
from concurrent.futures import ThreadPoolExecutor

import pytest

from combined_strategy_support import ALGORITHM_IDS, compare_arrays, compare_preview, compare_summary, ensure_preview, setup_strategy, uncached_full, validation_context
from on_demand_fixtures import source_hashes
from app.services.run_execution_context import RunExecutionContext
from test_on_demand_psd_api import wait_window


@pytest.mark.parametrize("algorithm_id", ALGORITHM_IDS)
@pytest.mark.parametrize("rejected", [False, True])
def test_combined_matches_uncached_full_and_isolates_preview_cancellation(tmp_path, monkeypatch, algorithm_id, rejected, record_property):
    services, config = setup_strategy(tmp_path, algorithm_id, rejected=rejected)
    client, recording, windows, runs, root = services
    before = source_hashes(root)
    entered, release = threading.Event(), threading.Event()
    original = RunExecutionContext.window_completed
    pool = ThreadPoolExecutor(max_workers=1)
    try:
        request, expected, arrays = uncached_full(runs, recording, config)

        # Pause after a real numerical frame and after releasing its compute lease.
        # Preview work therefore joins an already-progressing full calculation.
        def overlapping_window_completed(control):
            original(control)
            if control.completed == 1:
                entered.set()
                assert release.wait(20), "preview did not release background execution"

        monkeypatch.setattr(RunExecutionContext, "window_completed", overlapping_window_completed)
        queued = runs.enqueue(request)
        future = pool.submit(runs.process_next)
        assert entered.wait(10), "background did not enter execution"
        ensured = ensure_preview(client, recording, config)
        preview = wait_window(client, ensured["window_id"])
        assert preview["state"] == ("rejected" if rejected else "ready"), preview
        assert runs.get(queued.run_id).status.value == "running"
        assert runs.get(queued.run_id).progress_completed == 1
        end_s = config["dynamic_window_s"]
        assert preview["range"] == {"start_sample": 0, "end_sample": int(end_s * recording.sfreq),
                                    "start_s": 0.0, "end_s": float(end_s)}
        compare_preview(preview, expected, arrays)
        if preview["result"] is not None:
            assert preview["result"]["identity"]["scientific_version"] == queued.scientific_version
            assert preview["result"]["identity"]["source_sha256"] == recording.source_sha256
            assert preview["result"]["provenance"]["served_from"] == "on_demand_computation"
        assert preview["window_id"] != queued.run_id
        windows.cancel_subscription(ensured["subscription_id"])
        assert runs.get(queued.run_id).status.value == "running"
        release.set()
        full = future.result(timeout=30)
        assert full.status.value == "completed", full.error
        artifact = runs.list_artifacts(full.run_id)[0]
        record_property("validation_context", json.dumps(validation_context(recording, config, full, artifact), sort_keys=True))
        actual = runs.base.artifacts.read_npz(artifact)
        record_property("array_comparison", json.dumps(compare_arrays(actual, arrays), sort_keys=True))
        assert full.actual_range == queued.requested_range
        compare_summary(full.result_summary, expected)
        provenance = full.result_summary["execution_provenance"]
        assert provenance["execution_mode"] == "full"
        assert provenance["reused_on_demand_window_count"] == (1 if not rejected and algorithm_id in {"psd", "stft"} else 0)
        assert hashlib.sha256((runs.base.artifacts.root / artifact.relative_path).read_bytes()).hexdigest() == artifact.sha256
        assert source_hashes(root) == before
    finally:
        release.set()
        pool.shutdown(wait=True, cancel_futures=True)
        windows.close()
        client.close()


@pytest.mark.parametrize("algorithm_id", ALGORITHM_IDS)
def test_combined_background_cancellation_preserves_ready_preview(tmp_path, monkeypatch, algorithm_id):
    services, config = setup_strategy(tmp_path, algorithm_id)
    client, recording, windows, runs, _ = services
    entered, release = threading.Event(), threading.Event()
    original = RunExecutionContext.window_completed
    pool = ThreadPoolExecutor(max_workers=1)
    try:
        request, expected, arrays = uncached_full(runs, recording, config)

        def overlapping_window_completed(control):
            original(control)
            if control.completed == 1:
                entered.set()
                assert release.wait(20)

        monkeypatch.setattr(RunExecutionContext, "window_completed", overlapping_window_completed)
        queued = runs.enqueue(request)
        future = pool.submit(runs.process_next)
        assert entered.wait(10)
        ensured = ensure_preview(client, recording, config)
        preview = wait_window(client, ensured["window_id"])
        compare_preview(preview, expected, arrays)
        runs.cancel(queued.run_id)
        release.set()
        cancelled = future.result(timeout=30)
        assert cancelled.status.value == "cancelled"
        assert not runs.list_artifacts(cancelled.run_id)
        assert windows.get(ensured["window_id"])["result"] == preview["result"]
        assert windows.get(ensured["window_id"])["state"] == "ready"
    finally:
        release.set()
        pool.shutdown(wait=True, cancel_futures=True)
        windows.close()
        client.close()
