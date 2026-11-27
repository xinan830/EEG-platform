import numpy as np
import pytest
from types import SimpleNamespace
from pydantic import ValidationError

from app.scientific.primitives import DEFAULT_SPECTRAL_GATEWAY
from app.algorithms.rbp.official import five_band_shares
from app.services.live_analysis.live_recording_access import LiveRecordingAccessError, LiveRecordingAccessService
from app.services.live_analysis.live_rbp_algorithm import LiveRbpAlgorithm, LiveRbpConfig


def _source(*, sfreq=250.0, capacity=20_000):
    return LiveRecordingAccessService().create(
        "acq-live", "stream-v1", "schema-v1", sfreq,
        ("F3", "F4"), ("V", "V"), capacity,
    )


def _signal(start: int, count: int, sfreq: float = 250.0) -> np.ndarray:
    t = np.arange(start, start + count) / sfreq
    return np.column_stack((
        18e-6 * np.sin(2 * np.pi * 10 * t) + 8e-6 * np.sin(2 * np.pi * 20 * t),
        16e-6 * np.sin(2 * np.pi * 6 * t) + 7e-6 * np.sin(2 * np.pi * 35 * t),
    ))


def test_live_rbp_config_rejects_duplicate_channels_and_unsupported_notch():
    with pytest.raises(ValidationError):
        LiveRbpConfig(channels=["F3", "f3"])
    with pytest.raises(ValidationError):
        LiveRbpConfig(channels=["F3"], notch_hz=55)


def test_live_rbp_validates_order_and_nyquist():
    algorithm = LiveRbpAlgorithm()
    normalized, plan = algorithm.configure(_source(), {"channels": ["f4", "f3"], "window_s": 4})
    assert normalized.channels == ["F4", "F3"]
    assert (plan.window_samples, plan.step_samples) == (1000, 250)
    with pytest.raises(LiveRecordingAccessError, match="奈奎斯特"):
        algorithm.configure(_source(sfreq=100), {"channels": ["F3"]})


def test_rbp_adapter_preserves_deterministic_pre_coordination_values():
    source = _source()
    signal = _signal(0, 1000)
    source.append(1000, signal, 1000)
    algorithm = LiveRbpAlgorithm()
    config, _ = algorithm.configure(source, {"channels": ["F4", "F3"], "window_s": 4})
    result = algorithm.calculate("acq-live", source.read_samples(1000, 2000, config.channels), config, 1000)

    assert result.state == "ready"
    assert (result.start_sample, result.end_sample, result.start_s, result.end_s) == (1000, 2000, 0.0, 4.0)
    np.testing.assert_allclose(result.values["alpha"], [3.3115327569339583e-10, 0.8348907267303337], rtol=1e-10)
    np.testing.assert_allclose(result.values["theta"], [0.8462375996651601, 6.653837613755834e-09], rtol=1e-10)
    assert result.channels == ["F4", "F3"]
    assert result.units["alpha"] == "ratio"
    assert result.units["alpha_absolute_power"] == "uV^2"
    assert result.quality["F3"]["status"] == "clean"
    assert result.scientific_identity == "rbp-live-v1"
    assert result.implementation_identity == "rbp-live-window-v1"
    assert result.provenance["window_s"] == 4
    assert result.provenance["equivalent_to_offline_official_rbp_v4"] is False
    assert np.isclose(sum(result.values[name][0] for name in ("delta", "theta", "alpha", "beta", "gamma")), 1)


@pytest.mark.parametrize("notch_hz", [None, 50.0, 60.0])
def test_window_local_live_preprocessing_is_identified_separately_from_offline(notch_hz):
    sfreq = 250.0
    sample_count = int(30 * sfreq)
    rng = np.random.default_rng(43)
    t = np.arange(sample_count) / sfreq
    raw = (12e-6 * np.sin(2 * np.pi * 10 * t) +
           5e-6 * np.sin(2 * np.pi * 50 * t) +
           rng.normal(0, 1e-6, sample_count))[:, None]
    offline = DEFAULT_SPECTRAL_GATEWAY.preprocess(
        raw, sfreq, low_hz=1, high_hz=50, notch_hz=notch_hz,
    )
    results = []
    for start_s in (0, 10, 20):
        start, end = int(start_s * sfreq), int((start_s + 10) * sfreq)
        live_window = DEFAULT_SPECTRAL_GATEWAY.preprocess(
            raw[start:end], sfreq, low_hz=1, high_hz=50, notch_hz=notch_hz,
        )
        live_psd = DEFAULT_SPECTRAL_GATEWAY.welch_annotated_channels(
            live_window, sfreq, low_hz=1, high_hz=50,
        )
        offline_psd = DEFAULT_SPECTRAL_GATEWAY.welch_annotated_channels(
            offline[start:end], sfreq, low_hz=1, high_hz=50,
        )
        _, live_share = five_band_shares(SimpleNamespace(freqs=live_psd.freqs, psd=live_psd.psd))
        _, offline_share = five_band_shares(SimpleNamespace(freqs=offline_psd.freqs, psd=offline_psd.psd))
        results.append((list(live_share.values()), list(offline_share.values())))

    assert any(not np.allclose(live, offline, rtol=1e-6, atol=1e-9)
               for live, offline in results)
