"""Admission, canonical identity, and bounded concurrent scheduling contracts."""

import sqlite3
import threading
from concurrent.futures import ThreadPoolExecutor

import pytest

from app.algorithm_runtime.errors import (
    DynamicWindowCapacityExceededError, DynamicWindowIdentityConflictError,
    DynamicWindowProjectForbiddenError,
)
from app.algorithm_runtime.on_demand import DynamicWindowRequest
from app.algorithm_runtime.registry import AlgorithmRegistry
from app.algorithms.psd.runner import PsdAlgorithm
from app.models.recording import RecordingSummary
from app.persistence.migrations import migrate_database
from app.services.on_demand_dynamic import DynamicWindowService
from test_on_demand_dynamic_service import _RecordingStub


@pytest.fixture
def admission(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    migrate_database(database)
    with sqlite3.connect(database) as connection:
        connection.execute("INSERT INTO recordings(id, original_name, stored_name, extension, created_at) VALUES ('r', 'r', 'r', '.edf', 'now')")
    recording = RecordingSummary("r", "r", "r", ".edf", "now", sfreq=200,
                                 duration_s=20, channels=("F3",), source_sha256="source")
    started, release = threading.Event(), threading.Event()
    class BlockingRecording(_RecordingStub):
        def load_spectrum(self, *args, **kwargs):
            started.set()
            assert release.wait(15)
            return super().load_spectrum(*args, **kwargs)
    registry = AlgorithmRegistry()
    registry.register(PsdAlgorithm())
    service = DynamicWindowService(BlockingRecording(recording), registry, database,
                                  tmp_path / "artifacts", max_pending_windows=2)
    request = DynamicWindowRequest(recording_id="r", algorithm_id="psd", cursor_sample=800,
        config={"time": {"start_s": 0, "end_s": 20}, "channel": "F3", "mode": "dynamic",
                "dynamic_window_s": 5, "refresh_step_s": 1, "low_hz": 1, "high_hz": 30, "notch_hz": None})
    yield service, request, started, release
    release.set()
    service.close()


def test_concurrent_distinct_targets_cannot_leave_denied_attempts_or_subscriptions(admission):
    service, request, started, _ = admission
    first = service.ensure(request.model_copy(update={"request_id": "first"}))
    assert started.wait(2)
    def ensure(index):
        try:
            return service.ensure(request.model_copy(update={"cursor_sample": index * 200,
                                                              "request_id": f"request-{index}"}))
        except DynamicWindowCapacityExceededError:
            return None
    with ThreadPoolExecutor(max_workers=8) as pool:
        results = list(pool.map(ensure, range(5, 21)))
    assert sum(item is not None for item in results) == 1
    with sqlite3.connect(service.repository.database_path) as connection:
        assert connection.execute("SELECT COUNT(*) FROM dynamic_analysis_attempts").fetchone()[0] == 2
        assert connection.execute("SELECT COUNT(*) FROM dynamic_analysis_subscriptions").fetchone()[0] == 2
    assert service.ensure(request.model_copy(update={"request_id": "same-target"}))["window_id"] == first["window_id"]


def test_database_admission_is_atomic_even_without_the_service_gate(admission):
    service, request, _, _ = admission
    first = service.ensure(request)
    repository = service.repository
    windows = [repository.ensure_window(first["session_id"], start_sample=index * 200,
        end_sample=(index + 5) * 200, start_s=index, end_s=index + 5) for index in range(1, 12)]
    def insert(window):
        try:
            return repository.create_attempt(window.window_id, window.window_id, max_active_attempts=2)
        except DynamicWindowCapacityExceededError:
            return None
    with ThreadPoolExecutor(max_workers=8) as pool:
        results = list(pool.map(insert, windows))
    assert sum(item is not None for item in results) == 1


@pytest.mark.parametrize("project", ["missing", "another-project"])
def test_project_mismatch_fails_before_scientific_rows_exist(admission, project):
    service, request, _, _ = admission
    if project == "another-project":
        with sqlite3.connect(service.repository.database_path) as connection:
            connection.execute("INSERT INTO projects VALUES ('another-project', 'test', '', 'now', 'now')")
    with pytest.raises(DynamicWindowProjectForbiddenError):
        service.ensure(request.model_copy(update={"project_id": project}))
    with sqlite3.connect(service.repository.database_path) as connection:
        assert connection.execute("SELECT COUNT(*) FROM dynamic_analysis_sessions").fetchone()[0] == 0


def test_client_cannot_spoof_published_definition(admission):
    service, request, _, _ = admission
    with pytest.raises(DynamicWindowIdentityConflictError):
        service.ensure(request.model_copy(update={"definition_id": "spoofed"}))
    with sqlite3.connect(service.repository.database_path) as connection:
        assert connection.execute("SELECT COUNT(*) FROM dynamic_analysis_sessions").fetchone()[0] == 0


def test_resolved_definition_digest_and_build_are_pinned(admission):
    service, request, _, _ = admission
    service._identity_resolver.implementation_build = "test-build-1"
    first = service.ensure(request)
    assert len(first["identity"]["definition_sha256"]) == 64
    assert first["identity"]["implementation_build"] == "test-build-1"
    service._identity_resolver.implementation_build = "test-build-2"
    second = service.ensure(request)
    assert second["session_id"] != first["session_id"]


def test_closed_service_does_not_admit_work(admission):
    service, request, _, _ = admission
    service.close()
    with pytest.raises(DynamicWindowCapacityExceededError):
        service.ensure(request)
