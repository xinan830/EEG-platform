"""Official RBP adapter over the frozen offline spectral primitives."""

from app.eeg_core.primitives.compositions import fixed_band_rbp

RBP_BANDS = (("delta", 1.0, 4.0), ("theta", 4.0, 8.0), ("alpha", 8.0, 13.0), ("beta", 13.0, 30.0))

__all__ = ["RBP_BANDS", "fixed_band_rbp"]
