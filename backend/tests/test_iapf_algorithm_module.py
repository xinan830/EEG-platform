from __future__ import annotations

from types import SimpleNamespace

import numpy as np
import pytest

from app.algorithm_runtime.executor import AlgorithmRuntime
from app.algorithm_runtime.registry import AlgorithmRegistry
from app.algorithm_runtime.windows import build_playback_windows
from app.algorithms.iapf.runner import IapfAlgorithm
from app.algorithms.iapf.official import IAPFEstimate
from app.scientific.quality import SpectralQualityGateError


def _fake_recording(duration_s: float = 40.0):
    spectrum = SimpleNamespace(
        gate_failed=None,
        freqs=np.linspace(1.0, 30.0, 117),
        psd=np.ones((1, 117), dtype=float),
    )

    def load_spectrum(*, start_s, window_s, channels, **_):
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


def test_iapf_dynamic_does_not_emit_partial_values_before_a_complete_window(monkeypatch) -> None:
    result = _runtime(monkeypatch).execute(
        algorithm_id="iapf",
        recording=_fake_recording(),
        config={"channel": "Fz", "mode": "dynamic", "start_s": 0, "end_s": 19, "window_s": 20, "step_s": 1},
    )
    assert result.time_s == []
    assert result.values == []


def test_iapf_dynamic_uses_manifest_20_second_sliding_windows(monkeypatch) -> None:
    runtime = _runtime(monkeypatch)
    calls: list[tuple[float, float]] = []
    spectrum = SimpleNamespace(gate_failed=None, freqs=np.linspace(1.0, 30.0, 117), psd=np.ones((1, 117), dtype=float))

    def load_spectrum(*, start_s, window_s, channels, **_):
        calls.append((start_s, window_s))
        return spectrum

    recording = SimpleNamespace(id="r1", channel_names=["Fz"], sfreq_hz=500.0, duration_s=40.0, load_spectrum=load_spectrum)
    result = runtime.execute(
        algorithm_id="iapf",
        recording=recording,
        config={"channel": "Fz", "mode": "dynamic", "start_s": 0, "end_s": 22, "window_s": 20, "step_s": 1},
    )
    assert result.time_s == [20.0, 21.0, 22.0]
    assert result.windows == [
        {"start_s": 0.0, "end_s": 20.0},
        {"start_s": 1.0, "end_s": 21.0},
        {"start_s": 2.0, "end_s": 22.0},
    ]
    assert result.states == ["Complete"] * 3
    assert calls == [(0.0, 20.0), (1.0, 20.0), (2.0, 20.0)]


def test_iapf_dynamic_manifest_defaults_to_20_seconds_and_one_second(monkeypatch) -> None:
    from app.algorithms.iapf.manifest import MANIFEST

    assert MANIFEST.dynamic_policy.default_window_s == 20.0
    assert MANIFEST.dynamic_policy.refresh_step_s == 1.0
    assert MANIFEST.dynamic_policy.allow_warmup is False
    result = _runtime(monkeypatch).execute(
        algorithm_id="iapf",
        recording=_fake_recording(),
        config={"channel": "Fz", "mode": "dynamic", "start_s": 0, "end_s": 20, "window_s": 20, "step_s": 1},
    )
    assert result.time_s == [20.0]


def test_iapf_dynamic_keeps_a_rejected_window_and_continues(monkeypatch) -> None:
    runtime = _runtime(monkeypatch)
    spectrum = SimpleNamespace(gate_failed=None, freqs=np.linspace(1.0, 30.0, 117), psd=np.ones((1, 117), dtype=float))
    calls = 0

    def load_spectrum(*, start_s, window_s, channels, **_):
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
    result = runtime.execute(
        algorithm_id="iapf", recording=recording,
        config={"channel": "Fz", "mode": "dynamic", "start_s": 0, "end_s": 22, "window_s": 20, "step_s": 1},
    )
    assert result.values[0] is None
    assert result.quality[0] == "gate_failed"
    assert result.failures[0].code == "PSD_QUALITY_GATE_FAILED"
    assert result.values == [None, 10.0, 10.0]
    assert result.states[0] == "Rejected"
    assert result.states == ["Rejected", "Complete", "Complete"]


def test_later_generic_catchup_never_restarts_warmup_from_a_trailing_boundary() -> None:
    windows = build_playback_windows(2, 16, duration_s=30, window_s=10, step_s=1)
    assert [(item.start_s, item.end_s, item.state) for item in windows] == [
        (2.0, 12.0, "Complete"), (3.0, 13.0, "Complete"), (4.0, 14.0, "Complete"),
        (5.0, 15.0, "Complete"), (6.0, 16.0, "Complete"),
    ]
