"""Construction of the current built-in executable algorithm registry."""

from __future__ import annotations

from .registry import AlgorithmRegistry


def build_builtin_registry() -> AlgorithmRegistry:
    from app.algorithms.faa.runner import FaaAlgorithm
    from app.algorithms.iapf.runner import IapfAlgorithm
    from app.algorithms.rbp.runner import RbpAlgorithm
    from app.algorithms.theta_beta.runner import ThetaBetaAlgorithm

    registry = AlgorithmRegistry()
    registry.register(FaaAlgorithm())
    registry.register(IapfAlgorithm())
    registry.register(RbpAlgorithm())
    registry.register(ThetaBetaAlgorithm())
    return registry
