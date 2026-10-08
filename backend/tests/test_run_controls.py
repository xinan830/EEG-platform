from concurrent.futures import ThreadPoolExecutor
import threading

import pytest

from app.algorithm_runtime.windows import build_dynamic_analysis_frames, controlled_frames
from app.models.run import RunCreateRequest, RunStatus
from app.services.run_execution_context import RunCancelledError, RunExecutionContext
from app.services.run_worker import RunWorker
from tests.test_run_queue import _queue, _request


def test_concurrent_config_and_explicit_key_requests_are_atomic(tmp_path):
    queue, recording_id = _queue(tmp_path)
    for key in (None, "one-attempt"):
        with ThreadPoolExecutor(max_workers=8) as pool:
            runs = list(pool.map(lambda _: queue.enqueue(_request(recording_id, key)), range(16)))
        assert len({run.run_id for run in runs}) == 1
        queue.cancel(runs[0].run_id)
    assert len(queue.list()) == 2


def test_terminal_unkeyed_run_does_not_prevent_new_attempt(tmp_path):
    queue, recording_id = _queue(tmp_path)
    first = queue.enqueue(_request(recording_id))
    queue.cancel(first.run_id)
    second = queue.enqueue(_request(recording_id))
    assert second.run_id != first.run_id
    assert second.status is RunStatus.QUEUED


def test_repeated_retry_requests_merge_into_one_active_attempt(tmp_path):
    queue, recording_id = _queue(tmp_path)
    first = queue.enqueue(_request(recording_id))
    queue.cancel(first.run_id)
    with ThreadPoolExecutor(max_workers=4) as pool:
        retried = list(pool.map(lambda _: queue.retry(first.run_id), range(8)))
    assert len({run.run_id for run in retried}) == 1
    assert all(run.parent_run_id == first.run_id for run in retried)


def test_explicit_key_rejects_changed_configuration_and_reuses_terminal_run(tmp_path):
    queue, recording_id = _queue(tmp_path)
    first = queue.enqueue(_request(recording_id, "key"))
    queue.cancel(first.run_id)
    assert queue.enqueue(_request(recording_id, "key")).run_id == first.run_id
    changed = _request(recording_id, "key")
    changed.config["time"]["start_s"] = 1
    with pytest.raises(ValueError, match="幂等键"):
        queue.enqueue(changed)


def test_unhandled_window_error_does_not_count_or_mask_error():
    progress = []
    context = RunExecutionContext(lambda: False, progress.append, 2)
    frames = build_dynamic_analysis_frames(0, 6, duration_s=6, window_s=5, step_s=1, allow_warmup=False)
    with pytest.raises(ValueError, match="original"):
        for _ in controlled_frames(frames, context):
            raise ValueError("original")
    assert progress == []


def test_cancellation_after_first_window_preserves_progress_and_no_artifact(tmp_path, monkeypatch):
    queue, recording_id = _queue(tmp_path)
    request = RunCreateRequest(recording_id=recording_id, analysis_type="official_algorithm", config={
        "algorithm_id": "peak_frequency", "channel": "F3", "mode": "dynamic",
        "time": {"start_s": 0, "end_s": 10}, "dynamic_window_s": 5, "refresh_step_s": 1,
        "low_hz": 1, "high_hz": 30,
    })
    run = queue.enqueue(request)
    original = queue.recordings.load_spectrum
    calls = []
    def cancel_after_read(*args, **kwargs):
        result = original(*args, **kwargs)
        calls.append(1)
        queue.cancel(run.run_id)
        return result
    monkeypatch.setattr(queue.recordings, "load_spectrum", cancel_after_read)
    final = queue.process_next()
    assert final.status is RunStatus.CANCELLED
    assert final.progress_completed == 1
    assert final.progress_total == 7
    assert len(calls) == 1
    assert queue.list_artifacts(run.run_id) == []


def test_cache_reuse_has_full_progress(tmp_path):
    queue, recording_id = _queue(tmp_path)
    first = queue.enqueue(_request(recording_id))
    queue.process_next()
    second = queue.enqueue(_request(recording_id))
    completed = queue.process_next()
    assert second.run_id != first.run_id
    assert completed.reused_from_run_id == first.run_id
    assert completed.progress_completed == completed.progress_total == 1


