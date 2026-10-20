"""Failure-injection evidence for official offline EEG algorithms."""

from __future__ import annotations

import numpy as np
import pytest

from app.scientific.primitives.spectral import estimate_welch_psd
from app.scientific.quality import evaluate_spectral_window


def _signal(samples: int = 400) -> np.ndarray:
    times = np.arange(samples) / 100.0
    return (12e-6 * np.sin(2 * np.pi * 9.7 * times))[:, None]


@pytest.mark.parametrize(
    ("mutate", "reason"),
    [
        (lambda x: x[:-1], "missing_samples"),
        (lambda x: x.copy(), "amplitude_threshold"),
        (lambda x: x.copy(), "clipping"),
        (lambda x: np.full_like(x, np.nan), "non_finite"),
    ],
)
def test_quality_gate_classifies_injected_bad_windows_without_imputation(mutate, reason):
    values = _signal()
    if reason == "amplitude_threshold":
        values[137, 0] = 250e-6
    elif reason == "clipping":
        values[:8, 0] = 90e-6
    else:
        values = mutate(values)

    assessment = evaluate_spectral_window(values, 400)

    assert assessment.status == "bad"
    assert reason in assessment.reasons
    if reason == "non_finite":
        assert assessment.gap.non_finite_samples == 400


def test_short_or_rejected_welch_never_returns_fabricated_zero_spectrum():
    short = estimate_welch_psd(_signal(399), 100.0)
    saturated = _signal()
    saturated[:8, 0] = 90e-6
    rejected = estimate_welch_psd(saturated, 100.0)

    for result in (short, rejected):
        assert result.gate_failed == "low_quality"
        assert result.freqs.size == 0
        assert result.psd.shape == (1, 0)
        assert result.evidence["gap"]["imputed"] is False
    assert "missing_samples" in short.rejected_reasons
    assert "clipping" in rejected.rejected_reasons
