"""Non-scientific execution controls passed across the runtime boundary."""

from typing import Protocol, runtime_checkable


@runtime_checkable
class ExecutionControl(Protocol):
    def check_cancelled(self) -> None: ...
    def window_started(self) -> None: ...
    def window_completed(self) -> None: ...
