"""Finite-run inspection filters shared by offline scientific preparation."""

import numpy as np
from scipy import signal


def detrend_and_filter_for_inspection(
    values_v: np.ndarray,
    sfreq_hz: float,
    *,
    low_hz: float | None = 0.5,
    high_hz: float | None = 45.0,
    notch_hz: float | None = 50.0,
    notch_q: float = 30.0,
) -> tuple[np.ndarray, np.ndarray]:
    """Return (bandpassed_for_eeg_v, broadband_notched_for_emg_v) preserving NaN gaps."""
    data = np.asarray(values_v, dtype=np.float64)
    nyquist = sfreq_hz / 2.0
    if data.shape[0] < 32 or nyquist <= 2.0:
        return data.copy(), data.copy()

    def _apply_columnwise(arr: np.ndarray, fn) -> np.ndarray:
        out = arr.copy()
        for ch in range(arr.shape[1]):
            col = arr[:, ch]
            mask = np.isfinite(col)
            if not np.any(mask):
                continue
            padded = np.concatenate(([False], mask, [False]))
            diff = np.diff(padded.astype(np.int8))
            for s, e in zip(np.flatnonzero(diff == 1), np.flatnonzero(diff == -1), strict=True):
                if int(e - s) >= 32:
                    try:
                        out[int(s):int(e), ch] = fn(col[int(s):int(e)])
                    except ValueError:
                        pass
        return out

    broadband = data
    if low_hz is not None and 0.0 < low_hz < nyquist:
        sos_hp = signal.butter(4, low_hz, btype="highpass", fs=sfreq_hz, output="sos")
        broadband = _apply_columnwise(broadband, lambda x: signal.sosfiltfilt(sos_hp, x))
    if notch_hz is not None and 1.0 < notch_hz < nyquist - 1.0:
        b_n, a_n = signal.iirnotch(notch_hz, float(notch_q), fs=sfreq_hz)
        broadband = _apply_columnwise(broadband, lambda x: signal.filtfilt(b_n, a_n, x))

    eeg_band = broadband
    if high_hz is not None and (low_hz or 0.0) < high_hz < nyquist - 1.0:
        sos_lp = signal.butter(4, high_hz, btype="lowpass", fs=sfreq_hz, output="sos")
        eeg_band = _apply_columnwise(eeg_band, lambda x: signal.sosfiltfilt(sos_lp, x))
    return eeg_band, broadband
