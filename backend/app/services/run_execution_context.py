"""Cooperative controls shared by the queue and dynamic algorithm modules."""

from __future__ import annotations

from collections.abc import Callable


class RunCancelledError(RuntimeError):
    """Raised when a dynamic run observes cancellation between windows."""


class RunExecutionContext:
    def __init__(self, is_cancel_requested: Callable[[], bool], report_progress: Callable[[int], None], total: int | None):
        self._is_cancel_requested = is_cancel_requested
        self._report_progress = report_progress
        self.total = total
        self.completed = 0

    def check_cancelled(self) -> None:
        if self._is_cancel_requested():
            raise RunCancelledError("分析已取消")

    def window_started(self) -> None:
        self.check_cancelled()

    def window_completed(self) -> None:
        self.completed += 1
        self._report_progress(self.completed)
        self.check_cancelled()
