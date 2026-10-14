import pytest

from app.algorithm_runtime.on_demand_lifecycle import (
    TERMINAL_WINDOW_STATES,
    can_transition,
    require_transition,
)


def test_window_lifecycle_allows_preparation_and_computation_path():
    assert can_transition("preparing_source", "queued")
    assert can_transition("queued", "computing_window")
    assert can_transition("computing_window", "ready")


@pytest.mark.parametrize("state", ["ready", "insufficient_data", "rejected", "unavailable", "cancelled", "failed"])
def test_terminal_states_cannot_be_reopened(state):
    assert state in TERMINAL_WINDOW_STATES
    assert not can_transition(state, "queued")
    with pytest.raises(ValueError, match="invalid dynamic window state transition"):
        require_transition(state, "computing_window")


def test_quality_rejection_is_terminal_and_distinct_from_execution_failure():
    assert can_transition("computing_window", "rejected")
    assert can_transition("computing_window", "failed")
    assert not can_transition("rejected", "failed")
    assert not can_transition("failed", "rejected")


def test_cancellation_is_allowed_before_science_starts_or_during_execution():
    assert can_transition("preparing_source", "cancelled")
    assert can_transition("queued", "cancelled")
    assert can_transition("computing_window", "cancelled")
    assert not can_transition("ready", "cancelled")
