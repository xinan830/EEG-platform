"""Settings maintenance must remove cache identities without touching science history."""

import asyncio
import sqlite3
import threading
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import pytest

from app.api.cache_maintenance import router
from app.api.live_filters import router as live_filters_router
from app.api.live_recordings import router as live_recordings_router
from app.core.cache_maintenance_gate import CacheMaintenanceGate, CacheMaintenanceMiddleware
from app.services.cache_maintenance import CacheMaintenanceService
from app.services.live_filter import LiveFilterService
from app.services.live_recording_access import LiveRecordingAccessService
from on_demand_fixtures import configure_api, psd_config, source_hashes
from test_on_demand_psd_api import wait_window


ENDPOINT = "/api/maintenance/cache/clear"
DYNAMIC = {"dynamic_analysis_sessions", "dynamic_analysis_windows",
           "dynamic_analysis_attempts", "dynamic_analysis_subscriptions"}


@pytest.fixture
def setup(tmp_path):
    client, recording, windows, runs, source = configure_api(tmp_path)
    gate = CacheMaintenanceGate()
    service = CacheMaintenanceService(windows, runs._offline_window_reuse.artifacts)
    client.app.state.cache_maintenance_service = service
    client.app.add_middleware(CacheMaintenanceMiddleware, gate=gate)
    client.app.include_router(router)
    try:
        yield client, recording, windows, runs, source, service, gate
    finally:
        windows.close(wait=True)
        client.close()


def ready_window(client, recording):
    result = client.post("/api/analysis-windows/ensure", json={
        "recording_id": recording.id, "algorithm_id": "psd", "cursor_sample": 1000,
        "config": psd_config(recording),
    })
    assert result.status_code in {200, 202}, result.text
    return wait_window(client, result.json()["window_id"])


def protected_rows(database):
    with sqlite3.connect(database) as connection:
        names = [r[0] for r in connection.execute("SELECT name FROM sqlite_master WHERE type='table'")]
        return {t: connection.execute(f'SELECT * FROM "{t}" ORDER BY rowid').fetchall()
                for t in names if t not in DYNAMIC and t != "audit_events"}


@pytest.mark.parametrize("missing_files", [False, True])
def test_clear_preserves_sources_history_schema_and_can_recompute(setup, missing_files):
    client, recording, windows, runs, source, service, _ = setup
    old = ready_window(client, recording)
    before = protected_rows(windows.repository.database_path)
    hashes = source_hashes(source)
    historical = runs.base.artifacts.root / "historical-result.npz"
    historical.write_bytes(b"historical scientific artifact")
    with sqlite3.connect(windows.repository.database_path) as connection:
        schema = connection.execute("SELECT type,name,sql FROM sqlite_master ORDER BY name").fetchall()
    if missing_files:
        for path in windows.artifacts_dir.iterdir():
            path.unlink()
    response = client.post(ENDPOINT, json={}, headers={"X-Request-ID": "settings-clear"})
    assert response.status_code == 200, response.text
    value = response.json()
    assert value["status"] == "cleared" and value["deleted_windows"] == 1
    assert value["deleted_files"] == (0 if missing_files else 2)
    assert (value["freed_bytes"] == 0) is missing_files
    assert not list(windows.artifacts_dir.iterdir())
    assert protected_rows(windows.repository.database_path) == before
    assert source_hashes(source) == hashes
    assert historical.read_bytes() == b"historical scientific artifact"
    assert not windows._artifact_store._read_cache
    assert not service.reuse_artifacts._read_cache
    assert windows._artifact_store._directory_usage[windows.artifacts_dir.resolve()] == 0
    with sqlite3.connect(windows.repository.database_path) as connection:
        assert all(connection.execute(f'SELECT COUNT(*) FROM {t}').fetchone()[0] == 0 for t in DYNAMIC)
        assert connection.execute("PRAGMA quick_check").fetchone()[0] == "ok"
        assert not connection.execute("PRAGMA foreign_key_check").fetchall()
        assert connection.execute("SELECT type,name,sql FROM sqlite_master ORDER BY name").fetchall() == schema
        assert connection.execute("SELECT COUNT(*) FROM audit_events WHERE action='cache.clear' AND request_id='settings-clear'").fetchone()[0] == 1
    assert client.post(ENDPOINT, json={}).json()["deleted_windows"] == 0
    new = ready_window(client, recording)
    assert new["window_id"] != old["window_id"]
    assert new["result"]["arrays"] == old["result"]["arrays"]


