"""Deterministic signal-domain primitives. Inputs and outputs remain in volts."""

from __future__ import annotations

from fractions import Fraction

import numpy as np
from scipy import signal

from app.scientific.quality.spectral import evaluate_spectral_window

from .types import (
    ChannelMap, EEGSignal, PrimitiveValueError, QualityMask, TimeRange,
    WindowedSignal, append_provenance,
)
from .units import Unit


def select_channels(source: EEGSignal, channels: tuple[str, ...] | list[str]) -> EEGSignal:
    indices = source.channels.indices(channels)
    return EEGSignal(source.values[:, indices], source.sfreq_hz, source.channels.select(channels), source.time,
                     source.unit, source.quality, append_provenance(source.provenance, "channel_select", channels=tuple(channels)))


def rereference(source: EEGSignal, method: str = "average", reference_channel: str | None = None) -> EEGSignal:
    if method == "average":
        reference = source.values.mean(axis=1, keepdims=True)
    elif method == "channel" and reference_channel:
        reference = source.values[:, [source.channels.indices([reference_channel])[0]]]
    else:
        raise PrimitiveValueError("rereference method must be average or a present reference channel")
    return EEGSignal(source.values - reference, source.sfreq_hz, source.channels, source.time, source.unit,
                     source.quality, append_provenance(source.provenance, "rereference", method=method, reference_channel=reference_channel))


def bandpass(source: EEGSignal, low_hz: float, high_hz: float, order: int = 4, phase: str = "zero_phase") -> EEGSignal:
    if source.unit != Unit.V or not 0 < low_hz < high_hz < source.sfreq_hz / 2 or order <= 0:
        raise PrimitiveValueError("bandpass requires V data and valid bounds below Nyquist")
    sos = signal.butter(order, [low_hz, high_hz], btype="bandpass", fs=source.sfreq_hz, output="sos")
    try:
        values = signal.sosfiltfilt(sos, source.values, axis=0) if phase == "zero_phase" else signal.sosfilt(sos, source.values, axis=0)
    except ValueError as exc:
        raise PrimitiveValueError("signal is too short for the selected filter") from exc
    if phase not in ("zero_phase", "causal"):
        raise PrimitiveValueError("phase must be zero_phase or causal")
    return EEGSignal(values, source.sfreq_hz, source.channels, source.time, source.unit, source.quality,
                     append_provenance(source.provenance, "bandpass", low_hz=low_hz, high_hz=high_hz, order=order, phase=phase))


def notch(source: EEGSignal, frequency_hz: float, quality_factor: float = 30.0, phase: str = "zero_phase") -> EEGSignal:
    if source.unit != Unit.V or not 0 < frequency_hz < source.sfreq_hz / 2 or quality_factor <= 0:
        raise PrimitiveValueError("notch requires V data and a valid frequency")
    b, a = signal.iirnotch(frequency_hz, quality_factor, fs=source.sfreq_hz)
    try:
        values = signal.filtfilt(b, a, source.values, axis=0) if phase == "zero_phase" else signal.lfilter(b, a, source.values, axis=0)
    except ValueError as exc:
        raise PrimitiveValueError("signal is too short for the selected notch filter") from exc
    if phase not in ("zero_phase", "causal"):
        raise PrimitiveValueError("phase must be zero_phase or causal")
    return EEGSignal(values, source.sfreq_hz, source.channels, source.time, source.unit, source.quality,
                     append_provenance(source.provenance, "notch", frequency_hz=frequency_hz, quality_factor=quality_factor, phase=phase))


def resample(source: EEGSignal, target_sfreq_hz: float) -> EEGSignal:
    """Polyphase resampling is explicitly anti-aliased by scipy's FIR filter."""
    if source.unit != Unit.V or target_sfreq_hz <= 0:
        raise PrimitiveValueError("resample requires V data and a positive target rate")
    fraction = Fraction(target_sfreq_hz / source.sfreq_hz).limit_denominator(10_000)
    up, down = fraction.numerator, fraction.denominator
    realized_sfreq_hz = source.sfreq_hz * up / down
    values = signal.resample_poly(source.values, up, down, axis=0)
    duration_s = len(values) / realized_sfreq_hz
    time = TimeRange(source.time.start_s, source.time.start_s + duration_s)
    return EEGSignal(values, realized_sfreq_hz, source.channels, time, source.unit, source.quality,
                     append_provenance(source.provenance, "resample", requested_sfreq_hz=target_sfreq_hz,
                                       realized_sfreq_hz=realized_sfreq_hz, method="resample_poly_fir_antialias", up=up, down=down))


def detrend(source: EEGSignal, kind: str = "constant") -> EEGSignal:
    if kind not in ("constant", "linear"):
        raise PrimitiveValueError("detrend kind must be constant or linear")
    return EEGSignal(signal.detrend(source.values, axis=0, type=kind), source.sfreq_hz, source.channels, source.time,
                     source.unit, source.quality, append_provenance(source.provenance, "detrend", kind=kind))


def window(source: EEGSignal, length_s: float, step_s: float, residual_policy: str = "drop") -> WindowedSignal:
    if length_s <= 0 or step_s <= 0 or residual_policy not in ("drop", "reject"):
        raise PrimitiveValueError("window requires positive length/step and drop or reject residual policy")
    length_samples = int(round(length_s * source.sfreq_hz))
    step_samples = int(round(step_s * source.sfreq_hz))
    if length_samples <= 0 or step_samples <= 0:
        raise PrimitiveValueError("window settings round to zero samples")
    starts = list(range(0, len(source.values) - length_samples + 1, step_samples))
    residual = (len(source.values) - length_samples) % step_samples if len(source.values) >= length_samples else len(source.values)
    if residual_policy == "reject" and residual:
        raise PrimitiveValueError("signal contains a residual window")
    if not starts:
        raise PrimitiveValueError("signal is shorter than one complete window")
    values = np.stack([source.values[start:start + length_samples] for start in starts])
    ranges = tuple(TimeRange(source.time.start_s + start / source.sfreq_hz, source.time.start_s + (start + length_samples) / source.sfreq_hz) for start in starts)
    quality = tuple(QualityMask(check.status, check.reasons) for check in (evaluate_spectral_window(item, length_samples) for item in values))
    return WindowedSignal(values, source.sfreq_hz, source.channels, ranges, length_samples / source.sfreq_hz,
                          step_samples / source.sfreq_hz, residual_policy, source.unit, quality,
                          append_provenance(source.provenance, "window", length_s=length_s, step_s=step_s, residual_policy=residual_policy))
