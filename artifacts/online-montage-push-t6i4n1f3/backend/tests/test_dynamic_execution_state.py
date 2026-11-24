"""Live database fences remain visible without reconnecting at every checkpoint."""

import sqlite3
import threading

import pytest

from app.core.performance_trace import performance_trace
from app.persistence.repositories.dynamic_execution_state import DynamicExecutionStateReader
from test_on_demand_admission import admission


@pytest.fixture
def running(admission):
    service, request, started, _ = admission
    response = service.ensure(request)
    assert started.wait(2)
    attempt = service.repository.get_active_attempt(response["window_id"])
    return service, response, attempt


def test_many_checks_share_one_connection_and_dispose_it(running, caplog):
    service, response, attempt = running
    caplog.set_level("INFO")
    with performance_trace("reader_test") as trace:
        reader = DynamicExecutionStateReader(service.repository.database_path, response["window_id"],
                                             attempt.attempt_id, attempt.lease_token)
        try:
            for _ in range(100):
                assert reader.read() == "active"
        finally:
            reader.close()
    assert trace.metrics["database_connect_calls"] == 1
    with pytest.raises(sqlite3.ProgrammingError, match="closed"):
        reader.read()


@pytest.mark.parametrize("damage", ["cancelled", "token", "expired", "attempt_state", "attempt_deleted"])
def test_next_check_observes_external_cancellation_or_lease_change(running, damage):
    service, response, attempt = running
    reader = DynamicExecutionStateReader(service.repository.database_path, response["window_id"],
                                         attempt.attempt_id, attempt.lease_token)
    try:
        assert reader.read() == "active"
        with sqlite3.connect(service.repository.database_path) as connection:
            if damage == "cancelled":
                connection.execute("UPDATE dynamic_analysis_windows SET state='cancelled' WHERE window_id=?",
                                   (response["window_id"],))
            elif damage == "attempt_deleted":
                connection.execute("DELETE FROM dynamic_analysis_attempts WHERE attempt_id=?", (attempt.attempt_id,))
            else:
                column, value = {"token": ("lease_token", "another-worker"),
                                 "expired": ("lease_expires_at", "2000-01-01T00:00:00+00:00"),
                                 "attempt_state": ("state", "expired")}[damage]
                connection.execute(f"UPDATE dynamic_analysis_attempts SET {column}=? WHERE attempt_id=?",
                                   (value, attempt.attempt_id))
        assert reader.read() == ("cancelled" if damage == "cancelled" else "lease_lost")
    finally:
        reader.close()


def test_reader_requires_attempt_to_belong_to_its_window(running):
    service, response, attempt = running
    other = service.repository.ensure_window(response["session_id"], start_sample=1000,
                                            end_sample=2000, start_s=5, end_s=10)
    reader = DynamicExecutionStateReader(service.repository.database_path, other.window_id,
                                         attempt.attempt_id, attempt.lease_token)
    try:
        assert reader.read() == "lease_lost"
    finally:
        reader.close()


def test_worker_closes_reader_after_execution(admission, monkeypatch):
    import app.services.on_demand_worker as worker

    service, request, started, release = admission
    closed = threading.Event()

    class ObservedReader(DynamicExecutionStateReader):
        def close(self):
            super().close()
            closed.set()

    monkeypatch.setattr(worker, "DynamicExecutionStateReader", ObservedReader)
    service.ensure(request)
    assert started.wait(2)
    release.set()
    assert closed.wait(3)
