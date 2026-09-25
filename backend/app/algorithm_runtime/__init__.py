"""Schema-driven execution contracts for all current algorithms."""

from .contracts import (
    AlgorithmConfigBase,
    AlgorithmFailure,
    AlgorithmInputs,
    AlgorithmManifest,
    AlgorithmParameter,
    AlgorithmResult,
    AlgorithmSeriesResult,
    AlgorithmStructuredResult,
    ExecutionContext,
)
from .errors import AlgorithmRuntimeError, UnknownAlgorithmError, UnsupportedAlgorithmModeError

__all__ = [
    "AlgorithmConfigBase",
    "AlgorithmFailure",
    "AlgorithmInputs",
    "AlgorithmManifest",
    "AlgorithmParameter",
    "AlgorithmResult",
    "AlgorithmSeriesResult",
    "AlgorithmStructuredResult",
    "ExecutionContext",
    "AlgorithmRuntimeError",
    "UnknownAlgorithmError",
    "UnsupportedAlgorithmModeError",
]
