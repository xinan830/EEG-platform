"""Analytical checks for the diagnostic reference, not production changes."""

import numpy as np
import pytest

from scripts import recording_audit_reference as ref


def test_reference_psd_has_expected_sine_power_and_frequency():
    fs = 250
    time = np.arange(fs * 12) / fs
    values = (10e-6 * np.sin(2 * np.pi * 10 * time))[:, None]
    freq, density, valid, total = ref.welch(values, fs, 1, 50)
    assert valid == total == 5
    assert freq[np.argmax(density[0])] == 10
    assert ref.integrate(freq, density[0], 8, 13) == pytest.approx(50e-12, rel=1e-12)
    # The declared 1e-20 density floor contributes power outside the sine bins.
    alpha_power = 50e-12 + 4.25e-20
    total_power = alpha_power + 44e-20
    assert ref.shares(freq, density)[2] == pytest.approx(alpha_power / total_power, abs=1e-12)


def test_reference_quality_does_not_turn_rejected_data_into_zero():
    values = np.zeros((4000, 1))
    freq, density, valid, total = ref.welch(values, 1000, 1, 50)
    assert freq is density is None
    assert valid == 0 and total == 1


def test_reference_iapf_ols_and_faa_log_ratio():
    freq = np.arange(1, 30.25, .25)
    density = (2e-12 / freq ** 1.3 + 1e-11 * np.exp(-((freq - 10) / .3) ** 2))[None, :]
    assert ref.iapf(freq, density) == 10
    fs = 250
    time = np.arange(fs * 20) / fs
    left = 10e-6 * np.sin(2 * np.pi * 10 * time)
    assert ref.faa(np.column_stack([left, 2 * left]), fs) == pytest.approx(np.log(4), abs=1e-12)
