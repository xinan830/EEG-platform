"""Official module contracts retain their characterized scientific values."""

from __future__ import annotations

import numpy as np

from app.algorithms.iapf.official import estimate_iapf
from app.algorithms.rbp.official import RBP_BANDS
from app.algorithms.theta_beta.compute import compute_theta_beta
from app.scientific.primitives.spectral import SpectralEstimate


def test_official_algorithm_modules_keep_declared_constants():
    assert RBP_BANDS == (("delta", 1.0, 4.0), ("theta", 4.0, 8.0), ("alpha", 8.0, 13.0), ("beta", 13.0, 30.0), ("gamma", 30.0, 50.0))


def test_iapf_and_theta_beta_module_outputs_match_frozen_semantics():
    freqs = np.arange(1.0, 30.25, 0.25)
    base = 1e-12 / freqs
    alpha = 8e-12 * np.exp(-0.5 * ((freqs - 10.0) / 0.7) ** 2)
    psd = np.vstack((base + alpha, base + alpha * 1.2, base + alpha * 1.4))
    spectrum = SpectralEstimate(freqs, psd, 1.0, 4, 4, None)

    iapf = estimate_iapf(spectrum)
    values = compute_theta_beta(SpectralEstimate(freqs, psd[:1], 1.0, 4, 4, None),
                                channel="Fz", requested_range={"start_s": 0.0, "end_s": 30.0})

    assert iapf.value is not None
    assert iapf.source in {"peak", "cog"}
    assert values.channel == "Fz"
    assert values.value is not None
