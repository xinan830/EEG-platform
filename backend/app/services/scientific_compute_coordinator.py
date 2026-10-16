"""Bounded, process-local coordination for offline scientific windows.

The coordinator is deliberately smaller than a job queue.  Durable run and
window state remains in SQLite; this gate only decides which already-admitted
window may enter the expensive numerical section.  A single slot keeps native
BLAS work bounded while the lane policy gives interactive windows priority
without allowing them to starve a background full run.
"""

from __future__ import annotations

import threading
import time
from dataclasses import dataclass
import logging
from typing import Callable, Literal


ComputeLane = Literal["interactive", "batch"]
logger = logging.getLogger(__name__)


class ScientificComputeCoordinatorClosed(RuntimeError):
    """Raised when work is admitted after the application is shutting down."""


@dataclass
class ScientificComputeLease:
    """One held compute slot; release is idempotent for exception-safe callers."""

    coordinator: "ScientificComputeCoordinator"
    lane: ComputeLane
    acquired_at: float
    waited_s: float
    _released: bool = False

    def release(self) -> None:
        if self._released:
            return
        self._released = True
        self.coordinator._release(self)

    def __enter__(self) -> "ScientificComputeLease":
        return self

    def __exit__(self, _type, _value, _traceback) -> None:
        self.release()


class ScientificComputeCoordinator:
    """A bounded fair gate shared by full and on-demand window workers.

    ``interactive_burst_limit`` is a scheduling guard, not a promised frame
    rate.  It is intentionally observable through :meth:`snapshot` so a later
    performance change can be based on measurements rather than assumptions.
    """

    def __init__(self, *, max_active: int = 1, interactive_burst_limit: int = 4,
                 wait_poll_s: float = 0.05) -> None:
        if type(max_active) is not int or max_active < 1:
            raise ValueError("max_active must be a positive integer")
        if type(interactive_burst_limit) is not int or interactive_burst_limit < 1:
            raise ValueError("interactive_burst_limit must be a positive integer")
        if type(wait_poll_s) not in (int, float) or wait_poll_s <= 0:
            raise ValueError("wait_poll_s must be positive")
        self.max_active = max_active
        self.interactive_burst_limit = interactive_burst_limit
        self.wait_poll_s = float(wait_poll_s)
        self._condition = threading.Condition(threading.RLock())
        self._active = 0
        self._waiting: dict[ComputeLane, int] = {"interactive": 0, "batch": 0}
        self._interactive_since_batch = 0
        self._closed = False
        self._metrics = {
            "acquired": {"interactive": 0, "batch": 0},
            "waited": {"interactive": 0, "batch": 0},
            "total_wait_s": {"interactive": 0.0, "batch": 0.0},
            "max_wait_s": {"interactive": 0.0, "batch": 0.0},
            "completed": {"interactive": 0, "batch": 0},
            "total_hold_s": {"interactive": 0.0, "batch": 0.0},
            "max_hold_s": {"interactive": 0.0, "batch": 0.0},
            "batch_yields": 0,
        }

    def acquire(self, lane: ComputeLane, check_cancelled: Callable[[], None] | None = None) -> ScientificComputeLease:
        if lane not in ("interactive", "batch"):
            raise ValueError("unknown scientific compute lane")
        started_wait = time.perf_counter()
        with self._condition:
            self._waiting[lane] += 1
            try:
                while True:
                    if self._closed:
                        raise ScientificComputeCoordinatorClosed("scientific compute coordinator is closed")
                    if check_cancelled is not None:
                        check_cancelled()
                    if self._active < self.max_active and self._can_grant(lane):
                        self._active += 1
                        waited_s = time.perf_counter() - started_wait
                        metrics = self._metrics
                        metrics["acquired"][lane] += 1
                        if waited_s > 1e-9:
                            metrics["waited"][lane] += 1
                            metrics["total_wait_s"][lane] += waited_s
                            metrics["max_wait_s"][lane] = max(metrics["max_wait_s"][lane], waited_s)
                        if lane == "interactive":
                            self._interactive_since_batch += 1
                        logger.info(
                            "scientific_compute_acquired lane=%s waited_ms=%.1f active=%s waiting_interactive=%s waiting_batch=%s",
                            lane, waited_s * 1000.0, self._active,
                            self._waiting["interactive"], self._waiting["batch"],
                        )
                        return ScientificComputeLease(self, lane, time.perf_counter(), waited_s)
                    self._condition.wait(timeout=self.wait_poll_s)
            except Exception:
                logger.info(
                    "scientific_compute_wait_cancelled lane=%s waited_ms=%.1f",
                    lane, (time.perf_counter() - started_wait) * 1000.0,
                )
                raise
            finally:
                self._waiting[lane] -= 1

    def close(self) -> None:
        with self._condition:
            self._closed = True
            self._condition.notify_all()
        logger.info("scientific_compute_closed active=%s", self._active)

    def snapshot(self) -> dict[str, object]:
        """Return a copy suitable for diagnostics or structured telemetry."""
        with self._condition:
            return {
                "active": self._active,
                "max_active": self.max_active,
                "waiting": dict(self._waiting),
                "interactive_since_batch": self._interactive_since_batch,
                "closed": self._closed,
                "interactive_burst_limit": self.interactive_burst_limit,
                "metrics": {
                    key: dict(value) if isinstance(value, dict) else value
                    for key, value in self._metrics.items()
                },
            }

    def _can_grant(self, lane: ComputeLane) -> bool:
        if lane == "interactive":
            # A waiting batch lane gets a turn after the configured burst.  If
            # no batch work is waiting, interactive work remains immediately
            # responsive.
            return self._waiting["batch"] == 0 or self._interactive_since_batch < self.interactive_burst_limit
        return self._waiting["interactive"] == 0 or self._interactive_since_batch >= self.interactive_burst_limit

    def _release(self, lease: ScientificComputeLease) -> None:
        held_s = max(0.0, time.perf_counter() - lease.acquired_at)
        with self._condition:
            if self._active <= 0:
                raise RuntimeError("scientific compute lease released without an active slot")
            self._active -= 1
            metrics = self._metrics
            metrics["completed"][lease.lane] += 1
            metrics["total_hold_s"][lease.lane] += held_s
            metrics["max_hold_s"][lease.lane] = max(metrics["max_hold_s"][lease.lane], held_s)
            if lease.lane == "batch":
                if self._waiting["interactive"]:
                    metrics["batch_yields"] += 1
                self._interactive_since_batch = 0
            self._condition.notify_all()
            logger.info(
                "scientific_compute_released lane=%s held_ms=%.1f active=%s batch_yields=%s",
                lease.lane, held_s * 1000.0, self._active, metrics["batch_yields"],
            )
