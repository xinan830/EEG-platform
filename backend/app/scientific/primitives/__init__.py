"""Reusable signal-processing primitives."""

from .spectral import (
    SpectralEstimate,
    band_power,
    estimate_spectrogram,
    estimate_spectrogram_with_quality,
    estimate_welch_psd,
    preprocess_offline,
)
from .gateway import DEFAULT_SPECTRAL_GATEWAY, ScientificSpectralGateway

__all__ = [
    "SpectralEstimate",
    "band_power",
    "estimate_spectrogram",
    "estimate_spectrogram_with_quality",
    "estimate_welch_psd",
    "preprocess_offline",
    "DEFAULT_SPECTRAL_GATEWAY",
    "ScientificSpectralGateway",
]
