"""Pure lifecycle rules for on-demand dynamic windows.

The scheduler and persistence layer will consume these rules later. Keeping
the transition table pure makes invalid state changes testable without starting
workers or touching the existing AnalysisRun queue.
"""

from __future__ import annotations

from typing import Final

from .on_demand import OnDemandWindowState


TERMINAL_WINDOW_STATES: Final[frozenset[OnDemandWindowState]] = frozenset(
    {"ready", "insufficient_data", "rejected", "unavailable", "cancelled", "failed"}
)

_ALLOWED_TRANSITIONS: Final[dict[OnDemandWindowState, frozenset[OnDemandWindowState]]] = {
    "preparing_source": frozenset({"queued", "computing_window", "insufficient_data", "unavailable", "cancelled", "failed"}),
    "queued": frozenset({"preparing_source", "computing_window", "insufficient_data", "cancelled", "failed"}),
    # A lease recovery may safely requeue work that never published a result.
    "computing_window": frozenset({"queued", "ready", "rejected", "unavailable", "cancelled", "failed"}),
    "ready": frozenset(),
    "insufficient_data": frozenset(),
    "rejected": frozenset(),
    "unavailable": frozenset(),
    "cancelled": frozenset(),
    "failed": frozenset(),
}


def can_transition(current: OnDemandWindowState, target: OnDemandWindowState) -> bool:
    """Return whether a window may make one forward lifecycle transition."""

    return target in _ALLOWED_TRANSITIONS[current]


def require_transition(current: OnDemandWindowState, target: OnDemandWindowState) -> None:
    """Raise a stable error when a persistence update violates the lifecycle."""

    if not can_transition(current, target):
        raise ValueError(f"invalid dynamic window state transition: {current} -> {target}")
