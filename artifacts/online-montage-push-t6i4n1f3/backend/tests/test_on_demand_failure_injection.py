import json
import sqlite3
import threading
from concurrent.futures import ThreadPoolExecutor

import pytest

from app.algorithm_runtime.on_demand import DynamicWindowIdentity, DynamicWindowRequest, DynamicWindowResult
from on_demand_fixtures import configure_api, psd_config
from test_on_demand_psd_api import wait_window


def request(recording, **extra):
    return DynamicWindowRequest(recording_id=recording.id, algorithm_id="psd", cursor_sample=1000,
                                config=psd_config(recording), request_id="request", **extra)


def test_partial_publish_is_unreadable_and_files_are_cleaned(tmp_path, monkeypatch):
    client, recording, windows, _, _ = configure_api(tmp_path)
    try:
        def fail_metadata_publish(source, target):
            if str(target).endswith(".json"):
                raise OSError("injected interruption after payload rename")
            original_replace(source, target)

        import app.services.dynamic_window_artifacts as module
        original_replace = module.os.replace
        monkeypatch.setattr(module.os, "replace", fail_metadata_publish)
        response = windows.ensure(request(recording))
        terminal = wait_window(client, response["window_id"])
        assert terminal["state"] == "failed"
        assert terminal["result"] is None
        assert not list(windows.artifacts_dir.iterdir())
        assert windows.repository.get_window(response["window_id"]).result_artifact_id is None
    finally:
        windows.close()
        client.close()


@pytest.mark.parametrize("damage", ["metadata", "missing_payload", "payload"])
def test_ready_corruption_returns_structured_conflict_without_recomputing(tmp_path, damage):
    client, recording, windows, _, _ = configure_api(tmp_path)
    try:
        response = windows.ensure(request(recording))
        ready = wait_window(client, response["window_id"])
        assert ready["state"] == "ready"
        window = windows.repository.get_window(response["window_id"])
        artifact_id = window.result_artifact_id
        if damage == "metadata":
            path = windows.artifacts_dir / f"{artifact_id}.json"
            value = json.loads(path.read_text(encoding="utf-8"))
            value["array_units"]["psd"] = "wrong-unit"
            path.write_text(json.dumps(value), encoding="utf-8")
        elif damage == "missing_payload":
            (windows.artifacts_dir / f"{artifact_id}.npz").unlink()
        else:
            (windows.artifacts_dir / f"{artifact_id}.npz").write_bytes(b"broken")
        conflict = client.get(f"/api/analysis-windows/{window.window_id}", headers={"X-Request-ID": "corrupt-read"})
        assert conflict.status_code == 409
        assert conflict.json()["code"] == "WINDOW_RESULT_CORRUPT"
        assert conflict.json()["request_id"] == "corrupt-read"
        ensure_conflict = client.post("/api/analysis-windows/ensure", json=request(recording).model_dump(mode="json"))
        assert ensure_conflict.status_code == 409
        assert ensure_conflict.json()["code"] == "WINDOW_RESULT_CORRUPT"
        assert windows.repository.get_window(window.window_id) == window
        with sqlite3.connect(windows.repository.database_path) as connection:
            assert connection.execute("SELECT COUNT(*) FROM dynamic_analysis_attempts WHERE window_id=?", (window.window_id,)).fetchone()[0] == 1
    finally:
        windows.close()
        client.close()


def test_stale_and_repeated_publish_cannot_overwrite_window(tmp_path):
    client, recording, windows, _, _ = configure_api(tmp_path)
    try:
        response = windows.ensure(request(recording))
        ready = wait_window(client, response["window_id"])
        original = windows.repository.get_window(response["window_id"])
        with sqlite3.connect(windows.repository.database_path) as connection:
            attempt_id, worker, token = connection.execute(
                "SELECT attempt_id, worker_id, lease_token FROM dynamic_analysis_attempts WHERE window_id=?", (original.window_id,),
            ).fetchone()
        with pytest.raises(ValueError, match="stale"):
            windows.repository.update_attempt_window(attempt_id, worker, token, "ready", artifact_id="repeat")
        with pytest.raises(ValueError, match="stale"):
            windows.repository.update_attempt_window(attempt_id, worker, "invalid-token", "failed", failure={"code": "bad", "message": "bad"})
        assert windows.repository.get_window(original.window_id) == original
        assert windows.get(original.window_id)["result"] == ready["result"]
    finally:
        windows.close()
        client.close()


def test_concurrent_retry_resolves_one_successor_without_reopening_old_window(tmp_path):
    client, recording, windows, _, _ = configure_api(tmp_path)
    try:
        # At cursor zero the backend plans an insufficient window. Create a
        # cancelled range directly to isolate the retry transaction race.
        response = windows.ensure(request(recording).model_copy(update={"cursor_sample": 0}))
        session = response["session_id"]
        original = windows.repository.ensure_window(session, start_sample=1000, end_sample=2000, start_s=5, end_s=10)
        cancelled = windows.repository.transition_window(original.window_id, "cancelled")

        def retry(_):
            return windows.repository.retry_window(session, start_sample=1000, end_sample=2000,
                                                   start_s=5, end_s=10, retry_of_window_id=original.window_id)

        with ThreadPoolExecutor(max_workers=4) as pool:
            successors = list(pool.map(retry, range(12)))
        assert len({item.window_id for item in successors}) == 1
        assert successors[0].generation == 1
        assert windows.repository.get_window(original.window_id) == cancelled
    finally:
        windows.close()
        client.close()


@pytest.mark.parametrize("changes", [{"cursor_sample": 0.5}, {"cursor_sample": -1}, {"start_sample": 0}, {"arrays": [1, 2]}, {"retry": True}])
def test_invalid_request_has_stable_code_and_creates_no_work(tmp_path, changes):
    client, recording, windows, _, _ = configure_api(tmp_path)
    try:
        payload = request(recording).model_dump(mode="json") | changes
        response = client.post("/api/analysis-windows/ensure", json=payload, headers={"X-Request-ID": "invalid"})
        assert response.status_code == 422
        assert response.json()["code"] in {"WINDOW_CURSOR_INVALID", "WINDOW_REQUEST_INVALID"}
        assert response.json()["request_id"] == "invalid"
        with sqlite3.connect(windows.repository.database_path) as connection:
            for table in ("dynamic_analysis_sessions", "dynamic_analysis_windows", "dynamic_analysis_attempts", "dynamic_analysis_subscriptions"):
                assert connection.execute(f"SELECT COUNT(*) FROM {table}").fetchone()[0] == 0
    finally:
        windows.close()
        client.close()
