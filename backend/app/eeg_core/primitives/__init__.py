"""Safe, typed research primitives for future backend-only algorithm execution."""

from .compositions import fixed_band_rbp, frontal_alpha_asymmetry
from .registry import NODE_REGISTRY, UnknownNodeError, resolve_node
from .types import (
    BandPower, ChannelMap, EEGSignal, PSDSeries, QualityMask, RelativePower,
    Scalar, TimeRange, TimeSeries, WindowedSignal,
)
from .units import Unit, UnitError

__all__ = [
    "BandPower", "ChannelMap", "EEGSignal", "NODE_REGISTRY", "PSDSeries", "QualityMask",
    "RelativePower", "Scalar", "TimeRange", "TimeSeries", "Unit", "UnitError", "UnknownNodeError",
    "WindowedSignal", "fixed_band_rbp", "frontal_alpha_asymmetry", "resolve_node",
]
