from __future__ import annotations

from types import SimpleNamespace

import numpy as np

from app.algorithm_runtime.executor import AlgorithmRuntime
from app.algorithm_runtime.registry import AlgorithmRegistry
from app.algorithms.iapf.runner import IapfAlgorithm
from app.eeg_core.official_algorithms.iapf import IAPFEstimate


def _fake_recording():
    spectrum = SimpleNamespace(
        gate_failed=None,
        freqs=np.linspace(1.0, 30.0, 117),
        psd=np.ones((1, 117), dtype=float),
    )

    def load_spectrum(*, start_s, window_s, channels):
        return spectrum

    return SimpleNamespace(id="r1", channel_names=["Fz", "O2"], sfreq_hz=500.0, duration_s=30.0, load_spectrum=load_spectrum)


def test_iapf_module_preserves_selected_raw_channel(monkeypatch) -> None:
    monkeypatch.setattr(
        "app.algorithms.iapf.compute.estimate_iapf",
        lambda spectrum: IAPFEstimate(10.25, "peak", None, 0.9, 0.1, 10.25, 10.1),
    )
    registry = AlgorithmRegistry()
    registry.register(IapfAlgorithm())
    result = AlgorithmRuntime(registry).execute(
        algorithm_id="iapf",
        recording=_fake_recording(),
        config={"channel": "o2", "mode": "static", "start_s": 0, "end_s": 10},
    )
    assert result.value == 10.25
    assert result.channel == "O2"
    assert result.unit == "Hz"


def test_iapf_dynamic_returns_one_point_per_window(monkeypatch) -> None:
    monkeypatch.setattr(
        "app.algorithms.iapf.compute.estimate_iapf",
        lambda spectrum: IAPFEstimate(10.0, "peak", None, 0.9, 0.1, 10.0, 10.0),
    )
    registry = AlgorithmRegistry()
    registry.register(IapfAlgorithm())
    result = AlgorithmRuntime(registry).execute(
        algorithm_id="iapf",
        recording=_fake_recording(),
        config={"channel": "Fz", "mode": "dynamic", "start_s": 0, "end_s": 10, "window_s": 4, "step_s": 2},
    )
    assert result.time_s == [4.0, 6.0, 8.0, 10.0]
    assert result.values == [10.0, 10.0, 10.0, 10.0]


def test_iapf_dynamic_emits_a_warmup_result_after_one_welch_segment(monkeypatch) -> None:
    monkeypatch.setattr(
        "app.algorithms.iapf.compute.estimate_iapf",
        lambda spectrum: IAPFEstimate(10.0, "peak", None, 0.9, 0.1, 10.0, 10.0),
    )
    registry = AlgorithmRegistry()
    registry.register(IapfAlgorithm())

    result = AlgorithmRuntime(registry).execute(
        algorithm_id="iapf",
        recording=_fake_recording(),
        config={"channel": "Fz", "mode": "dynamic", "start_s": 0, "end_s": 4, "window_s": 10, "step_s": 1},
    )

    assert result.time_s == [4.0]
    assert result.windows == [{"start_s": 0.0, "end_s": 4.0}]
    assert result.warmups == [True]
    assert result.values == [10.0]


def test_iapf_warmup_loads_only_the_actual_available_eeg_range(monkeypatch) -> None:
    monkeypatch.setattr(
        "app.algorithms.iapf.compute.estimate_iapf",
        lambda spectrum: IAPFEstimate(10.0, "peak", None, 0.9, 0.1, 10.0, 10.0),
    )
    calls: list[tuple[float, float]] = []
    spectrum = SimpleNamespace(gate_failed=None, freqs=np.linspace(1.0, 30.0, 117), psd=np.ones((1, 117), dtype=float))

    def load_spectrum(*, start_s, window_s, channels):
        calls.append((start_s, window_s))
        return spectrum

    recording = SimpleNamespace(id="r1", channel_names=["Fz"], sfreq_hz=500.0, duration_s=30.0, load_spectrum=load_spectrum)
    registry = AlgorithmRegistry()
    registry.register(IapfAlgorithm())
    result = AlgorithmRuntime(registry).execute(
        algorithm_id="iapf",
        recording=recording,
        config={"channel": "Fz", "mode": "dynamic", "start_s": 0, "end_s": 8, "window_s": 10, "step_s": 1},
    )

    assert result.windows == [
        {"start_s": 0.0, "end_s": 4.0}, {"start_s": 0.0, "end_s": 5.0},
        {"start_s": 0.0, "end_s": 6.0}, {"start_s": 0.0, "end_s": 7.0},
        {"start_s": 0.0, "end_s": 8.0},
    ]
    assert calls == [(0.0, 4.0), (0.0, 5.0), (0.0, 6.0), (0.0, 7.0), (0.0, 8.0)]


def test_iapf_catchup_keeps_each_warmup_endpoint_before_the_full_window(monkeypatch) -> None:
    monkeypatch.setattr(
        "app.algorithms.iapf.compute.estimate_iapf",
        lambda spectrum: IAPFEstimate(10.0, "peak", None, 0.9, 0.1, 10.0, 10.0),
    )
    registry = AlgorithmRegistry()
    registry.register(IapfAlgorithm())

    result = AlgorithmRuntime(registry).execute(
        algorithm_id="iapf",
        recording=_fake_recording(),
        config={"channel": "Fz", "mode": "dynamic", "start_s": 0, "end_s": 11, "window_s": 10, "step_s": 1},
    )

    assert result.time_s == [4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0, 11.0]
    assert result.windows == [
        {"start_s": 0.0, "end_s": 4.0}, {"start_s": 0.0, "end_s": 5.0},
        {"start_s": 0.0, "end_s": 6.0}, {"start_s": 0.0, "end_s": 7.0},
        {"start_s": 0.0, "end_s": 8.0}, {"start_s": 0.0, "end_s": 9.0},
        {"start_s": 0.0, "end_s": 10.0}, {"start_s": 1.0, "end_s": 11.0},
    ]
    assert result.warmups == [True, True, True, True, True, True, False, False]
