"""Typed gateway for service-level access to scientific primitives."""

from __future__ import annotations

from typing import Any

import numpy as np

from .spectral import (
    SpectralEstimate,
    band_power,
    estimate_spectrogram_with_quality,
    estimate_welch_psd,
    preprocess_offline,
)
from .fourier import RealFourierTransform, real_fft


class ScientificSpectralGateway:
    """Stable service boundary; numerical ownership remains in ``spectral``."""

    def preprocess(self, data: np.ndarray, sfreq_hz: float) -> np.ndarray:
        return preprocess_offline(data, sfreq_hz)

    def welch(self, data: np.ndarray, sfreq_hz: float) -> SpectralEstimate:
        return estimate_welch_psd(data, sfreq_hz)

    def spectrogram(self, data: np.ndarray, sfreq_hz: float) -> tuple[np.ndarray, np.ndarray, np.ndarray, list[dict[str, Any]]]:
        return estimate_spectrogram_with_quality(data, sfreq_hz)

    def integrate_band(self, frequencies_hz: np.ndarray, values: np.ndarray, low_hz: float, high_hz: float) -> np.ndarray | float:
        return band_power(frequencies_hz, values, low_hz, high_hz)

    def fourier(
        self,
        values: np.ndarray,
        sampling_rate_hz: float,
        *,
        start_sample: int = 0,
        transform_length: int | None = None,
    ) -> RealFourierTransform:
        """Expose the canonical FFT without moving science into services."""
        return real_fft(
            values,
            sampling_rate_hz,
            start_sample=start_sample,
            transform_length=transform_length,
        )


DEFAULT_SPECTRAL_GATEWAY = ScientificSpectralGateway()

__all__ = ["DEFAULT_SPECTRAL_GATEWAY", "ScientificSpectralGateway"]
