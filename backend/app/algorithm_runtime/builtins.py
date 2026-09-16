"""Construction of the current built-in executable algorithm registry."""

from __future__ import annotations

from .registry import AlgorithmRegistry


def build_builtin_registry() -> AlgorithmRegistry:
    from app.algorithms.iapf.runner import IapfAlgorithm
    from app.algorithms.theta_beta.runner import ThetaBetaAlgorithm

    registry = AlgorithmRegistry()
    registry.register(IapfAlgorithm())
    registry.register(ThetaBetaAlgorithm())
    return registry
