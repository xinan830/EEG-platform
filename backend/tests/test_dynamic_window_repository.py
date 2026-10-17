import sqlite3
from concurrent.futures import ThreadPoolExecutor

import pytest

from app.algorithm_runtime.on_demand import DynamicWindowIdentity
from app.persistence.repositories.dynamic_window import DynamicWindowRepository
from app.persistence.migrations import migrate_database


def _identity(*, project_scope=None, config="config"):
    return DynamicWindowIdentity(
        project_scope=project_scope,
        recording_id="recording-1",
        source_sha256="source-1",
        algorithm_id="psd",
        definition_id="official-psd",
        definition_version="1",
        scientific_version="science-1",
        implementation_identity="impl-1",
        configuration_sha256=config,
        window_policy_version="window-v1",
        start_sample=0,
        end_sample=500,
    )


def _recording(database):
    migrate_database(database)
    with sqlite3.connect(database) as connection:
        connection.execute(
            "INSERT INTO recordings(id, original_name, stored_name, extension, created_at) VALUES (?, ?, ?, ?, ?)",
            ("recording-1", "recording", "recording", ".edf", "now"),
        )


def test_session_and_window_are_idempotent_and_lifecycle_is_enforced(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    _recording(database)
    repository = DynamicWindowRepository(database)
    session = repository.ensure_session(_identity(), configuration={"channel": "F3"})
    same_session = repository.ensure_session(_identity(), configuration={"channel": "F3"})
    assert same_session == session

    window = repository.ensure_window(session.session_id, start_sample=0, end_sample=500, start_s=0, end_s=5)
    assert repository.ensure_window(session.session_id, start_sample=0, end_sample=500, start_s=0, end_s=5) == window
    computing = repository.transition_window(window.window_id, "computing_window")
    with pytest.raises(ValueError, match="atomic artifact publication"):
        repository.transition_window(computing.window_id, "ready")
    rejected = repository.transition_window(
        computing.window_id, "rejected", quality="gate_failed",
        failure={"code": "quality_gate_failed", "message": "Rejected by test quality gate"},
    )
    assert rejected.completed_at is not None
    with pytest.raises(ValueError, match="invalid dynamic window state transition"):
        repository.transition_window(rejected.window_id, "queued")


def test_subscriber_cancellation_is_isolated(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    _recording(database)
    repository = DynamicWindowRepository(database)
    session = repository.ensure_session(_identity(), configuration={})
    window = repository.ensure_window(session.session_id, start_sample=10, end_sample=510, start_s=0.1, end_s=5.1)
    first = repository.create_subscription(window.window_id, "request-1")
    second = repository.create_subscription(window.window_id, "request-2")
    assert repository.active_subscription_count(window.window_id) == 2
    cancelled = repository.cancel_subscription(first.subscription_id)
    assert cancelled.state == "cancelled"
    assert repository.active_subscription_count(window.window_id) == 1
    assert repository.cancel_subscription(first.subscription_id).state == "cancelled"
    assert repository.get_window(window.window_id) == window
    assert repository.cancel_subscription(second.subscription_id).state == "cancelled"
    assert repository.active_subscription_count(window.window_id) == 0
    assert repository.get_window(window.window_id) == window


def test_cancelling_one_consumer_does_not_cancel_protected_shared_work(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    _recording(database)
    repository = DynamicWindowRepository(database)
    session = repository.ensure_session(_identity(), configuration={})
    window = repository.ensure_window(session.session_id, start_sample=10, end_sample=510, start_s=0.1, end_s=5.1)
    ui_subscription = repository.create_subscription(window.window_id, "ui-request")
    full_subscription = repository.create_subscription(window.window_id, "full-reuse-request")

    repository.cancel_subscription(ui_subscription.subscription_id, cancel_unobserved_work=True)
    assert repository.get_window(window.window_id).state == "queued"
    assert repository.active_subscription_count(window.window_id) == 1

    repository.cancel_subscription(full_subscription.subscription_id, cancel_unobserved_work=False)
    assert repository.get_window(window.window_id).state == "queued"


def test_different_scientific_configuration_gets_a_different_session(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    _recording(database)
    repository = DynamicWindowRepository(database)
    first = repository.ensure_session(_identity(config="one"), configuration={"channel": "F3"})
    second = repository.ensure_session(_identity(config="two"), configuration={"channel": "F7"})
    assert first.session_id != second.session_id


def test_project_scopes_do_not_share_sessions_or_subscribers(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    _recording(database)
    with sqlite3.connect(database) as connection:
        connection.executemany(
            "INSERT INTO projects(project_id, name, description, created_at, updated_at) VALUES (?, ?, '', 'now', 'now')",
            [("project-a", "A"), ("project-b", "B")],
        )
    repository = DynamicWindowRepository(database)
    sessions = [repository.ensure_session(_identity(project_scope=scope), configuration={})
                for scope in [None, "project-a", "project-b"]]
    assert len({session.session_id for session in sessions}) == 3
    assert repository.ensure_session(_identity(project_scope="project-a"), configuration={}) == sessions[1]
    window = repository.ensure_window(sessions[1].session_id, start_sample=0, end_sample=500, start_s=0, end_s=5)
    with pytest.raises(KeyError, match="project scope"):
        repository.create_subscription(window.window_id, "request", project_id="project-b")
    subscription = repository.create_subscription(window.window_id, "request", project_id="project-a")
    with pytest.raises(KeyError):
        repository.cancel_subscription(subscription.subscription_id, project_id="project-b")
    assert repository.active_subscription_count(window.window_id) == 1
    assert repository.cancel_subscription(subscription.subscription_id, project_id="project-a").state == "cancelled"


def test_concurrent_ensure_creates_exactly_one_session_and_window(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    _recording(database)
    repository = DynamicWindowRepository(database)

    def ensure(_):
        session = repository.ensure_session(_identity(), configuration={})
        window = repository.ensure_window(session.session_id, start_sample=0, end_sample=500, start_s=0, end_s=5)
        return session.session_id, window.window_id

    with ThreadPoolExecutor(max_workers=4) as pool:
        identities = list(pool.map(ensure, range(12)))
    assert len(set(identities)) == 1
    with sqlite3.connect(database) as connection:
        assert connection.execute("SELECT COUNT(*) FROM dynamic_analysis_sessions").fetchone()[0] == 1
        assert connection.execute("SELECT COUNT(*) FROM dynamic_analysis_windows").fetchone()[0] == 1


def test_conflicting_config_and_time_coordinates_do_not_mutate_snapshots(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    _recording(database)
    repository = DynamicWindowRepository(database)
    session = repository.ensure_session(_identity(), configuration={"channel": "F3"})
    with pytest.raises(ValueError, match="configuration snapshot"):
        repository.ensure_session(_identity(), configuration={"channel": "F7"})
    assert repository.get_session(session.session_id) == session
    window = repository.ensure_window(session.session_id, start_sample=0, end_sample=500, start_s=0, end_s=5)
    with pytest.raises(ValueError, match="time coordinates"):
        repository.ensure_window(session.session_id, start_sample=0, end_sample=500, start_s=0, end_s=6)
    assert repository.get_window(window.window_id) == window


@pytest.mark.parametrize("changes", [
    {"start_sample": 0.5}, {"end_sample": True}, {"start_s": -1},
    {"end_s": float("nan")}, {"end_s": float("inf")}, {"state": "ready"},
])
def test_invalid_new_windows_are_rejected_before_persistence(tmp_path, changes):
    database = tmp_path / "catalog.sqlite3"
    _recording(database)
    repository = DynamicWindowRepository(database)
    session = repository.ensure_session(_identity(), configuration={})
    params = dict(start_sample=0, end_sample=500, start_s=0, end_s=5)
    with pytest.raises(ValueError):
        repository.ensure_window(session.session_id, **(params | changes))
    with sqlite3.connect(database) as connection:
        assert connection.execute("SELECT COUNT(*) FROM dynamic_analysis_windows").fetchone()[0] == 0


def test_failed_transition_rolls_back_and_missing_parent_is_rejected(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    _recording(database)
    repository = DynamicWindowRepository(database)
    session = repository.ensure_session(_identity(), configuration={})
    window = repository.ensure_window(session.session_id, start_sample=0, end_sample=500, start_s=0, end_s=5)
    with pytest.raises(ValueError, match="structured failure"):
        repository.transition_window(window.window_id, "failed")
    assert repository.get_window(window.window_id) == window
    with pytest.raises(sqlite3.IntegrityError):
        repository.ensure_window("missing", start_sample=0, end_sample=500, start_s=0, end_s=5)


def test_attempt_lease_claim_heartbeat_failure_and_retry_are_scoped(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    _recording(database)
    repository = DynamicWindowRepository(database)
    session = repository.ensure_session(_identity(), configuration={})
    window = repository.ensure_window(session.session_id, start_sample=0, end_sample=500, start_s=0, end_s=5)
    first = repository.create_attempt(window.window_id, "request-1")
    claimed = repository.claim_attempt(window.window_id, "worker-1", "2999-01-01T00:00:00+00:00")
    assert claimed is not None and claimed.attempt_id == first.attempt_id and claimed.state == "running"
    assert repository.claim_attempt(window.window_id, "worker-2", "2999-01-01T00:00:00+00:00") is None
    with pytest.raises(ValueError, match="stale|owned"):
        repository.heartbeat_attempt(first.attempt_id, "worker-2", "2999-01-01T00:00:01+00:00", lease_token=claimed.lease_token)
    heartbeated = repository.heartbeat_attempt(first.attempt_id, "worker-1", "2999-01-01T00:00:01+00:00", lease_token=claimed.lease_token)
    assert heartbeated.heartbeat_at is not None
    with pytest.raises(ValueError, match="structured error"):
        repository.finish_attempt(first.attempt_id, "worker-1", "failed", lease_token=heartbeated.lease_token)
    failed = repository.finish_attempt(first.attempt_id, "worker-1", "failed", error={"code": "boom"}, lease_token=heartbeated.lease_token)
    assert failed.state == "failed" and failed.lease_expires_at is None
    retry = repository.create_attempt(window.window_id, "request-2")
    assert retry.attempt_id != first.attempt_id
    claimed_retry = repository.claim_attempt(window.window_id, "worker-2", "2999-01-01T00:00:00+00:00")
    assert claimed_retry is not None and claimed_retry.attempt_id == retry.attempt_id


def test_attempt_persists_producer_metadata_and_prefers_higher_priority(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    _recording(database)
    repository = DynamicWindowRepository(database)
    session = repository.ensure_session(_identity(), configuration={})
    window = repository.ensure_window(session.session_id, start_sample=0, end_sample=500, start_s=0, end_s=5)
    full = repository.create_attempt(
        window.window_id, "full-request", producer_mode="full",
        producer_owner_id="run-1", priority=10,
    )
    assert full.producer_mode == "full"
    assert full.producer_owner_id == "run-1"
    assert full.priority == 10
    # The existing active attempt remains the single durable producer. A
    # higher-priority request cannot create a second producer for the window.
    on_demand = repository.create_attempt(
        window.window_id, "preview-request", producer_mode="on_demand",
        producer_owner_id="page-1", priority=100,
    )
    assert on_demand.attempt_id == full.attempt_id
    assert on_demand.priority == 100
    assert repository.get_active_attempt(window.window_id).priority == 100

    claimed = repository.claim_attempt(window.window_id, "full-worker", "2999-01-01T00:00:00+00:00")
    assert claimed is not None
    unchanged = repository.create_attempt(
        window.window_id, "late-preview", producer_mode="on_demand",
        producer_owner_id="page-2", priority=1000,
    )
    assert unchanged.priority == 100


def test_queued_producer_transfer_is_fenced_and_running_owner_cannot_be_stolen(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    _recording(database)
    repository = DynamicWindowRepository(database)
    session = repository.ensure_session(_identity(), configuration={})
    window = repository.ensure_window(session.session_id, start_sample=0, end_sample=500, start_s=0, end_s=5)
    original = repository.create_attempt(
        window.window_id, "full-request", producer_mode="full",
        producer_owner_id="run-1", priority=10,
    )
    transferred = repository.transfer_queued_producer(
        window.window_id, expected_owner_id="run-1", new_owner_id="page-1",
        producer_mode="on_demand", priority=100,
    )
    assert transferred.attempt_id == original.attempt_id
    assert transferred.producer_mode == "on_demand"
    with pytest.raises(ValueError, match="owner"):
        repository.transfer_queued_producer(
            window.window_id, expected_owner_id="run-1", new_owner_id="page-2",
            producer_mode="on_demand", priority=100,
        )
    claimed = repository.claim_attempt(window.window_id, "page-worker", "2999-01-01T00:00:00+00:00")
    assert claimed is not None
    with pytest.raises(KeyError, match="queued"):
        repository.transfer_queued_producer(
            window.window_id, expected_owner_id="page-1", new_owner_id="run-2",
            producer_mode="full", priority=10,
        )


def test_expired_attempts_are_requeued_and_terminal_windows_cannot_retry(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    _recording(database)
    repository = DynamicWindowRepository(database)
    session = repository.ensure_session(_identity(), configuration={})
    window = repository.ensure_window(session.session_id, start_sample=0, end_sample=500, start_s=0, end_s=5)
    attempt = repository.create_attempt(window.window_id, "request-1")
    claimed = repository.claim_attempt(window.window_id, "worker-1", "2999-01-01T00:00:00+00:00")
    assert claimed is not None
    with sqlite3.connect(database) as connection:
        connection.execute("UPDATE dynamic_analysis_attempts SET lease_expires_at = '2000-01-01T00:00:00+00:00' WHERE attempt_id = ?", (attempt.attempt_id,))
    assert repository.recover_expired_attempts() == 1
    assert repository.get_attempt(attempt.attempt_id).state == "expired"
    with sqlite3.connect(database) as connection:
        assert connection.execute("SELECT COUNT(*) FROM dynamic_analysis_attempts WHERE window_id = ? AND state = 'queued'", (window.window_id,)).fetchone()[0] == 1
    with pytest.raises(ValueError, match="stale"):
        repository.finish_attempt(attempt.attempt_id, "worker-1", "succeeded", lease_token=claimed.lease_token)
    computing = repository.transition_window(window.window_id, "computing_window")
    rejected = repository.transition_window(
        computing.window_id, "rejected", failure={"code": "gate", "message": "quality"},
    )
    with pytest.raises(ValueError, match="terminal"):
        repository.create_attempt(rejected.window_id, "request-2")


def test_cancelled_window_retry_uses_new_generation_and_preserves_history(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    _recording(database)
    repository = DynamicWindowRepository(database)
    session = repository.ensure_session(_identity(), configuration={})
    original = repository.ensure_window(session.session_id, start_sample=0, end_sample=500, start_s=0, end_s=5)
    cancelled = repository.transition_window(
        original.window_id, "cancelled", quality="cancelled",
        failure={"code": "cancelled", "message": "user cancelled"},
    )

    retry = repository.retry_window(session.session_id, start_sample=0, end_sample=500, start_s=0, end_s=5, retry_of_window_id=original.window_id)
    assert retry.window_id != original.window_id
    assert retry.generation == 1
    assert retry.state == "queued"
    assert repository.get_window(original.window_id) == cancelled
    assert repository.latest_window(session.session_id, start_sample=0, end_sample=500) == retry
    assert repository.retry_window(session.session_id, start_sample=0, end_sample=500, start_s=0, end_s=5, retry_of_window_id=original.window_id) == retry
    with pytest.raises(ValueError, match="only cancelled or failed"):
        repository.retry_window(session.session_id, start_sample=0, end_sample=500, start_s=0, end_s=5, retry_of_window_id=retry.window_id)
