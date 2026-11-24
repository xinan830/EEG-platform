"""RBP-owned interval projections of an already computed V²/Hz spectrum."""

import numpy as np

from app.scientific.primitives.spectral import band_power
from .amplitude import band_rms_uv
from .official import RBP_BANDS


def saved_denominators_uv2(arrays: dict, channel_count: int) -> list[float | None]:
    """Reuse the official five-band 1–50 Hz denominator, in µV²."""
    powers = []
    for name, _, _ in RBP_BANDS:
        try:
            column = np.asarray(arrays.get(f"{name}_absolute_power"), dtype=float)
        except (TypeError, ValueError):
            return [None] * channel_count
        if column.shape != (channel_count,):
            return [None] * channel_count
        powers.append(column)
    values = np.stack(powers)
    total = np.sum(values, axis=0)
    valid = np.isfinite(values).all(axis=0) & (values >= 0).all(axis=0) & (total > 0)
    return [float(value) if usable else None for value, usable in zip(total, valid)]


def project_interval(frequencies: np.ndarray, spectrum: np.ndarray | None, denominator_uv2: float | None,
                     low_hz: float, high_hz: float) -> tuple[float | None, float | None, float | None]:
    """Return µV² power, percent of saved total and µV RMS; never read EEG or run Welch."""
    if spectrum is None or denominator_uv2 is None or denominator_uv2 <= 0:
        return None, None, None
    power_uv2 = float(band_power(frequencies, spectrum, low_hz, high_hz)) * 1e12
    if not np.isfinite(power_uv2) or power_uv2 < 0:
        return None, None, None
    rms_uv = float(band_rms_uv(np.asarray(power_uv2)))
    return power_uv2, power_uv2 / denominator_uv2 * 100.0, rms_uv


def project_bins(frequencies: np.ndarray, spectra: np.ndarray, denominators: list[float | None],
                 bounds: list[tuple[float, float]]) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Batch channels through the same exact-edge primitive used by interval projection."""
    total = np.asarray(denominators, dtype=float)
    valid = (np.isfinite(spectra).all(axis=1) & (spectra >= 0).all(axis=1) &
             np.isfinite(total) & (total > 0))
    powers = np.full((len(total), len(bounds)), np.nan)
    if valid.any():
        usable = spectra[valid]
        for index, (low, high) in enumerate(bounds):
            powers[valid, index] = band_power(frequencies, usable, low, high) * 1e12
    powers[~np.isfinite(powers) | (powers < 0)] = np.nan
    relative = np.full_like(powers, np.nan)
    np.divide(powers, total[:, None], out=relative, where=valid[:, None])
    return powers, relative * 100.0, band_rms_uv(powers)
