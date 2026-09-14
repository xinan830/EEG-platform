"""PSD, band-power and quality primitives with frozen offline-spectral semantics."""

from __future__ import annotations

import numpy as np
from scipy import signal

from app.eeg_core.spectral import band_power as integrate_band_power

from .types import BandPower, PSDSeries, PrimitiveValueError, QualityMask, RelativePower, TimeRange, WindowedSignal, append_provenance
from .units import Unit


def welch_psd(source: WindowedSignal, low_hz: float = 1.0, high_hz: float = 30.0, minimum_clean_ratio: float = 0.75) -> PSDSeries:
    """Average complete clean windows; overlapping input windows define Welch overlap."""
    if source.unit != Unit.V or not 0 <= minimum_clean_ratio <= 1 or not 0 < low_hz < high_hz < source.sfreq_hz / 2:
        raise PrimitiveValueError("Welch requires V windows, valid frequency bounds, and a clean ratio")
    clean_indices = [index for index, quality in enumerate(source.quality) if quality.is_available]
    candidate_reasons = tuple(dict.fromkeys(reason for quality in source.quality for reason in quality.rejected_reasons))
    range_time = TimeRange(source.ranges[0].start_s, source.ranges[-1].end_s)
    window_samples = source.values.shape[1]
    freqs = np.fft.rfftfreq(window_samples, 1.0 / source.sfreq_hz)
    mask = (freqs >= low_hz) & (freqs <= high_hz)
    accepted = len(clean_indices) / len(source.quality)
    provenance = append_provenance(source.provenance, "welch_psd", window="hann", detrend="constant", scaling="density", low_hz=low_hz, high_hz=high_hz, clean_ratio=accepted)
    if not clean_indices or accepted < minimum_clean_ratio:
        reasons = tuple(dict.fromkeys(("low_quality",) + candidate_reasons))
        return PSDSeries(None, freqs[mask], source.channels, range_time, Unit.V2_PER_HZ,
                         QualityMask("bad", reasons, candidate_reasons), provenance)
    spectra = []
    for index in clean_indices:
        frequencies, density = signal.welch(source.values[index], fs=source.sfreq_hz, window="hann", nperseg=window_samples,
                                             noverlap=0, detrend="constant", scaling="density", axis=0)
        spectra.append(density.T)
    # Preserve the frozen v3 public lower floor so shadow parity includes its
    # deterministic serialized-value behaviour, not only the raw Welch math.
    values = np.maximum(np.mean(np.stack(spectra), axis=0)[:, mask], 1e-20)
    return PSDSeries(values, frequencies[mask], source.channels, range_time,
                     Unit.V2_PER_HZ, QualityMask("clean", (), candidate_reasons), provenance)


def band_power(source: PSDSeries, low_hz: float, high_hz: float) -> BandPower:
    provenance = append_provenance(source.provenance, "band_power", low_hz=low_hz, high_hz=high_hz)
    unit = Unit.V2 if source.unit == Unit.V2_PER_HZ else Unit.UV2
    if source.values is None:
        return BandPower(None, source.channels, (low_hz, high_hz), unit, source.quality, provenance)
    try:
        values = np.asarray(integrate_band_power(source.frequencies_hz, source.values, low_hz, high_hz), dtype=np.float64)
    except ValueError as exc:
        raise PrimitiveValueError(str(exc)) from exc
    return BandPower(values, source.channels, (low_hz, high_hz), unit, source.quality, provenance)


def relative_band_power(numerator: BandPower, denominator: BandPower) -> RelativePower:
    if numerator.channels != denominator.channels or numerator.unit != denominator.unit:
        raise PrimitiveValueError("RBP inputs require equal ordered channels and exactly the same unit")
    provenance = append_provenance(numerator.provenance + denominator.provenance, "relative_band_power", denominator_band=denominator.band_hz)
    quality = QualityMask.combine((numerator.quality, denominator.quality))
    if numerator.values is None or denominator.values is None:
        return RelativePower(None, numerator.channels, numerator.band_hz, Unit.RATIO, quality, provenance)
    if np.any(denominator.values <= 0):
        return RelativePower(None, numerator.channels, numerator.band_hz, Unit.RATIO, QualityMask("bad", ("division_by_zero",)), provenance)
    return RelativePower(numerator.values / denominator.values, numerator.channels, numerator.band_hz, Unit.RATIO, quality, provenance)


def quality_gate(source: PSDSeries, minimum_clean_ratio: float = 0.75) -> PSDSeries:
    """A declarative gate for future graph execution; unavailable values never become zero."""
    if not 0 <= minimum_clean_ratio <= 1:
        raise PrimitiveValueError("minimum clean ratio must be between zero and one")
    if source.quality.is_available:
        return source
    return PSDSeries(None, source.frequencies_hz, source.channels, source.time, source.unit, source.quality,
                     append_provenance(source.provenance, "quality_gate", minimum_clean_ratio=minimum_clean_ratio))
