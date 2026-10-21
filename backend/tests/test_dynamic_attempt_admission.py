"""Producer-scoped admission preserves shared ownership and terminal release."""

import pytest

from app.algorithm_runtime.errors import DynamicWindowCapacityExceededError
from app.persistence.repositories.dynamic_window import DynamicWindowRepository
from test_dynamic_window_repository import _identity, _recording


@pytest.fixture
def windows(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    _recording(database)
    repository = DynamicWindowRepository(database)
    session = repository.ensure_session(_identity(), configuration={})
    rows = [repository.ensure_window(
        session.session_id, start_sample=index * 500, end_sample=(index + 1) * 500,
        start_s=index * 5, end_s=(index + 1) * 5,
    ) for index in range(3)]
    return repository, rows


def test_full_window_reuse_precedes_saturated_interactive_admission(windows):
    repository, rows = windows
    repository.create_attempt(rows[0].window_id, "interactive")
    full = repository.create_attempt(
        rows[1].window_id, "full", producer_mode="full", producer_owner_id="run", priority=10,
    )
    limits = dict(max_active_attempts=1, max_project_attempts=1, max_recording_attempts=1)
    reused = repository.create_attempt(rows[1].window_id, "join", priority=100, **limits)
    assert reused.attempt_id == full.attempt_id
    assert reused.producer_mode == "full"
    assert reused.producer_owner_id == "run"
    assert reused.priority == 100
    claimed = repository.claim_attempt(rows[1].window_id, "full-worker", "2999-01-01T00:00:00+00:00")
    running = repository.create_attempt(rows[1].window_id, "join-running", priority=200, **limits)
    assert running == claimed
    with pytest.raises(DynamicWindowCapacityExceededError):
        repository.create_attempt(rows[2].window_id, "overflow", **limits)


@pytest.mark.parametrize("state", ["succeeded", "failed", "cancelled"])
def test_terminal_attempt_releases_scoped_and_queue_capacity(windows, state):
    repository, rows = windows
    limits = dict(max_active_attempts=1, max_project_attempts=1, max_recording_attempts=1)
    first = repository.create_attempt(rows[0].window_id, "first", **limits)
    with pytest.raises(DynamicWindowCapacityExceededError):
        repository.create_attempt(rows[1].window_id, "blocked", **limits)
    claimed = repository.claim_attempt(rows[0].window_id, "worker", "2999-01-01T00:00:00+00:00")
    repository.finish_attempt(
        first.attempt_id, "worker", state, lease_token=claimed.lease_token,
        error={"code": "TEST_FAILURE"} if state == "failed" else None,
    )
    admitted = repository.create_attempt(rows[1].window_id, "after-terminal", **limits)
    assert admitted.state == "queued"


def test_full_backlog_does_not_fill_global_interactive_queue(windows):
    repository, rows = windows
    for row in rows[:2]:
        repository.create_attempt(row.window_id, row.window_id, producer_mode="full")
    admitted = repository.create_attempt(rows[2].window_id, "interactive", max_active_attempts=1)
    assert admitted.producer_mode == "on_demand"
