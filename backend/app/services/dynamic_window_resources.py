"""Explicit resource policy for on-demand work, not a recording-duration limit."""

from dataclasses import dataclass


@dataclass(frozen=True)
class DynamicWindowResourcePolicy:
    max_project_attempts: int = 8
    max_recording_attempts: int = 4
    max_preparation_bytes: int = 8 * 1024**3
    minimum_free_disk_bytes: int = 1024**3
    max_artifact_directory_bytes: int = 2 * 1024**3

    def __post_init__(self):
        for name in self.__dataclass_fields__:
            value = getattr(self, name)
            if type(value) is not int or value < 1:
                raise ValueError(f"{name} must be a positive integer")