def test_pending_executor_refuses_cleanup_without_mutating_files(setup):
    client, recording, windows, _, _, _, _ = setup
    old = ready_window(client, recording)
    windows._pending["injected-finishing-job"] = object()
    try:
        response = client.post(ENDPOINT, json={})
        assert response.status_code == 409 and response.json()["code"] == "CACHE_BUSY"
        assert windows.get(old["window_id"])["state"] == "ready"
        assert len(list(windows.artifacts_dir.iterdir())) == 2
    finally:
        windows._pending.pop("injected-finishing-job")


def test_file_failure_is_partial_and_retry_removes_orphans(setup, monkeypatch):
    client, recording, windows, _, _, service, _ = setup
    ready_window(client, recording)
    original = Path.unlink
    with monkeypatch.context() as patch:
        def denied(path, *args, **kwargs):
            if path.parent == windows.artifacts_dir:
                raise PermissionError("injected locked artifact")
            return original(path, *args, **kwargs)
        patch.setattr(Path, "unlink", denied)
        response = client.post(ENDPOINT, json={}, headers={"X-Request-ID": "partial-clear"})
        assert response.status_code == 500 and response.json()["code"] == "CACHE_FILES_REMAIN"
        assert response.json()["request_id"] == "partial-clear"
        assert not windows.repository.published_artifact_ids()
        assert len(list(windows.artifacts_dir.iterdir())) == 2
        assert not service.reuse_artifacts._read_cache
    assert client.post(ENDPOINT, json={}).json()["deleted_files"] == 2


def test_no_path_argument_is_accepted(setup):
    client, *_ = setup
    response = client.post(ENDPOINT, json={"path": "recordings", "sql": "DELETE FROM recordings"})
    assert response.status_code == 422


def test_queued_attempt_and_full_run_block_cleanup(setup):
    client, recording, windows, runs, _, _, _ = setup
    old = ready_window(client, recording)
    queued = windows.repository.ensure_window(old["session_id"], start_sample=1000, end_sample=2000,
                                              start_s=5, end_s=10)
    attempt = windows.repository.create_attempt(queued.window_id, "queued-cache-test")
    response = client.post(ENDPOINT, json={})
    assert response.status_code == 409 and response.json()["code"] == "CACHE_BUSY"
    assert windows.repository.get_window(old["window_id"]).state == "ready"
    with windows.repository._connect() as connection:
        connection.execute("UPDATE dynamic_analysis_attempts SET state='cancelled' WHERE attempt_id=?", (attempt.attempt_id,))
    created = client.post("/api/runs", json={"recording_id": recording.id,
        "analysis_type": "official_algorithm", "config": psd_config(recording)})
    assert created.status_code == 202, created.text
    response = client.post(ENDPOINT, json={})
    assert response.status_code == 409 and response.json()["code"] == "CACHE_BUSY"
    assert runs.process_next().status.value == "completed"
    before = protected_rows(windows.repository.database_path)
    artifacts = {p.name: p.read_bytes() for p in runs.base.artifacts.root.glob("*.npz")}
    assert client.post(ENDPOINT, json={}).status_code == 200
    assert protected_rows(windows.repository.database_path) == before
    assert {p.name: p.read_bytes() for p in runs.base.artifacts.root.glob("*.npz")} == artifacts


def test_missing_directory_is_recreated_and_unknown_content_is_preserved(setup):
    client, _, windows, _, _, _, _ = setup
    windows.artifacts_dir.rmdir()
    assert client.post(ENDPOINT, json={}).status_code == 200
    assert windows.artifacts_dir.is_dir()
    protected = windows.artifacts_dir / "unexpected-recording.bin"
    protected.write_bytes(b"do not guess ownership")
    response = client.post(ENDPOINT, json={})
    assert response.status_code == 409 and response.json()["code"] == "CACHE_PATH_UNSAFE"
    assert protected.read_bytes() == b"do not guess ownership"


def test_linked_cache_file_is_refused(setup, tmp_path):
    client, _, windows, _, _, _, _ = setup
    source = tmp_path / "protected.json"
    source.write_text("raw metadata", encoding="utf-8")
    linked = windows.artifacts_dir / "link.json"
    try:
        linked.symlink_to(source)
    except OSError:
        pytest.skip("Windows symbolic links are unavailable without the required privilege")
    response = client.post(ENDPOINT, json={})
    assert response.status_code == 409 and response.json()["code"] == "CACHE_PATH_UNSAFE"
    assert source.read_text(encoding="utf-8") == "raw metadata"