def test_lane_worker_isolation_and_shutdown():
    background_started = threading.Event()
    release = threading.Event()
    interactive_done = threading.Event()
    counts = {"interactive": 0, "batch": 0}
    class Queue:
        def process_next(self, lane):
            counts[lane] += 1
            if lane == "batch" and counts[lane] == 1:
                background_started.set()
                assert release.wait(5)
            if lane == "interactive" and background_started.wait(2):
                interactive_done.set()
            return None
    worker = RunWorker(Queue())
    worker.start()
    try:
        assert background_started.wait(2)
        assert interactive_done.wait(2)
        assert len(worker._threads) == 2
        worker.start()
        assert len(worker._threads) == 2
    finally:
        release.set()
        worker.stop()
    assert not any(thread.is_alive() for thread in worker._threads)


def test_dynamic_and_static_claim_distinct_lanes(tmp_path):
    queue, recording_id = _queue(tmp_path)
    dynamic = queue.enqueue(RunCreateRequest(recording_id=recording_id, analysis_type="official_algorithm", config={
        "algorithm_id": "peak_frequency", "channel": "F3", "mode": "dynamic",
        "time": {"start_s": 0, "end_s": 10}, "dynamic_window_s": 5, "refresh_step_s": 1,
        "low_hz": 1, "high_hz": 30,
    }))
    static = queue.enqueue(_request(recording_id))
    assert queue.repository.claim_next_queued("interactive").run_id == static.run_id
    assert queue.repository.claim_next_queued("batch").run_id == dynamic.run_id


def test_merged_explicit_keys_survive_terminal_status_and_restart(tmp_path):
    queue, recording_id = _queue(tmp_path)
    first = queue.enqueue(_request(recording_id, "first"))
    assert queue.enqueue(_request(recording_id, "alias")).run_id == first.run_id
    queue.cancel(first.run_id)
    from app.services.run_queue import PersistentRunQueue
    restored = PersistentRunQueue(queue.recordings, queue.repository.database_path, tmp_path / "artifacts")
    for key in ("first", "alias"):
        assert restored.enqueue(_request(recording_id, key)).run_id == first.run_id
        changed = _request(recording_id, key)
        changed.config["time"]["start_s"] = 1
        with pytest.raises(ValueError, match="幂等键"):
            restored.enqueue(changed)


def test_cancel_during_artifact_write_wins_and_removes_unpublished_output(tmp_path, monkeypatch):
    queue, recording_id = _queue(tmp_path)
    run = queue.enqueue(_request(recording_id))
    original = queue.base.artifacts.write_npz
    def cancel_before_commit(*args, **kwargs):
        artifact = original(*args, **kwargs)
        queue.cancel(run.run_id)
        return artifact
    monkeypatch.setattr(queue.base.artifacts, "write_npz", cancel_before_commit)
    final = queue.process_next()
    assert final.status is RunStatus.CANCELLED
    assert final.result_summary is None
    assert queue.list_artifacts(run.run_id) == []
    assert not list((tmp_path / "artifacts").rglob("*.npz"))


def test_cancelled_queued_job_cannot_be_claimed(tmp_path):
    queue, recording_id = _queue(tmp_path)
    run = queue.enqueue(_request(recording_id))
    queue.cancel(run.run_id)
    assert queue.repository.claim_next_queued() is None


def test_worker_restores_native_limits_after_delayed_exit(monkeypatch):
    import app.services.run_worker as module
    entered, release, restored = threading.Event(), threading.Event(), threading.Event()
    class Limits:
        def restore_original_limits(self):
            restored.set()
    monkeypatch.setattr(module, "threadpool_limits", lambda **_kwargs: Limits())
    class Queue:
        def process_next(self, lane):
            if lane == "batch":
                entered.set()
                release.wait(5)
            return None
    worker = RunWorker(Queue())
    worker.start()
    try:
        assert entered.wait(2)
        worker._stop.set()
        worker._wake.set()
        assert not restored.is_set()
    finally:
        release.set()
        worker.stop()
    assert restored.is_set()
