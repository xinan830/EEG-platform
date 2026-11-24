"""Fixed, lane-isolated execution workers with bounded native parallelism."""

from __future__ import annotations

import threading
from typing import Any

from threadpoolctl import threadpool_limits


class RunWorker:
    def __init__(self, queue: Any):
        self.queue = queue
        self._wake = threading.Event()
        self._stop = threading.Event()
        self._threads: list[threading.Thread] = []
        self._native_limits: Any = None
        self._state_lock = threading.Lock()
        self._active_workers = 0

    def start(self) -> None:
        with self._state_lock:
            if any(thread.is_alive() for thread in self._threads):
                return
            self._stop.clear()
            # Native limits are process-wide, with one owner for both workers.
            self._native_limits = threadpool_limits(limits=1)
            self._active_workers = 2
            self._threads = [threading.Thread(target=self._loop, args=(lane,),
                             name=f"analysis-{lane}-worker", daemon=True)
                             for lane in ("interactive", "batch")]
            for thread in self._threads:
                thread.start()

    def wake(self) -> None:
        self._wake.set()

    def stop(self) -> None:
        self._stop.set()
        self._wake.set()
        for thread in self._threads:
            thread.join(timeout=2.0)

    def _loop(self, lane: str) -> None:
        try:
            while not self._stop.is_set():
                while not self._stop.is_set() and self.queue.process_next(lane) is not None:
                    pass
                self._wake.wait(0.25)
                self._wake.clear()
        finally:
            with self._state_lock:
                self._active_workers -= 1
                if self._active_workers == 0 and self._native_limits is not None:
                    self._native_limits.restore_original_limits()
                    self._native_limits = None
