"""Single composition list for executable built-in algorithms.

The module packages own their manifests and runners.  This file is the one
composition boundary that turns those packages into the built-in runtime
catalog; consumers must derive their registries from this list instead of
maintaining a second algorithm-by-algorithm list.
"""

from __future__ import annotations

from collections.abc import Callable

from app.algorithm_runtime.contracts import AlgorithmManifest, AlgorithmModule
from app.algorithms.band_ratio.runner import BandRatioAlgorithm
from app.algorithms.brainbeat.runner import BrainbeatAlgorithm
from app.algorithms.faa.runner import FaaAlgorithm
from app.algorithms.iapf.runner import IapfAlgorithm
from app.algorithms.peak_frequency.runner import PeakFrequencyAlgorithm
from app.algorithms.psd.runner import PsdAlgorithm
from app.algorithms.rbp.runner import RbpAlgorithm
from app.algorithms.stft.runner import StftAlgorithm
from app.algorithms.theta_beta.runner import ThetaBetaAlgorithm


BUILTIN_ALGORITHM_FACTORIES: tuple[Callable[[], AlgorithmModule], ...] = (
    BandRatioAlgorithm,
    BrainbeatAlgorithm,
    FaaAlgorithm,
    IapfAlgorithm,
    PeakFrequencyAlgorithm,
    PsdAlgorithm,
    RbpAlgorithm,
    StftAlgorithm,
    ThetaBetaAlgorithm,
)


def build_builtin_modules() -> tuple[AlgorithmModule, ...]:
    return tuple(factory() for factory in BUILTIN_ALGORITHM_FACTORIES)


BUILTIN_ALGORITHM_MANIFESTS: tuple[AlgorithmManifest, ...] = tuple(
    factory().manifest for factory in BUILTIN_ALGORITHM_FACTORIES
)