def test_middleware_holds_gate_until_last_body_and_releases_exceptions():
    async def run():
        gate = CacheMaintenanceGate()
        entered, finish = asyncio.Event(), asyncio.Event()
        async def app(scope, receive, send):
            entered.set()
            await finish.wait()
            raise RuntimeError("response failed")
        async def receive():
            return {"type": "http.disconnect"}
        async def send(message):
            pass
        middleware = CacheMaintenanceMiddleware(app, gate)
        running = asyncio.create_task(middleware({"type": "http", "path": "/api/results", "method": "GET"}, receive, send))
        await entered.wait()
        assert not gate.try_enter(exclusive=True)
        finish.set()
        with pytest.raises(RuntimeError, match="response failed"):
            await running
        assert gate.try_enter(exclusive=True)
        gate.leave(exclusive=True)
    asyncio.run(run())


def test_gate_keeps_full_response_exclusive_and_releases_after_failure():
    gate = CacheMaintenanceGate()
    assert gate.try_enter(exclusive=False)
    assert not gate.try_enter(exclusive=True)
    gate.leave(exclusive=False)
    assert gate.try_enter(exclusive=True)
    assert not gate.try_enter(exclusive=True)
    assert not gate.try_enter(exclusive=False)
    gate.leave(exclusive=True)
    assert gate.try_enter(exclusive=False)
    gate.leave(exclusive=False)


def test_requests_are_rejected_while_clear_owns_gate(setup, monkeypatch):
    client, _, _, _, _, service, _ = setup
    entered, release = threading.Event(), threading.Event()
    original = service.clear
    def blocked():
        entered.set()
        assert release.wait(5)
        return original()
    monkeypatch.setattr(service, "clear", blocked)
    with ThreadPoolExecutor(max_workers=1) as pool:
        future = pool.submit(client.post, ENDPOINT, json={})
        try:
            assert entered.wait(5)
            response = client.get("/api/analysis-windows/absent", headers={"X-Request-ID": "blocked-read"})
            assert response.status_code == 409 and response.json()["code"] == "CACHE_BUSY"
            assert response.json()["request_id"] == "blocked-read"
            assert client.post(ENDPOINT, json={}).status_code == 409
        finally:
            release.set()
        assert future.result().status_code == 200
    assert client.get("/api/analysis-windows/absent").status_code == 404


def test_live_batches_continue_during_maintenance_and_keep_their_state(setup):
    client, _, _, _, _, _, gate = setup
    client.app.state.live_filter_service = LiveFilterService()
    client.app.state.live_recording_access_service = LiveRecordingAccessService()
    client.app.include_router(live_filters_router)
    client.app.include_router(live_recordings_router)
    assert client.post("/api/live-filters/sessions", json={
        "session_id": "live", "sampling_rate_hz": 200, "channel_count": 1,
        "eeg_channel_indexes": [0], "low_cut_hz": 1, "high_cut_hz": 40,
    }).status_code == 201
    assert client.post("/api/live-recordings/sessions", json={
        "session_id": "live", "stream_revision": "1", "schema_revision": "1",
        "sampling_rate_hz": 200, "channel_names": ["F3"], "channel_units": ["V"],
    }).status_code == 201
    assert gate.try_enter(exclusive=True)
    try:
        filtered = client.post("/api/live-filters/sessions/live/batches",
                               json={"sample_count": 2, "values_v": [1e-6, 2e-6]})
        assert filtered.status_code == 200 and len(filtered.json()["values_v"]) == 2
        appended = client.post("/api/live-recordings/sessions/live/batches/binary",
                               params={"first_sample_counter": 100, "sample_count": 2}, content=bytes(16))
        assert appended.status_code == 200
    finally:
        gate.leave(exclusive=True)
    assert client.post(ENDPOINT, json={}).status_code == 200
    live_range = client.get("/api/live-recordings/sessions/live/range")
    assert live_range.status_code == 200 and live_range.json()["range"]["high_watermark_sample"] == 101
    assert client.post("/api/live-filters/sessions/live/batches",
                       json={"sample_count": 1, "values_v": [3e-6]}).status_code == 200
