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
from .fourier import RealFourierTransform, real_fft
from .block_filter import preprocess_blocks

__all__ = [
    "SpectralEstimate",
    "band_power",
    "estimate_spectrogram",
    "estimate_spectrogram_with_quality",
    "estimate_welch_psd",
    "preprocess_offline",
    "DEFAULT_SPECTRAL_GATEWAY",
    "ScientificSpectralGateway",
    "RealFourierTransform",
    "real_fft",
    "preprocess_blocks",
]
