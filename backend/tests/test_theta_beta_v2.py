from __future__ import annotations

from types import SimpleNamespace

import numpy as np

from app.algorithm_runtime.executor import AlgorithmRuntime
from app.algorithm_runtime.registry import AlgorithmRegistry
from app.algorithms.theta_beta.runner import ThetaBetaAlgorithm
from app.eeg_core.official_algorithms.iapf import IAPFEstimate


def _recording_with_spectrum(*, gate_failed=None, psd=None):
    freqs = np.linspace(1.0, 30.0, 117)
    spectrum = SimpleNamespace(gate_failed=gate_failed, freqs=freqs, psd=np.asarray(psd if psd is not None else np.ones((1, 117)), dtype=float))

    def load_spectrum(*, start_s, window_s, channels):
        return spectrum

    return SimpleNamespace(id="r1", channel_names=["Fz", "Pz", "O2"], sfreq_hz=500.0, duration_s=30.0, load_spectrum=load_spectrum)


def test_theta_beta_uses_one_selected_raw_channel(monkeypatch) -> None:
    monkeypatch.setattr(
        "app.algorithms.theta_beta.compute.estimate_iapf",
        lambda spectrum: IAPFEstimate(10.0, "peak", None, 0.9, 0.1, 10.0, 10.0),
    )
    registry = AlgorithmRegistry()
    registry.register(ThetaBetaAlgorithm())
    result = AlgorithmRuntime(registry).execute(
        algorithm_id="theta_beta",
        recording=_recording_with_spectrum(),
        config={"channel": "O2", "mode": "static", "start_s": 0, "end_s": 10},
    )
    assert result.channel == "O2"
    assert result.value is not None
    assert result.unit == "dimensionless"
    assert "theta_range_hz" in result.evidence


def test_theta_beta_returns_null_for_failed_psd_gate(monkeypatch) -> None:
    result = ThetaBetaAlgorithm().execute_static(
        ThetaBetaAlgorithm().resolve_inputs(
            _recording_with_spectrum(gate_failed="low_quality"),
            ThetaBetaAlgorithm.config_model(channel="Fz", mode="static", start_s=0, end_s=10),
        ),
        ThetaBetaAlgorithm.config_model(channel="Fz", mode="static", start_s=0, end_s=10),
    )
    assert result.value is None
    assert result.failure is not None
    assert result.failure.code == "PSD_QUALITY_GATE_FAILED"
