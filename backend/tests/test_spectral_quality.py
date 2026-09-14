import numpy as np

from app.eeg_core.quality import evaluate_spectral_window
from app.eeg_core.spectral import estimate_welch_psd


def test_quality_reports_all_stable_reason_codes():
    expected = 100
    missing = evaluate_spectral_window(np.ones((99, 1)) * 2e-6, expected)
    non_finite = evaluate_spectral_window(np.full((expected, 1), np.nan), expected)
    amplitude = evaluate_spectral_window(np.linspace(-200e-6, 200e-6, expected)[:, None], expected)
    flatline = evaluate_spectral_window(np.ones((expected, 1)) * 2e-6, expected)
    clipped_values = np.linspace(-10e-6, 10e-6, expected)
    clipped_values[:4] = clipped_values.min()
    clipped = evaluate_spectral_window(clipped_values[:, None], expected)

    assert "missing_samples" in missing.reasons
    assert "non_finite" in non_finite.reasons
    assert "amplitude_threshold" in amplitude.reasons
    assert "flatline" in flatline.reasons
    assert "clipping" in clipped.reasons


def test_quality_gate_keeps_rejected_output_unavailable_not_zero():
    result = estimate_welch_psd(np.ones((400, 1)) * 2e-6, 100.0)

    assert result.gate_failed == "low_quality"
    assert "flatline" in result.rejected_reasons
    assert result.psd.shape == (1, 0)
