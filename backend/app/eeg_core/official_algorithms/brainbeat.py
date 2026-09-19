"""Frozen single-frame BrainBeat calculation for the realtime chain."""

from __future__ import annotations

import numpy as np

from app.algorithm_runtime.contracts import AlgorithmManifest
from app.eeg_core.analysis_contract import LIVE_ANALYSIS_CONTRACT


# BrainBeat remains an official, non-runnable descriptor until its stateful
# realtime lifecycle can be represented by a traceable AnalysisRun.
BRAINBEAT_MANIFEST = AlgorithmManifest(
    algorithm_id="brainbeat",
    display_name_zh="脑节律指标",
    abbreviation="BrainBeat",
    purpose_zh="实时链路中的前额 Theta 与顶区 Alpha 相对功率关系；含状态性 EMA warm-up。",
    scientific_version=LIVE_ANALYSIS_CONTRACT["algorithm_version"],
    implementation_identity="realtime-eegprocessor-v1",
    supported_modes=[],
    output_unit="dimensionless",
    definition_name="Official BRAINBEAT",
    execution_kind="official_composite_shadow_only",
    availability="shadow_validation",
    is_runnable=False,
    required_channel_roles=["Fz", "Pz", "IAPF"],
)


def _band_power(freqs, pxx, f_low, f_high) -> float:
    frequencies = np.asarray(freqs, dtype=float)
    power = np.asarray(pxx, dtype=float)
    mask = (frequencies >= f_low) & (frequencies <= f_high)
    if int(mask.sum()) < 2:
        return 0.0
    return float(np.trapezoid(power[mask], frequencies[mask]))


def segment_brainbeat(freqs, fz_psd, pz_psd, iapf, epsilon=1e-20):
    """Compute Fz relative theta divided by Pz relative alpha."""
    theta_low, theta_high = max(4.0, iapf - 6.0), iapf - 2.0
    alpha_low, alpha_high = iapf - 2.0, iapf + 2.0
    total_fz = _band_power(freqs, fz_psd, 1.0, 30.0) + epsilon
    total_pz = _band_power(freqs, pz_psd, 1.0, 30.0) + epsilon
    theta_fz = _band_power(freqs, fz_psd, theta_low, theta_high) / total_fz
    alpha_pz = _band_power(freqs, pz_psd, alpha_low, alpha_high) / total_pz
    return float(theta_fz / (alpha_pz + epsilon))
