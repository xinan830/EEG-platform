"""Five-band official RBP scientific contract."""

from __future__ import annotations

from typing import Any

import numpy as np

from app.scientific.primitives.spectral import band_power

RBP_FILTER_LOW_HZ = 1.0
RBP_FILTER_HIGH_HZ = 50.0
RBP_NOTCH_HZ = 50.0

RBP_BANDS = (
    ("delta", 1.0, 4.0),
    ("theta", 4.0, 8.0),
    ("alpha", 8.0, 13.0),
    ("beta", 13.0, 30.0),
    ("gamma", 30.0, 50.0),
)


def five_band_shares(spectrum: Any) -> tuple[dict[str, float], dict[str, float]]:
    """Integrate one V²/Hz PSD and return µV² powers and unitless shares."""
    frequencies = np.asarray(spectrum.freqs, dtype=float)
    density = np.asarray(spectrum.psd[0], dtype=float)
    if (len(frequencies) < 2 or frequencies[0] > RBP_FILTER_LOW_HZ
            or frequencies[-1] < RBP_FILTER_HIGH_HZ or not np.isfinite(density).all()):
        raise ValueError("RBP 频谱未完整覆盖 1–50 Hz")
    powers = {
        name: float(band_power(frequencies, density, low, high) * 1e12)
        for name, low, high in RBP_BANDS
    }
    total = sum(powers.values())
    if not np.isfinite(total) or total <= 0 or any(value < 0 for value in powers.values()):
        raise ValueError("RBP 1–50 Hz 五频段总功率无效")
    return powers, {name: value / total for name, value in powers.items()}


__all__ = ["RBP_BANDS", "RBP_FILTER_LOW_HZ", "RBP_FILTER_HIGH_HZ", "RBP_NOTCH_HZ", "five_band_shares"]
