"""Compatibility facade for the migrated scientific spectral primitives."""

from app.scientific.primitives.spectral import (
    SpectralEstimate,
    band_power,
    estimate_spectrogram,
    estimate_spectrogram_with_quality,
    estimate_welch_psd,
    preprocess_offline,
)

__all__ = [
    "SpectralEstimate",
    "band_power",
    "estimate_spectrogram",
    "estimate_spectrogram_with_quality",
    "estimate_welch_psd",
    "preprocess_offline",
]
