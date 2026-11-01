"""Current STFT science: finite context, then unchanged four-second PSD frames."""

import math


SPECTROGRAM_VERSION = "spectrogram-v3-bounded"
STFT_PREPROCESSING_VERSION = "spectral-window-v1"
STFT_PREPROCESSING_SCOPE = "bounded_window_with_finite_context"


def stft_context_s(low_hz: float) -> float:
    """Keep at least three low-cut cycles on each side; not an equivalence bound."""
    if not math.isfinite(low_hz) or low_hz <= 0:
        raise ValueError("STFT 低切必须为有限正频率")
    return max(2.0, 3.0 / low_hz)
