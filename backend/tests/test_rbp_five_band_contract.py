from __future__ import annotations

from types import SimpleNamespace

import numpy as np
import pytest

from app.algorithms.rbp.runner import RbpAlgorithm
from app.algorithm_runtime.executor import AlgorithmRuntime
from app.algorithm_runtime.registry import AlgorithmRegistry
from app.scientific.quality import SpectralQualityGateError


def _spectrum(*, gate_failed: str | None = None):
    freqs = np.arange(1.0, 50.25, 0.25)
    return SimpleNamespace(
        gate_failed=gate_failed,
        freqs=freqs,
        psd=np.ones((1, len(freqs)), dtype=float),
        clean_epochs=4,
        total_epochs=4,
        signal_quality=1.0 if gate_failed is None else 0.0,
        rejected_reasons=(),
        evidence={},
    )


def _recording(loader, *, sfreq_hz: float = 200.0, duration_s: float = 12.0):
    return SimpleNamespace(
        id="r1", channel_names=["Fz"], sfreq_hz=sfreq_hz,
        duration_s=duration_s, load_spectrum=loader,
    )


def _runtime() -> AlgorithmRuntime:
    registry = AlgorithmRegistry()
    registry.register(RbpAlgorithm())
    return AlgorithmRuntime(registry)


def test_static_quality_failure_returns_all_five_shares_as_null() -> None:
    def load_spectrum(**_kwargs):
        return _spectrum(gate_failed="low_quality")

    result = _runtime().execute(
        algorithm_id="rbp", recording=_recording(load_spectrum),
        config={"channel": "Fz", "mode": "static", "start_s": 0, "end_s": 8},
    )

    assert result.quality == "gate_failed"
    assert result.failure is not None
    assert set(result.arrays) == {"delta", "theta", "alpha", "beta", "gamma"}
    assert all(np.isnan(value).all() for value in result.arrays.values())
    assert result.evidence["extensions"]["channel_failures"]["by_channel"]["Fz"]["code"] == "PSD_QUALITY_GATE_FAILED"


def test_dynamic_quality_failure_returns_five_nulls_and_continues_to_next_window() -> None:
    calls = 0

    def load_spectrum(**_kwargs):
        nonlocal calls
        calls += 1
        return _spectrum(gate_failed="low_quality") if calls == 1 else _spectrum()

    result = _runtime().execute(
        algorithm_id="rbp", recording=_recording(load_spectrum, duration_s=6),
        config={"channel": "Fz", "mode": "dynamic", "start_s": 0, "end_s": 6, "window_s": 5, "step_s": 1},
    )

    assert result.windows[0].quality == "gate_failed"
    assert result.windows[0].state == "Rejected"
    assert all(np.isnan(value[0]).all() if value.ndim > 1 else np.isnan(value[0]) for value in result.arrays.values())
    assert result.windows[1].quality == "clean"
    assert sum(result.arrays[name][1, 0] for name in ("delta", "theta", "alpha", "beta", "gamma")) == pytest.approx(1.0)


def test_static_multi_channel_preserves_order_and_isolates_bad_channel():
    spectrum = _spectrum()
    spectrum.psd = np.vstack([np.ones_like(spectrum.freqs), np.zeros_like(spectrum.freqs)])
    recording = _recording(lambda **_: spectrum)
    recording.channel_names = ["F3-REF", "F4-REF"]
    result = _runtime().execute(algorithm_id="rbp", recording=recording, config={
        "channel": "F3-REF", "channels": ["F3-REF", "F4-REF"], "mode": "static", "start_s": 0, "end_s": 8,
    })
    assert result.channel_order == ["F3-REF", "F4-REF"]
    assert all(np.isfinite(array[0]) and np.isnan(array[1]) for array in result.arrays.values())
    assert result.quality == "partial"
