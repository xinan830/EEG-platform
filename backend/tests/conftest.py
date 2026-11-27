"""Shared pytest isolation and test tier markers for the backend suite."""

from __future__ import annotations

from pathlib import Path
import logging
import pytest


GATE_TEST_FILES = {
    "test_algorithm_runtime_architecture.py",
    "test_preprocessing_architecture_cleanup.py",
    "test_access_scheduler_boundary.py",
    "test_file_size_policy.py",
    "test_health.py",
    "test_api_contract.py",
    "test_spectrum_api.py",
}

INTEGRATION_TEST_FILES = {
    "test_real_recording_regression.py",
    "test_result_boundary_baseline.py",
    "test_official_algorithm_numeric_reference_audit.py",
    "test_official_algorithm_extended_audit.py",
    "test_official_strategy_validation_matrix.py",
    "test_official_combined_strategies.py",
}


@pytest.fixture(autouse=True)
def isolate_app_state():
    """Restore app.state bindings; lifecycle tests must own their service instances."""
    from app.main import app

    snapshot = dict(app.state._state)
    try:
        yield
    finally:
        app.state._state.clear()
        app.state._state.update(snapshot)


@pytest.fixture
def backend_caplog(caplog, monkeypatch):
    """Opt diagnostic tests into INFO capture without changing production handlers."""
    caplog.set_level(logging.INFO, logger="brain_platform")
    monkeypatch.setattr(logging.getLogger("brain_platform"), "propagate", True)
    return caplog


@pytest.fixture
def override_app_state(isolate_app_state):
    """Set temporary attributes on app.state that are automatically restored after the test."""
    from app.main import app

    def _apply(**overrides):
        for key, value in overrides.items():
            setattr(app.state, key, value)
        return app

    return _apply


def pytest_collection_modifyitems(items: list[pytest.Item]) -> None:
    for item in items:
        filename = Path(str(item.fspath)).name
        if filename in GATE_TEST_FILES:
            item.add_marker(pytest.mark.gate)
        elif filename in INTEGRATION_TEST_FILES:
            item.add_marker(pytest.mark.integration)
        else:
            item.add_marker(pytest.mark.domain)
