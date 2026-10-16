"""Cooperative controls shared by the queue and dynamic algorithm modules."""

from __future__ import annotations

from collections.abc import Callable
import inspect
from typing import Any


class RunCancelledError(RuntimeError):
    """Raised when a dynamic run observes cancellation between windows."""


class RunExecutionContext:
    def __init__(self, is_cancel_requested: Callable[[], bool], report_progress: Callable[[int], None], total: int | None,
                 cached_window_provider: Callable[..., Any] | None = None,
                 compute_coordinator: Any | None = None):
        self._is_cancel_requested = is_cancel_requested
        self._report_progress = report_progress
        self.total = total
        self.completed = 0
        self._cached_window_provider = cached_window_provider
        self.reused_window_count = 0
        self._compute_coordinator = compute_coordinator
        self._compute_lease: Any | None = None

    @property
    def cache_miss_reasons(self) -> dict[str, int]:
        value = getattr(self._cached_window_provider, "cache_miss_reasons", {})
        return dict(value) if isinstance(value, dict) else {}

    def cached_window(self, start_sample: int, end_sample: int) -> Any | None:
        if self._cached_window_provider is None:
            return None
        # A full window normally holds the batch lease from ``window_started``
        # until ``window_completed``.  Cache reuse is different: the provider
        # may wait for an interactive producer to finish, and that producer
        # needs the shared compute slot.  Never hold the batch lease while
        # waiting on persisted on-demand work.
        self._release_compute_lease()
        provider = self._cached_window_provider
        try:
            parameters = inspect.signature(provider).parameters
        except (TypeError, ValueError):
            parameters = {}
        supports_cancellation = (
            "check_cancelled" in parameters
            or any(item.kind is inspect.Parameter.VAR_KEYWORD for item in parameters.values())
        )
        if supports_cancellation:
            result = provider(
                start_sample, end_sample, check_cancelled=self.check_cancelled,
            )
        else:
            # Keep compatibility with test/integration providers written
            # before cancellation-aware cache waits were introduced.
            result = provider(start_sample, end_sample)
        if result is not None:
            self.reused_window_count += 1
        else:
            # A miss falls back to the full numerical path.  Re-acquire only
            # for that path; a verified cache hit remains lease-free.
            self.check_cancelled()
            if self._compute_coordinator is not None:
                self._compute_lease = self._compute_coordinator.acquire("batch", self.check_cancelled)
        return result

    def check_cancelled(self) -> None:
        if self._is_cancel_requested():
            raise RunCancelledError("分析已取消")

    def window_started(self) -> None:
        self.check_cancelled()
        if self._compute_coordinator is not None:
            if self._compute_lease is not None:
                raise RuntimeError("a full analysis window is already active")
            self._compute_lease = self._compute_coordinator.acquire("batch", self.check_cancelled)

    def window_completed(self) -> None:
        try:
            self.completed += 1
            self._report_progress(self.completed)
            self.check_cancelled()
        finally:
            self._release_compute_lease()

    def close(self) -> None:
        """Release a lease if a numerical frame failed before completion."""
        self._release_compute_lease()

    def _release_compute_lease(self) -> None:
        lease = self._compute_lease
        self._compute_lease = None
        if lease is not None:
            lease.release()
