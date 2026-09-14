"""Backend-only verification for the constrained research primitive layer."""

from __future__ import annotations

import numpy as np
import pytest

from app.eeg_core.primitives import (
    ChannelMap, EEGSignal, QualityMask, TimeRange, Unit, UnitError,
    fixed_band_rbp, frontal_alpha_asymmetry, resolve_node,
)
from app.eeg_core.primitives.math_nodes import add, divide
from app.eeg_core.primitives.registry import UnknownNodeError
from app.eeg_core.primitives.signal_nodes import resample, select_channels, window
from app.eeg_core.primitives.spectral_nodes import band_power, welch_psd
from app.eeg_core.primitives.types import Scalar
from app.eeg_core.spectral import estimate_welch_psd


def _signal(duration_s: float = 30.0, sfreq_hz: float = 100.0) -> EEGSignal:
    times = np.arange(round(duration_s * sfreq_hz), dtype=float) / sfreq_hz
    values = np.column_stack((
        10e-6 * np.sin(2 * np.pi * 10 * times),
        20e-6 * np.sin(2 * np.pi * 10 * times),
        8e-6 * np.sin(2 * np.pi * 6 * times),
    ))
    return EEGSignal(values, sfreq_hz, ChannelMap(("F3", "F4", "Oz")), TimeRange(0.0, duration_s))


def test_values_are_copied_read_only_and_channel_selection_preserves_requested_order():
    source = _signal()
    source_values = source.values.copy()
    with pytest.raises(ValueError):
        source.values[0, 0] = 0.0

    selected = select_channels(source, ["Oz", "F3"])

    assert selected.channels.labels == ("Oz", "F3")
    np.testing.assert_array_equal(source.values, source_values)
    assert selected.provenance[-1].node == "channel_select"


def test_units_reject_implicit_conversion_and_incompatible_addition():
    with pytest.raises(UnitError, match="implicit"):
        add(Scalar(1.0, Unit.V2), Scalar(1.0, Unit.UV2))
    with pytest.raises(UnitError):
        add(Scalar(1.0, Unit.V2), Scalar(1.0, Unit.HZ))


def test_window_records_absolute_centers_and_drop_or_reject_residual_policy():
    source = _signal(duration_s=10.0)
    windows = window(source, 4.0, 2.0, residual_policy="drop")

    assert [item.center_s for item in windows.ranges] == [2.0, 4.0, 6.0, 8.0]
    assert windows.values.shape == (4, 400, 3)
    assert windows.residual_policy == "drop"

    odd_length = _signal(duration_s=9.0)
    with pytest.raises(ValueError, match="residual"):
        window(odd_length, 4.0, 2.0, residual_policy="reject")


def test_polyphase_resampling_is_anti_aliased_and_records_realized_rate():
    sfreq_hz = 200.0
    times = np.arange(10 * int(sfreq_hz), dtype=float) / sfreq_hz
    high_frequency = 1e-6 * np.sin(2 * np.pi * 70 * times)
    source = EEGSignal(high_frequency[:, None], sfreq_hz, ChannelMap(("F3",)), TimeRange(0.0, 10.0))

    result = resample(source, 100.0)

    assert result.sfreq_hz == 100.0
    assert dict(result.provenance[-1].parameters)["requested_sfreq_hz"] == "100.0"
    assert np.std(result.values[100:-100, 0]) < 0.1e-6


def test_primitive_welch_matches_frozen_offline_spectral_v3_point_by_point():
    source = _signal()
    primitive = welch_psd(window(source, 4.0, 2.0))
    legacy = estimate_welch_psd(source.values, source.sfreq_hz)

    assert primitive.quality.is_available
    np.testing.assert_allclose(primitive.frequencies_hz, legacy.freqs, rtol=0.0, atol=0.0)
    np.testing.assert_allclose(primitive.values, legacy.psd, rtol=1e-12, atol=1e-24)


def test_fixed_band_rbp_sums_to_one_and_faa_is_log_right_minus_left():
    source = _signal()
    psd = welch_psd(window(source, 4.0, 2.0))
    bands = [fixed_band_rbp(psd, *band) for band in ((1.0, 4.0), (4.0, 8.0), (8.0, 13.0), (13.0, 30.0))]
    combined = np.sum([band.values for band in bands], axis=0)

    np.testing.assert_allclose(combined, np.ones(3), rtol=1e-10, atol=1e-12)
    # v3 floors very small density bins at 1e-20 before integration; retain
    # that production contract while accepting its sub-nanounit log effect.
    assert frontal_alpha_asymmetry(psd).value == pytest.approx(np.log(4.0), abs=1e-8)


def test_quality_gate_returns_unavailable_values_never_fake_zero():
    flat = EEGSignal(np.full((400, 1), 2e-6), 100.0, ChannelMap(("F3",)), TimeRange(0.0, 4.0))
    psd = welch_psd(window(flat, 4.0, 2.0))
    alpha = band_power(psd, 8.0, 13.0)

    assert psd.values is None
    assert alpha.values is None
    assert "flatline" in psd.quality.reasons


def test_accepted_welch_keeps_rejected_window_reasons_for_provenance():
    source = _signal()
    values = source.values.copy()
    values[400, 0] = 300e-6
    artifact = EEGSignal(values, source.sfreq_hz, source.channels, source.time)

    psd = welch_psd(window(artifact, 4.0, 2.0))

    assert psd.quality.is_available
    assert "amplitude_threshold" in psd.quality.rejected_reasons


def test_closed_registry_rejects_unknown_nodes_and_divide_by_zero_is_unavailable():
    assert resolve_node("welch_psd") is welch_psd
    with pytest.raises(UnknownNodeError):
        resolve_node("__import__('os').system")

    result = divide(Scalar(2.0, Unit.V2), Scalar(0.0, Unit.V2))
    assert result.value is None
    assert result.quality.reasons == ("division_by_zero",)
