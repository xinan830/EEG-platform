"""Reference compositions demonstrating RBP and FAA with research primitives."""

from __future__ import annotations

from .math_nodes import natural_log, subtract
from .spectral_nodes import band_power, relative_band_power
from .types import PSDSeries, Scalar, append_provenance
from .units import Unit


def fixed_band_rbp(source: PSDSeries, low_hz: float, high_hz: float, total_low_hz: float = 1.0, total_high_hz: float = 30.0):
    """Build relative band power from one PSD without changing its channel order."""
    return relative_band_power(band_power(source, low_hz, high_hz), band_power(source, total_low_hz, total_high_hz))


def frontal_alpha_asymmetry(source: PSDSeries, left_channel: str = "F3", right_channel: str = "F4") -> Scalar:
    """Return ln(alpha F4 power) - ln(alpha F3 power), preserving explicit inputs."""
    alpha = band_power(source, 8.0, 13.0)
    if alpha.values is None:
        return Scalar(None, unit=Unit.DIMENSIONLESS,
                      quality=alpha.quality, provenance=append_provenance(alpha.provenance, "frontal_alpha_asymmetry"))
    left_index, right_index = alpha.channels.indices([left_channel, right_channel])
    left = Scalar(float(alpha.values[left_index]), alpha.unit, alpha.quality, append_provenance(alpha.provenance, "channel_scalar", channel=left_channel))
    right = Scalar(float(alpha.values[right_index]), alpha.unit, alpha.quality, append_provenance(alpha.provenance, "channel_scalar", channel=right_channel))
    return subtract(natural_log(right), natural_log(left))
