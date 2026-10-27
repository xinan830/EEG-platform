"""Unregistered TNK algorithm modules.

This package is intentionally separate from ``builtin_modules.py`` while the
scientific implementations complete numeric and strategy acceptance. Modules
consume a typed spectral window; file IO, scheduling, persistence, and catalog
registration remain platform-owned boundaries.
"""

from .contracts import TNK_ALGORITHM_IDS, TnkConfig, TnkManifest, TnkSpectralWindow
from .stateless import build_stateless_modules
from .stateful import build_stateful_modules
from .segment import CompositeMeditationModule, FaaModule
from .artifacts import TnkArtifactDescriptor, describe_arrays
from .playback_event import TnkPlaybackFrameEvent
from .playback_delivery import (
    TnkPlaybackDiscontinuity,
    TnkPlaybackEventBatch,
    TnkPlaybackEventBuffer,
)

__all__ = [
    "TNK_ALGORITHM_IDS", "TnkConfig", "TnkManifest", "TnkSpectralWindow",
    "build_stateless_modules", "build_stateful_modules", "FaaModule", "CompositeMeditationModule",
    "TnkArtifactDescriptor", "describe_arrays",
    "TnkPlaybackFrameEvent",
    "TnkPlaybackDiscontinuity", "TnkPlaybackEventBatch", "TnkPlaybackEventBuffer",
]
