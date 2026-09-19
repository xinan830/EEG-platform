"""Official algorithm module boundaries must preserve the legacy math exactly."""

from __future__ import annotations

import numpy as np

from app.eeg_core.official_algorithms.brainbeat import segment_brainbeat
from app.eeg_core.official_algorithms.faa import LEGACY_FAA_INITIAL_DISCARD_S, compute_faa
from app.eeg_core.official_algorithms.iapf import estimate_iapf
from app.eeg_core.official_algorithms.rbp import RBP_BANDS
from app.eeg_core.official_algorithms.theta_beta import metric_values
from app.eeg_core.spectral import SpectralEstimate


def test_official_algorithm_modules_keep_legacy_callable_identities():
    """The legacy files become facades; their public calculations live in the package."""
    from app.eeg_core.faa import compute_faa as legacy_faa
    from app.eeg_core.offline_metrics import estimate_iapf as legacy_iapf
    from app.eeg_core.offline_metrics import metric_values as legacy_metrics
    from app.eeg_core.realtime_spectral import segment_brainbeat as legacy_brainbeat

    assert compute_faa is legacy_faa
    assert estimate_iapf is legacy_iapf
    assert metric_values is legacy_metrics
    assert segment_brainbeat is legacy_brainbeat
    assert RBP_BANDS == (("delta", 1.0, 4.0), ("theta", 4.0, 8.0), ("alpha", 8.0, 13.0), ("beta", 13.0, 30.0))
    from app.eeg_core.faa import FAA_DISCARD_S
    assert FAA_DISCARD_S == LEGACY_FAA_INITIAL_DISCARD_S == 12.0


def test_iapf_and_theta_beta_module_outputs_match_frozen_semantics():
    freqs = np.arange(1.0, 30.25, 0.25)
    base = 1e-12 / freqs
    alpha = 8e-12 * np.exp(-0.5 * ((freqs - 10.0) / 0.7) ** 2)
    psd = np.vstack((base + alpha, base + alpha * 1.2, base + alpha * 1.4))
    spectrum = SpectralEstimate(freqs, psd, 1.0, 4, 4, None)

    iapf = estimate_iapf(spectrum)
    values = metric_values(spectrum, float(iapf.value))

    assert iapf.value is not None
    assert iapf.source in {"peak", "cog"}
    assert list(values["fatigue"]) == ["Fz", "Pz", "Oz"]
