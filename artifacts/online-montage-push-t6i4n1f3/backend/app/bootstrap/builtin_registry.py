"""Composition root for the executable built-in algorithm modules."""

from __future__ import annotations

from app.algorithm_runtime.registry import AlgorithmRegistry
from app.algorithms.builtin_modules import build_builtin_modules


def build_builtin_registry() -> AlgorithmRegistry:
    """Build the application registry from concrete official modules."""

    registry = AlgorithmRegistry()
    for module in build_builtin_modules():
        registry.register(module)
    return registry
