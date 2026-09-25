"""Compatibility facade for the migrated official RBP band contract."""

from app.algorithms.rbp.official import RBP_BANDS
from app.eeg_core.primitives.compositions import fixed_band_rbp

__all__ = ["RBP_BANDS", "fixed_band_rbp"]
