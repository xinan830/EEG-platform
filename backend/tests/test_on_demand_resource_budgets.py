"""Capacity failures preserve source, subscriptions and published scientific data."""

import sqlite3
from dataclasses import replace
from types import SimpleNamespace

import pytest

from app.algorithm_runtime.errors import DynamicWindowCapacityExceededError, DynamicWindowResourceExceededError
from app.algorithm_runtime.on_demand import DynamicWindowRequest
from app.services.dynamic_window_resources import DynamicWindowResourcePolicy
from app.services.mapped_preprocessing import MappedArrays
from on_demand_fixtures import configure_api, psd_config, source_hashes
from test_on_demand_admission import admission
from test_on_demand_psd_api import wait_window


@pytest.mark.parametrize("scope", ["project", "recording"])
def test_scoped_admission_is_transactional_and_duplicates_remain_available(admission, scope):
    service, request, started, _ = admission
    service.resource_policy = replace(service.resource_policy, **{f"max_{scope}_attempts": 1})
    first = service.ensure(request)
    assert started.wait(2)
    with pytest.raises(DynamicWindowCapacityExceededError) as error:
        service.ensure(request.model_copy(update={"cursor_sample": 1200}))
    assert error.value.detail["scope"] == f"{scope}_id"
    assert service.ensure(request.model_copy(update={"request_id": "join"}))["window_id"] == first["window_id"]
    with sqlite3.connect(service.repository.database_path) as connection:
        assert connection.execute("SELECT COUNT(*) FROM dynamic_analysis_attempts").fetchone()[0] == 1
        assert connection.execute("SELECT COUNT(*) FROM dynamic_analysis_subscriptions").fetchone()[0] == 2


@pytest.mark.parametrize("scope", ["project", "recording"])
def test_full_publication_reservations_do_not_consume_on_demand_quotas(admission, scope):
    service, request, started, _ = admission
    service.resource_policy = replace(service.resource_policy, **{f"max_{scope}_attempts": 2})
    first = service.ensure(request)
    assert started.wait(2)

    # Full results may reserve many windows until their Run publishes them.
    # Those reservations are outside the interactive queue's scoped budgets.
    repository = service.repository
    for index in range(2):
        window = repository.ensure_window(
            first["session_id"], start_sample=2000 + index * 1000,
            end_sample=3000 + index * 1000, start_s=10 + index * 5, end_s=15 + index * 5,
        )
        repository.create_attempt(
            window.window_id, f"full-{index}", producer_mode="full",
            producer_owner_id=f"run-{index}", priority=10,
        )

    second = service.ensure(request.model_copy(update={"cursor_sample": 1200, "request_id": "second"}))
    assert second["window_id"] != first["window_id"]
    with pytest.raises(DynamicWindowCapacityExceededError) as error:
        service.ensure(request.model_copy(update={"cursor_sample": 1400, "request_id": "third"}))
    assert error.value.detail["scope"] == f"{scope}_id"
    with sqlite3.connect(service.repository.database_path) as connection:
        assert connection.execute(
            "SELECT COUNT(*) FROM dynamic_analysis_attempts WHERE producer_mode = 'full' AND state = 'queued'"
        ).fetchone()[0] == 2
        assert connection.execute(
            "SELECT COUNT(*) FROM dynamic_analysis_attempts WHERE producer_mode = 'on_demand' AND state IN ('queued','running')"
        ).fetchone()[0] == 2


def test_persistent_capacity_does_not_delete_or_block_ready_results(tmp_path):
    client, recording, service, _, root = configure_api(tmp_path)
    before = source_hashes(root)
    request = DynamicWindowRequest(recording_id=recording.id, algorithm_id="psd",
        config=psd_config(recording), cursor_sample=1000)
    try:
        ensured = service.ensure(request)
        ready = wait_window(client, ensured["window_id"])
        assert ready["state"] == "ready"
        artifacts = {path.name: path.read_bytes() for path in service.artifacts_dir.iterdir()}
        service.resource_policy = replace(service.resource_policy, max_artifact_directory_bytes=1)
        with pytest.raises(DynamicWindowCapacityExceededError) as error:
            service.ensure(request.model_copy(update={"cursor_sample": 1200}))
        assert error.value.detail["scope"] == "artifact_directory"
        assert service.ensure(request)["result"] == ready["result"]
        assert artifacts == {path.name: path.read_bytes() for path in service.artifacts_dir.iterdir()}
        assert source_hashes(root) == before
    finally:
        service.close()
        service._executor.shutdown(wait=True)
        client.close()


def test_preparation_capacity_is_execution_failure_not_scientific_rejection(tmp_path):
    client, recording, service, _, root = configure_api(tmp_path)
    before = source_hashes(root)
    service.resource_policy = replace(service.resource_policy, max_preparation_bytes=1)
    try:
        ensured = service.ensure(DynamicWindowRequest(recording_id=recording.id, algorithm_id="psd",
            config=psd_config(recording), cursor_sample=1000))
        result = wait_window(client, ensured["window_id"])
        assert result["state"] == "failed"
        assert result["failure"]["code"] == "WINDOW_RESOURCE_EXCEEDED"
        assert result["failure"]["details"]["limit_bytes"] == 1
        assert result["result"] is None
        assert not list(service.artifacts_dir.iterdir())
        assert source_hashes(root) == before
    finally:
        service.close()
        service._executor.shutdown(wait=True)
        client.close()


def test_mapping_allocation_checks_free_disk_before_creating_files(monkeypatch):
    monkeypatch.setattr("app.services.mapped_preprocessing.shutil.disk_usage", lambda _: SimpleNamespace(free=1))
    owner = MappedArrays(DynamicWindowResourcePolicy())
    try:
        with pytest.raises(DynamicWindowResourceExceededError):
            owner.allocate((100, 2))
        assert owner.maps == {}
        assert owner.sequence == 0
    finally:
        owner.close()


@pytest.mark.parametrize("value", [0, -1, True, 1.5])
def test_invalid_resource_policy_is_not_silently_coerced(value):
    with pytest.raises(ValueError):
        DynamicWindowResourcePolicy(max_preparation_bytes=value)
