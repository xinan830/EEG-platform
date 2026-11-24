import logging
import threading
from concurrent.futures import ThreadPoolExecutor
from types import SimpleNamespace

import pytest

from app.core import performance_trace as diagnostics
from app.persistence import connect_database
from app.services.on_demand_dynamic import DynamicWindowService


def test_timing_scopes_count_connections_and_reset_after_failures(tmp_path, caplog):
    caplog.set_level(logging.INFO)
    with diagnostics.performance_trace("test_window_perf", request_id="outer") as outer:
        with pytest.raises(ValueError):
            with diagnostics.performance_trace("test_window_perf", request_id="inner") as inner:
                with diagnostics.measure_performance("metadata"):
                    with connect_database(tmp_path / "test.sqlite3") as connection:
                        assert connection.execute("SELECT 1").fetchone()[0] == 1
                    raise ValueError("preserve failure")
        assert diagnostics.current_performance_trace() is outer
        with ThreadPoolExecutor(1) as executor:
            assert executor.submit(diagnostics.current_performance_trace).result() is None
    assert diagnostics.current_performance_trace() is None
    assert inner.metrics["database_connect_calls"] == 1
    assert inner.metrics["metadata_calls"] == 1
    assert outer.metrics == {}
    assert any("request_id=inner outcome=error" in record.getMessage() for record in caplog.records)


def test_admission_distinguishes_lock_wait_from_work(monkeypatch, caplog):
    caplog.set_level(logging.INFO)
    clock = [0.0]
    monkeypatch.setattr(diagnostics, "perf_counter", lambda: clock[0])
    monkeypatch.setattr("app.services.on_demand_dynamic.time.perf_counter", lambda: clock[0])

    class Lock:
        def __enter__(self):
            clock[0] += 0.025

        def __exit__(self, *_):
            pass

    def ensure_locked(*_, **__):
        clock[0] += 0.010
        return {"window_id": "saved-window", "state": "ready"}

    def prepare(*_, **__):
        clock[0] += 0.100
        return SimpleNamespace()

    service = SimpleNamespace(_lock=Lock(), _stop=threading.Event(), _ensure_locked=ensure_locked,
                              _planner=SimpleNamespace(prepare=prepare))
    payload = SimpleNamespace(request_id="warm-request", algorithm_id="rbp", recording_id="recording", cursor_sample=79000)
    assert DynamicWindowService._ensure(service, payload)["window_id"] == "saved-window"
    message = next(record.getMessage() for record in caplog.records if "dynamic_window_admission_perf" in record.getMessage())
    assert "window_id=saved-window" in message
    assert "lock_wait_ms=25.000" in message
    assert "lock_held_ms=10.000" in message
    assert "total_ms=135.000" in message
