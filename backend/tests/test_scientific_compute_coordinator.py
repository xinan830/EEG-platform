"""Concurrency boundaries for the shared offline scientific compute gate."""

from __future__ import annotations

import threading
import time

from app.services.run_execution_context import RunCancelledError, RunExecutionContext
from app.services.scientific_compute_coordinator import (
    ScientificComputeCoordinator,
    ScientificComputeCoordinatorClosed,
)


def test_full_cache_wait_releases_batch_slot_for_interactive_window():
    coordinator = ScientificComputeCoordinator(max_active=1, wait_poll_s=0.005)
    provider_started = threading.Event()
    interactive_acquired = threading.Event()
    release_provider = threading.Event()
    errors: list[BaseException] = []

    def provider(_start: int, _end: int, *, check_cancelled):
        provider_started.set()
        while not release_provider.wait(0.005):
            check_cancelled()
        return {"source": "on_demand"}

    context = RunExecutionContext(
        lambda: False,
        lambda _completed: None,
        1,
        cached_window_provider=provider,
        compute_coordinator=coordinator,
    )
    context.window_started()

    def wait_for_cache() -> None:
        try:
            assert context.cached_window(0, 100) == {"source": "on_demand"}
        except BaseException as exc:  # pragma: no cover - surfaced below
            errors.append(exc)

    cache_thread = threading.Thread(target=wait_for_cache)
    cache_thread.start()
    assert provider_started.wait(1.0)

    interactive_lease = coordinator.acquire("interactive", lambda: None)
    try:
        interactive_acquired.set()
    finally:
        interactive_lease.release()
    release_provider.set()
    cache_thread.join(timeout=1.0)
    context.window_completed()

    assert interactive_acquired.is_set()
    assert not cache_thread.is_alive()
    assert errors == []
    assert coordinator.snapshot()["active"] == 0


def test_full_cache_miss_reacquires_batch_slot_for_numerical_work():
    coordinator = ScientificComputeCoordinator(max_active=1)
    context = RunExecutionContext(
        lambda: False,
        lambda _completed: None,
        1,
        cached_window_provider=lambda _start, _end, *, check_cancelled: None,
        compute_coordinator=coordinator,
    )
    context.window_started()
    assert coordinator.snapshot()["active"] == 1

    assert context.cached_window(0, 100) is None
    assert coordinator.snapshot()["active"] == 1

    context.window_completed()
    assert coordinator.snapshot()["active"] == 0


def test_full_cache_wait_honours_cancellation():
    coordinator = ScientificComputeCoordinator(max_active=1, wait_poll_s=0.005)
    cancelled = threading.Event()
    provider_started = threading.Event()
    errors: list[BaseException] = []

    def provider(_start: int, _end: int, *, check_cancelled):
        provider_started.set()
        while True:
            check_cancelled()
            time.sleep(0.005)

    context = RunExecutionContext(
        cancelled.is_set,
        lambda _completed: None,
        1,
        cached_window_provider=provider,
        compute_coordinator=coordinator,
    )
    context.window_started()

    def wait_for_cache() -> None:
        try:
            context.cached_window(0, 100)
        except BaseException as exc:
            errors.append(exc)

    thread = threading.Thread(target=wait_for_cache)
    thread.start()
    assert provider_started.wait(1.0)
    cancelled.set()
    thread.join(timeout=1.0)
    context.close()

    assert not thread.is_alive()
    assert len(errors) == 1
    assert isinstance(errors[0], RunCancelledError)
    assert coordinator.snapshot()["active"] == 0


def test_close_wakes_waiting_acquire():
    coordinator = ScientificComputeCoordinator(max_active=1, wait_poll_s=0.005)
    holder = coordinator.acquire("batch")
    errors: list[BaseException] = []

    def wait_for_slot() -> None:
        try:
            coordinator.acquire("interactive")
        except BaseException as exc:
            errors.append(exc)

    thread = threading.Thread(target=wait_for_slot)
    thread.start()
    time.sleep(0.02)
    coordinator.close()
    thread.join(timeout=1.0)
    holder.release()

    assert not thread.is_alive()
    assert len(errors) == 1
    assert isinstance(errors[0], ScientificComputeCoordinatorClosed)
    assert coordinator.snapshot()["closed"] is True
    assert coordinator.snapshot()["active"] == 0


def test_interactive_burst_gives_waiting_batch_a_turn():
    coordinator = ScientificComputeCoordinator(
        max_active=1, interactive_burst_limit=4, wait_poll_s=0.005,
    )
    holder = coordinator.acquire("interactive")
    batch_acquired = threading.Event()
    errors: list[BaseException] = []

    def wait_for_batch() -> None:
        try:
            lease = coordinator.acquire("batch")
            batch_acquired.set()
            lease.release()
        except BaseException as exc:  # pragma: no cover - surfaced below
            errors.append(exc)

    thread = threading.Thread(target=wait_for_batch)
    thread.start()
    time.sleep(0.02)
    holder.release()

    for _ in range(4):
        lease = coordinator.acquire("interactive")
        lease.release()

    thread.join(timeout=1.0)
    coordinator.close()

    assert batch_acquired.is_set()
    assert errors == []
    metrics = coordinator.snapshot()["metrics"]
    assert metrics["acquired"]["batch"] == 1
    assert metrics["waited"]["batch"] == 1
    assert metrics["max_wait_s"]["batch"] > 0
    assert metrics["completed"]["interactive"] == 5
    assert metrics["completed"]["batch"] == 1
    assert metrics["batch_yields"] >= 1
    assert metrics["max_hold_s"]["interactive"] >= 0

