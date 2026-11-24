"""RMS amplitude derived from the official RBP band-power result."""

import numpy as np


def band_rms_uv(absolute_power_uv2: np.ndarray) -> np.ndarray:
    """Return RMS in uV from uV^2; invalid or unavailable powers remain NaN."""
    power = np.asarray(absolute_power_uv2, dtype=np.float64)
    result = np.full(power.shape, np.nan, dtype=np.float64)
    valid = np.isfinite(power) & (power >= 0)
    np.sqrt(power, out=result, where=valid)
    return result
