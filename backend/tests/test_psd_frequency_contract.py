from __future__ import annotations

from types import SimpleNamespace

import numpy as np

from app.algorithm_runtime.executor import AlgorithmRuntime
from app.algorithm_runtime.registry import AlgorithmRegistry
from app.algorithms.psd.runner import PsdAlgorithm


def test_dynamic_psd_keeps_configured_filter_and_fixed_trend_axis() -> None:
    algorithm = PsdAlgorithm()
    calls: list[dict[str, object]] = []
    frequencies = algorithm._frequency_axis(200.0, 1.0, 50.0)
    spectrum = SimpleNamespace(
        gate_failed=None,
        freqs=frequencies,
        psd=np.ones((1, len(frequencies)), dtype=float),
        clean_epochs=1,
        total_epochs=1,
        signal_quality=1.0,
        rejected_reasons=(),
        evidence={},
    )

    def load_spectrum(**kwargs):
        calls.append(kwargs)
        return spectrum

    recording = SimpleNamespace(
        id="r1", channel_names=["Fz"], sfreq_hz=200.0, duration_s=8.0,
        load_spectrum=load_spectrum,
    )
    registry = AlgorithmRegistry()
    registry.register(algorithm)
    result = AlgorithmRuntime(registry).execute(
        algorithm_id="psd",
        recording=recording,
        config={
            "channel": "Fz", "mode": "dynamic", "start_s": 0.0,
            "end_s": 8.0, "window_s": 5.0, "step_s": 1.0,
            "low_hz": 0.5, "high_hz": 40.0, "notch_hz": 60.0,
        },
    )

    assert result.axes["frequency_hz"][0] == 1.0
    assert result.axes["frequency_hz"][-1] == 50.0
    assert calls
    assert all(call["filter_low_hz"] == 0.5 for call in calls)
    assert all(call["filter_high_hz"] == 40.0 for call in calls)
    assert all(call["output_low_hz"] == 1.0 for call in calls)
    assert all(call["output_high_hz"] == 50.0 for call in calls)
    assert all(call["notch_hz"] == 60.0 for call in calls)
    trace = result.windows[-1].evidence["calculation_trace"]
    assert trace["filter_frequency_range_hz"] == {"low_hz": 0.5, "high_hz": 40.0}
    assert trace["output_frequency_range_hz"] == {"low_hz": 1.0, "high_hz": 50.0}
