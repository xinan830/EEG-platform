from __future__ import annotations

from types import SimpleNamespace

import numpy as np
import pytest

from app.algorithm_runtime.executor import AlgorithmRuntime
from app.algorithm_runtime.registry import AlgorithmRegistry
from app.algorithms.theta_beta.compute import compute_theta_beta
from app.algorithms.theta_beta.runner import ThetaBetaAlgorithm
from app.algorithms.iapf.official import IAPFEstimate
from app.models.official_algorithm_run import OfficialAlgorithmRunConfig
from app.scientific.primitives.spectral import band_power
from app.scientific.quality import SpectralQualityGateError


def _recording_with_spectrum(*, gate_failed=None, psd=None):
    freqs = np.linspace(1.0, 30.0, 117)
    spectrum = SimpleNamespace(gate_failed=gate_failed, freqs=freqs, psd=np.asarray(psd if psd is not None else np.ones((1, 117)), dtype=float))

    def load_spectrum(*, start_s, window_s, channels, **_preprocessing):
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
    assert "theta_range_hz" in result.evidence["extensions"]["theta_beta_evidence"]
    trace = result.evidence["calculation_trace"]
    assert result.evidence["extensions"]["theta_beta_evidence"]["frequency_mode"] == "standard"
    assert trace["formula"] == "Theta 功率 ÷ Beta 功率"
    assert trace["inputs"][0]["range_hz"] == [4.0, 8.0]
    assert trace["inputs"][2]["range_hz"] == [13.0, 30.0]


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


def test_standard_mode_uses_fixed_bands_without_estimating_iapf(monkeypatch) -> None:
    monkeypatch.setattr(
        "app.algorithms.theta_beta.compute.estimate_iapf",
        lambda _spectrum: (_ for _ in ()).throw(AssertionError("standard mode must not estimate IAPF")),
    )
    freqs = np.arange(1.0, 31.0)
    psd = np.ones((1, len(freqs)), dtype=float)
    spectrum = SimpleNamespace(gate_failed=None, freqs=freqs, psd=psd)

    result = compute_theta_beta(
        spectrum, channel="Fz", requested_range={"start_s": 0, "end_s": 30},
    )

    evidence = result.evidence["extensions"]["theta_beta_evidence"]
    assert evidence["frequency_mode"] == "standard"
    assert evidence["theta_range_hz"] == [4.0, 8.0]
    assert evidence["beta_range_hz"] == [13.0, 30.0]
    assert result.value == band_power(freqs, psd[0], 4.0, 8.0) / band_power(freqs, psd[0], 13.0, 30.0)


def test_official_tbr_request_defaults_to_twenty_seconds_and_validates_supported_windows():
    request = OfficialAlgorithmRunConfig.model_validate({
        "algorithm_id": "theta_beta", "mode": "dynamic", "channel": "Fz",
        "time": {"start_s": 0, "end_s": 30},
    })

    assert request.dynamic_window_s == 20
    assert request.runtime_config()["frequency_mode"] == "standard"
    with pytest.raises(ValueError, match="仅支持 20 或 30 秒"):
        ThetaBetaAlgorithm.config_model(**(request.runtime_config() | {"window_s": 10}))


def test_theta_beta_dynamic_keeps_a_rejected_window_and_continues(monkeypatch) -> None:
    monkeypatch.setattr(
        "app.algorithms.theta_beta.compute.estimate_iapf",
        lambda spectrum: IAPFEstimate(10.0, "peak", None, 0.9, 0.1, 10.0, 10.0),
    )
    spectrum = SimpleNamespace(gate_failed=None, freqs=np.linspace(1.0, 30.0, 117), psd=np.ones((1, 117), dtype=float))
    calls = 0

    def load_spectrum(*, start_s, window_s, channels, **_preprocessing):
        nonlocal calls
        calls += 1
        if calls == 1:
            raise SpectralQualityGateError({
                "clean_segments": 0, "total_segments": 1, "clean_ratio": 0.0,
                "gate_failed": "low_quality", "rejected_reasons": ["amplitude_threshold"],
                "evidence": {"gap": {"detected": False}},
            })
        return spectrum

    recording = SimpleNamespace(id="r1", channel_names=["Fz"], sfreq_hz=500.0, duration_s=22.0, load_spectrum=load_spectrum)
    registry = AlgorithmRegistry()
    from app.algorithms.theta_beta.runner import ThetaBetaAlgorithm
    registry.register(ThetaBetaAlgorithm())
    result = AlgorithmRuntime(registry).execute(
        algorithm_id="theta_beta", recording=recording,
        config={"channel": "Fz", "mode": "dynamic", "start_s": 0, "end_s": 22, "window_s": 20, "step_s": 1},
    )
    assert result.values[0] is None
    assert result.quality[0] == "gate_failed"
    assert result.failures[0].code == "PSD_QUALITY_GATE_FAILED"
    assert all(value is not None for value in result.values[1:])
    assert result.states[0] == "Rejected"


def test_theta_beta_dynamic_uses_complete_twenty_second_windows_without_warmup(monkeypatch) -> None:
    monkeypatch.setattr(
        "app.algorithms.theta_beta.compute.estimate_iapf",
        lambda spectrum: (_ for _ in ()).throw(AssertionError("standard mode must not estimate IAPF")),
    )
    windows = []

    def load_spectrum(*, start_s, window_s, channels, **_preprocessing):
        windows.append((start_s, window_s))
        return SimpleNamespace(gate_failed=None, freqs=np.linspace(1.0, 30.0, 117),
                               psd=np.ones((1, 117), dtype=float))

    recording = SimpleNamespace(id="r1", channel_names=["Fz"], sfreq_hz=500.0,
                                duration_s=22.0, load_spectrum=load_spectrum)
    registry = AlgorithmRegistry()
    registry.register(ThetaBetaAlgorithm())
    result = AlgorithmRuntime(registry).execute(
        algorithm_id="theta_beta", recording=recording,
        config={"channel": "Fz", "mode": "dynamic", "start_s": 0, "end_s": 22,
                "window_s": 20, "step_s": 1},
    )

    assert windows == [(0.0, 20.0), (1.0, 20.0), (2.0, 20.0)]
    assert len(result.values) == 3
    assert all(value is not None for value in result.values)


def test_theta_beta_dynamic_rejects_unsupported_window():
    with pytest.raises(ValueError, match="仅支持 20 或 30 秒"):
        ThetaBetaAlgorithm.config_model(channel="Fz", mode="dynamic", start_s=0, end_s=30,
                                        window_s=10, step_s=1)
