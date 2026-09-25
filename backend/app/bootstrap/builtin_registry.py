"""Composition root for the executable built-in algorithm modules."""

from __future__ import annotations

from app.algorithm_runtime.registry import AlgorithmRegistry
from app.algorithms.band_ratio.runner import BandRatioAlgorithm
from app.algorithms.faa.runner import FaaAlgorithm
from app.algorithms.iapf.runner import IapfAlgorithm
from app.algorithms.peak_frequency.runner import PeakFrequencyAlgorithm
from app.algorithms.psd.runner import PsdAlgorithm
from app.algorithms.rbp.runner import RbpAlgorithm
from app.algorithms.theta_beta.runner import ThetaBetaAlgorithm


def build_builtin_registry() -> AlgorithmRegistry:
    """Build the application registry from concrete official modules."""

    registry = AlgorithmRegistry()
    registry.register(BandRatioAlgorithm())
    registry.register(FaaAlgorithm())
    registry.register(IapfAlgorithm())
    registry.register(PeakFrequencyAlgorithm())
    registry.register(PsdAlgorithm())
    registry.register(RbpAlgorithm())
    registry.register(ThetaBetaAlgorithm())
    return registry
