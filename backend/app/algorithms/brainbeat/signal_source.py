"""Brainbeat-owned construction of its ordered Fz/Pz spectral input."""

from __future__ import annotations

import numpy as np

from app.algorithm_runtime.contracts import AlgorithmInputs
from app.algorithm_runtime.signal_source import SpectralSourceRequest


def load_brainbeat_spectrum(
    inputs: AlgorithmInputs,
    *,
    fz_channel: str,
    pz_channel: str,
    start_s: float,
    end_s: float,
):
    """Load one paired Welch estimate without hiding role or sample order."""
    if not hasattr(inputs.payload, "load_spectral_source"):
        raise TypeError("Brainbeat requires the typed spectral source boundary")
    sfreq = inputs.sfreq_hz
    start_sample = max(0, int(np.floor(start_s * sfreq)))
    end_sample = min(
        int(round(inputs.duration_s * sfreq)),
        start_sample + int(round((end_s - start_s) * sfreq)),
    )
    montage_id, montage_fingerprint = inputs.payload.signal_source_identity()
    request = SpectralSourceRequest(
        roles=("Fz", "Pz"),
        start_sample=start_sample,
        end_sample=end_sample,
        expected_sfreq_hz=sfreq,
        montage_id=montage_id,
        montage_fingerprint=montage_fingerprint,
        filter_low_hz=1.0,
        filter_high_hz=30.0,
        output_low_hz=1.0,
        output_high_hz=30.0,
        notch_hz=None,
    )
    return inputs.payload.load_spectral_source(
        request,
        role_to_channel={"Fz": fz_channel, "Pz": pz_channel},
    )
