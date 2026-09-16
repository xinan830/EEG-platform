from __future__ import annotations

from types import SimpleNamespace

import numpy as np
import pytest

from app.algorithm_runtime.executor import AlgorithmRuntime
from app.algorithm_runtime.registry import AlgorithmRegistry
from app.algorithm_runtime.windows import build_playback_windows
from app.algorithms.iapf.runner import IapfAlgorithm
from app.eeg_core.official_algorithms.iapf import IAPFEstimate


def _fake_recording(duration_s: float = 40.0):
    spectrum = SimpleNamespace(
        gate_failed=None,
        freqs=np.linspace(1.0, 30.0, 117),
        psd=np.ones((1, 117), dtype=float),
    )

    def load_spectrum(*, start_s, window_s, channels):
        return spectrum

    return SimpleNamespace(id="r1", channel_names=["Fz", "O2"], sfreq_hz=500.0, duration_s=duration_s, load_spectrum=load_spectrum)


def _runtime(monkeypatch) -> AlgorithmRuntime:
    monkeypatch.setattr(
        "app.algorithms.iapf.compute.estimate_iapf",
        lambda spectrum: IAPFEstimate(10.0, "peak", None, 0.9, 0.1, 10.0, 10.0),
    )
    registry = AlgorithmRegistry()
    registry.register(IapfAlgorithm())
    return AlgorithmRuntime(registry)


def test_iapf_module_preserves_selected_raw_channel(monkeypatch) -> None:
    result = _runtime(monkeypatch).execute(
        algorithm_id="iapf",
        recording=_fake_recording(),
        config={"channel": "o2", "mode": "static", "start_s": 0, "end_s": 10},
    )
    assert result.value == 10.0
    assert result.channel == "O2"
    assert result.unit == "Hz"


def test_iapf_dynamic_emits_no_candidate_before_the_algorithm_window_is_full(monkeypatch) -> None:
    result = _runtime(monkeypatch).execute(
        algorithm_id="iapf",
        recording=_fake_recording(),
        config={"channel": "Fz", "mode": "dynamic", "start_s": 0, "end_s": 29, "window_s": 30, "step_s": 5},
    )
    assert result.time_s == []
    assert result.values == []
    assert result.warmups == []


def test_iapf_dynamic_uses_only_full_thirty_second_windows_at_five_second_steps(monkeypatch) -> None:
    runtime = _runtime(monkeypatch)
    calls: list[tuple[float, float]] = []
    spectrum = SimpleNamespace(gate_failed=None, freqs=np.linspace(1.0, 30.0, 117), psd=np.ones((1, 117), dtype=float))

    def load_spectrum(*, start_s, window_s, channels):
        calls.append((start_s, window_s))
        return spectrum

    recording = SimpleNamespace(id="r1", channel_names=["Fz"], sfreq_hz=500.0, duration_s=40.0, load_spectrum=load_spectrum)
    result = runtime.execute(
        algorithm_id="iapf",
        recording=recording,
        config={"channel": "Fz", "mode": "dynamic", "start_s": 0, "end_s": 40, "window_s": 30, "step_s": 5},
    )
    assert result.time_s == [30.0, 35.0, 40.0]
    assert result.windows == [
        {"start_s": 0.0, "end_s": 30.0},
        {"start_s": 5.0, "end_s": 35.0},
        {"start_s": 10.0, "end_s": 40.0},
    ]
    assert result.warmups == [False, False, False]
    assert calls == [(0.0, 30.0), (5.0, 30.0), (10.0, 30.0)]


def test_iapf_dynamic_rejects_the_generic_ten_second_one_second_policy(monkeypatch) -> None:
    with pytest.raises(ValueError, match="30"):
        _runtime(monkeypatch).execute(
            algorithm_id="iapf",
            recording=_fake_recording(),
            config={"channel": "Fz", "mode": "dynamic", "start_s": 0, "end_s": 30, "window_s": 10, "step_s": 1},
        )


def test_later_generic_catchup_never_restarts_warmup_from_a_trailing_boundary() -> None:
    windows = build_playback_windows(2, 16, duration_s=30, window_s=10, step_s=1)
    assert [(item.start_s, item.end_s, item.warmup) for item in windows] == [
        (2.0, 12.0, False), (3.0, 13.0, False), (4.0, 14.0, False),
        (5.0, 15.0, False), (6.0, 16.0, False),
    ]
