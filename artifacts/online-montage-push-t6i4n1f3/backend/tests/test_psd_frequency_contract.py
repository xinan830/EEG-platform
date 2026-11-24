from __future__ import annotations

from types import SimpleNamespace

import numpy as np

from app.algorithm_runtime.executor import AlgorithmRuntime
from app.algorithm_runtime.registry import AlgorithmRegistry
from app.algorithms.psd.runner import PsdAlgorithm


def test_dynamic_psd_uses_shared_analysis_filter_and_fixed_trend_axis() -> None:
    algorithm = PsdAlgorithm()
    calls: list[dict[str, object]] = []
    frequencies = algorithm._frequency_axis(200.0, 1.0, 50.0)
    spectrum = SimpleNamespace(
        gate_failed=None,
        freqs=frequencies,
        psd=np.vstack([np.ones(len(frequencies)), np.full(len(frequencies), 2.0)]),
        clean_epochs=1,
        total_epochs=1,
        signal_quality=1.0,
        rejected_reasons=(),
        evidence={"channel_quality": {
            "Cz": {"status": "clean", "reasons": []},
            "Fz": {"status": "clean", "reasons": []},
        }},
    )

    def load_spectrum(**kwargs):
        calls.append(kwargs)
        return spectrum

    recording = SimpleNamespace(
        id="r1", channel_names=["Fz", "Cz"], sfreq_hz=200.0, duration_s=8.0,
        load_spectrum=load_spectrum,
    )
    registry = AlgorithmRegistry()
    registry.register(algorithm)
    result = AlgorithmRuntime(registry).execute(
        algorithm_id="psd",
        recording=recording,
        config={
            "channels": ["Cz", "Fz"], "mode": "dynamic", "start_s": 0.0,
            "end_s": 8.0, "window_s": 5.0, "step_s": 1.0,
            "low_hz": 0.5, "high_hz": 40.0, "notch_hz": 60.0,
        },
    )

    assert result.axes["frequency_hz"][0] == 1.0
    assert result.axes["frequency_hz"][-1] == 50.0
    assert calls
    assert all(call["filter_low_hz"] == 1.0 for call in calls)
    assert all(call["filter_high_hz"] == 50.0 for call in calls)
    assert all(call["output_low_hz"] == 1.0 for call in calls)
    assert all(call["output_high_hz"] == 50.0 for call in calls)
    assert all(call["notch_hz"] == 60.0 for call in calls)
    assert result.channel_order == ["Cz", "Fz"]
    assert result.arrays["psd"].shape[1:] == (2, len(frequencies))
    np.testing.assert_array_equal(result.arrays["psd"][-1, :, 0], [1.0, 2.0])
    trace = result.windows[-1].evidence["calculation_trace"]
    assert trace["filter_frequency_range_hz"] == {"low_hz": 1.0, "high_hz": 50.0}
    assert trace["output_frequency_range_hz"] == {"low_hz": 1.0, "high_hz": 50.0}


def test_channel_quality_mapping_is_projected_in_requested_order_without_hiding_peers() -> None:
    frequencies = PsdAlgorithm._frequency_axis(200.0, 1.0, 50.0)
    spectrum = SimpleNamespace(
        psd=np.ones((2, len(frequencies)), dtype=float),
        evidence={"channel_quality": {
            "F4": {"status": "clean", "reasons": []},
            "F3": {"status": "unavailable", "reasons": ["missing_samples"]},
        }},
    )

    values, _, quality = PsdAlgorithm._channel_rows(spectrum, ("F3", "F4"), frequencies)

    assert np.isnan(values[0]).all()
    assert np.isfinite(values[1]).all()
    assert [item["status"] for item in quality] == ["unavailable", "clean"]


def test_psd_band_share_array_has_five_standard_bands() -> None:
    frequencies = np.arange(1.0, 50.25, 0.25)
    values = np.ones_like(frequencies)
    shares = PsdAlgorithm._band_shares(frequencies, values)
    assert shares.shape == (5,)
    np.testing.assert_allclose(shares.sum(), 1.0)
    assert PsdAlgorithm._band_share_trace()["band_share_labels"] == ["delta", "theta", "alpha", "beta", "gamma"]


def test_psd_band_share_requires_complete_standard_band_coverage() -> None:
    frequencies = np.arange(1.0, 30.25, 0.25)
    shares = PsdAlgorithm._band_shares(frequencies, np.ones_like(frequencies))
    assert np.isnan(shares).all()


def test_psd_band_share_uses_shared_boundary_interpolated_integral() -> None:
    from app.scientific.primitives.spectral import band_power

    frequencies = np.arange(1.0, 50.25, 0.25)
    values = frequencies ** 2
    shares = PsdAlgorithm._band_shares(frequencies, values)
    expected = np.asarray([
        band_power(frequencies, values, low, high)
        for low, high in PsdAlgorithm.BAND_SHARE_RANGES
    ])

    np.testing.assert_allclose(shares, expected / expected.sum(), rtol=1e-14, atol=1e-14)
