"""Explicit Fourier-transform primitive for V-valued EEG windows.

This module owns only the mathematical transform and its evidence. Filtering,
quality gates, channel selection, and display-unit conversion remain in their
respective boundaries.
"""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np

from app.scientific.contracts.types import SampleRange, TransformPaddingEvidence


@dataclass(frozen=True)
class RealFourierTransform:
    """One-sided FFT result in SI units, ordered as samples x channels."""

    frequencies_hz: np.ndarray
    coefficients_v: np.ndarray
    sample_range: SampleRange
    sampling_rate_hz: float
    transform_length: int
    transform_padding: TransformPaddingEvidence

    def __post_init__(self) -> None:
        frequencies = np.asarray(self.frequencies_hz)
        coefficients = np.asarray(self.coefficients_v)
        if frequencies.ndim != 1 or coefficients.ndim != 2:
            raise ValueError("Fourier output must contain a 1D frequency axis and 2D channel coefficients")
        if coefficients.shape[0] != frequencies.shape[0]:
            raise ValueError("Fourier frequency and coefficient axes must have the same length")
        if not np.iscomplexobj(coefficients) or not np.isfinite(coefficients).all():
            raise ValueError("Fourier coefficients must be finite complex values")
        if not np.isfinite(frequencies).all() or np.any(np.diff(frequencies) < 0):
            raise ValueError("Fourier frequencies must be finite and ascending")
        if self.sampling_rate_hz <= 0 or self.transform_length < self.sample_range.length:
            raise ValueError("invalid Fourier sampling rate or transform length")

        frequencies.setflags(write=False)
        coefficients.setflags(write=False)
        object.__setattr__(self, "frequencies_hz", frequencies)
        object.__setattr__(self, "coefficients_v", coefficients)


def real_fft(
    values: np.ndarray,
    sampling_rate_hz: float,
    *,
    start_sample: int = 0,
    transform_length: int | None = None,
) -> RealFourierTransform:
    """Compute an explicit one-sided FFT without modifying acquisition data.

    ``values`` is shaped ``(samples,)`` or ``(samples, channels)`` and is
    interpreted as volts. A larger transform length applies mathematical
    zero-padding and records it as transform evidence. Non-finite input is a
    recording-gap error, not a padding candidate.
    """

    array = np.asarray(values, dtype=np.float64)
    if array.ndim == 1:
        array = array[:, None]
    if array.ndim != 2 or array.shape[0] == 0 or array.shape[1] == 0:
        raise ValueError("Fourier input must be a non-empty 1D or 2D sample array")
    if not np.isfinite(float(sampling_rate_hz)) or sampling_rate_hz <= 0:
        raise ValueError("sampling rate must be a finite positive number")
    if not np.isfinite(array).all():
        raise ValueError("Fourier input contains non-finite samples; recording gap cannot be imputed")
    if start_sample < 0:
        raise ValueError("start_sample cannot be negative")

    sample_count = int(array.shape[0])
    if transform_length is None:
        length = sample_count
    elif isinstance(transform_length, bool) or int(transform_length) != transform_length:
        raise ValueError("transform_length must be an integer")
    else:
        length = int(transform_length)
    if length < sample_count or length <= 0:
        raise ValueError("transform_length must be at least the input sample count")

    coefficients = np.fft.rfft(array, n=length, axis=0)
    frequencies = np.fft.rfftfreq(length, d=1.0 / float(sampling_rate_hz))
    padding = TransformPaddingEvidence(
        used=length > sample_count,
        kind="fft_boundary" if length > sample_count else "none",
        samples=length - sample_count,
    )
    return RealFourierTransform(
        frequencies_hz=frequencies,
        coefficients_v=coefficients,
        sample_range=SampleRange(start_sample, start_sample + sample_count),
        sampling_rate_hz=float(sampling_rate_hz),
        transform_length=length,
        transform_padding=padding,
    )
