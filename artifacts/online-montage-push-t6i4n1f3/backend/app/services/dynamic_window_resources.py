"""Explicit resource policy for on-demand work, not a recording-duration limit."""

from dataclasses import dataclass

from app.algorithm_runtime.errors import DynamicWindowResourceExceededError


@dataclass(frozen=True)
class DynamicWindowResourcePolicy:
    max_project_attempts: int = 8
    max_recording_attempts: int = 4
    max_preparation_bytes: int = 8 * 1024**3
    minimum_free_disk_bytes: int = 1024**3
    max_artifact_directory_bytes: int = 2 * 1024**3
    max_replay_windows: int = 10000
    max_replay_samples: int = 10_000_000

    def __post_init__(self):
        for name in self.__dataclass_fields__:
            value = getattr(self, name)
            if type(value) is not int or value < 1:
                raise ValueError(f"{name} must be a positive integer")

    def check_replay_budget(self, *, context_start_sample: int, context_end_sample: int,
                            replay_window_count: int) -> None:
        """Reject unbounded history replay before a capability begins work."""
        requested_samples = context_end_sample - context_start_sample
        if requested_samples < 1 or replay_window_count < 1:
            raise ValueError("replay context must contain positive sample and window counts")
        if requested_samples > self.max_replay_samples:
            raise DynamicWindowResourceExceededError(
                "dynamic replay context exceeds the sample budget",
                detail={"resource": "replay_samples", "requested": requested_samples,
                        "limit": self.max_replay_samples},
            )
        if replay_window_count > self.max_replay_windows:
            raise DynamicWindowResourceExceededError(
                "dynamic replay context exceeds the window budget",
                detail={"resource": "replay_windows", "requested": replay_window_count,
                        "limit": self.max_replay_windows},
            )
