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


class UnsupportedAlgorithmModeError(AlgorithmRuntimeError):
    code = "ALGORITHM_MODE_UNSUPPORTED"


class InvalidAnalysisWindowError(AlgorithmRuntimeError):
    code = "ANALYSIS_WINDOW_INVALID"
