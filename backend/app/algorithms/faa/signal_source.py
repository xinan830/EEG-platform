"""FAA-owned construction of paired bounded input requests."""

from __future__ import annotations

import numpy as np

from app.algorithm_runtime.contracts import AlgorithmInputs
from app.algorithm_runtime.signal_source import SignalSourceRequest

from .config import FaaConfig


def load_faa_source(inputs: AlgorithmInputs, config: FaaConfig, start_s: float, end_s: float):
    """Resolve the configured F3/F4 pair while preserving continuous filtering."""
    sfreq = inputs.sfreq_hz
    montage_id, montage_fingerprint = inputs.payload.signal_source_identity()
    start_sample = max(0, int(np.floor(start_s * sfreq)))
    end_sample = min(int(round(inputs.duration_s * sfreq)), int(round(end_s * sfreq)))
    request = SignalSourceRequest(
        roles=("F3", "F4"), start_sample=start_sample, end_sample=end_sample,
        expected_sfreq_hz=sfreq, montage_id=montage_id,
        montage_fingerprint=montage_fingerprint,
        preprocessing_scope="configured_continuous_before_slice",
        low_hz=config.low_hz, high_hz=config.high_hz, notch_hz=config.notch_hz,
    )
    return inputs.payload.load_signal_source(
        request, role_to_channel={"F3": inputs.channel, "F4": config.f4_channel},
    )
