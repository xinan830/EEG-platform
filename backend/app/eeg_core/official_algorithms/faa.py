"""Compatibility facade for the migrated official FAA implementation."""

from app.algorithms.faa.official import (
    ARTIFACT_THRESHOLD_UV,
    FAA_BAND,
    FAA_CHANNELS,
    FAA_DISCARD_S,
    FAA_EPOCH_OVERLAP,
    FAA_EPOCH_S,
    FAA_MIN_CLEAN_EPOCHS,
    FAA_PERIOD_CAP_S,
    LEGACY_FAA_INITIAL_DISCARD_S,
    compute_faa,
    faa_epoch_bounds,
)

__all__ = [
    "ARTIFACT_THRESHOLD_UV", "FAA_BAND", "FAA_CHANNELS", "FAA_DISCARD_S",
    "FAA_EPOCH_OVERLAP", "FAA_EPOCH_S", "FAA_MIN_CLEAN_EPOCHS", "FAA_PERIOD_CAP_S",
    "LEGACY_FAA_INITIAL_DISCARD_S", "compute_faa", "faa_epoch_bounds",
]
