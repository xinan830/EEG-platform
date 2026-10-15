"""Stable errors raised before or during algorithm execution."""

from __future__ import annotations

from typing import Any


class AlgorithmRuntimeError(Exception):
    code = "ALGORITHM_RUNTIME_ERROR"

    def __init__(self, message: str, *, detail: dict[str, Any] | None = None) -> None:
        super().__init__(message)
        self.message = message
        self.detail = detail or {}


class UnknownAlgorithmError(AlgorithmRuntimeError):
    code = "ALGORITHM_NOT_FOUND"


class DuplicateAlgorithmError(AlgorithmRuntimeError):
    code = "ALGORITHM_DUPLICATE"


class AmbiguousAlgorithmVersionError(AlgorithmRuntimeError):
    code = "ALGORITHM_VERSION_REQUIRED"


class UnsupportedAlgorithmModeError(AlgorithmRuntimeError):
    code = "ALGORITHM_MODE_UNSUPPORTED"


class UnsupportedDynamicWindowCapabilityError(AlgorithmRuntimeError):
    code = "ON_DEMAND_CAPABILITY_UNSUPPORTED"


class InvalidAnalysisWindowError(AlgorithmRuntimeError):
    code = "ANALYSIS_WINDOW_INVALID"


class DynamicWindowCapacityExceededError(AlgorithmRuntimeError):
    code = "WINDOW_CAPACITY_EXCEEDED"


class DynamicWindowIdentityConflictError(AlgorithmRuntimeError):
    code = "WINDOW_IDENTITY_CONFLICT"


class DynamicWindowProjectForbiddenError(AlgorithmRuntimeError):
    code = "WINDOW_PROJECT_FORBIDDEN"


class DynamicWindowCursorInvalidError(AlgorithmRuntimeError):
    code = "WINDOW_CURSOR_INVALID"


class DynamicWindowResultCorruptError(AlgorithmRuntimeError, ValueError):
    code = "WINDOW_RESULT_CORRUPT"


class DynamicWindowResourceExceededError(AlgorithmRuntimeError):
    code = "WINDOW_RESOURCE_EXCEEDED"
