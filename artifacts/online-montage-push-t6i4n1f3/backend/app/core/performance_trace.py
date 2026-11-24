"""Request-local aggregate timings; never log payloads or individual SQL reads."""

from contextlib import contextmanager
from contextvars import ContextVar
import logging
from time import perf_counter


class PerformanceTrace:
    def __init__(self, **identity: object) -> None:
        self.identity = identity
        self.metrics: dict[str, float | int] = {}

    def add(self, stage: str, elapsed_ms: float) -> None:
        self.increment(f"{stage}_calls")
        self.metrics[f"{stage}_ms"] = self.metrics.get(f"{stage}_ms", 0.0) + elapsed_ms

    def increment(self, name: str, count: int = 1) -> None:
        self.metrics[name] = self.metrics.get(name, 0) + count


_current: ContextVar[PerformanceTrace | None] = ContextVar("performance_trace", default=None)


def current_performance_trace() -> PerformanceTrace | None:
    return _current.get()


@contextmanager
def measure_performance(stage: str):
    trace = _current.get()
    if trace is None:
        yield
        return
    started = perf_counter()
    try:
        yield
    finally:
        trace.add(stage, (perf_counter() - started) * 1000)


@contextmanager
def performance_trace(event: str, **identity: object):
    logger = logging.getLogger("brain_platform.performance")
    trace = PerformanceTrace(**identity)
    token = _current.set(trace if logger.isEnabledFor(logging.INFO) else None)
    started = perf_counter()
    outcome = "ok"
    try:
        yield trace
    except BaseException:
        outcome = "error"
        raise
    finally:
        _current.reset(token)
        fields = " ".join(f"{key}={value if value is not None else 'none'}"
                          for key, value in trace.identity.items())
        metrics = " ".join(f"{key}={value:.3f}" if isinstance(value, float) else f"{key}={value}"
                           for key, value in sorted(trace.metrics.items()))
        logger.info("%s %s outcome=%s %s total_ms=%.3f",
                    event, fields, outcome, metrics, (perf_counter() - started) * 1000)
