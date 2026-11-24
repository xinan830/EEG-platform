import numpy as np
import pytest

from app.services.live_filter import LiveFilterService


def test_live_filter_preserves_non_eeg_columns_and_returns_volts() -> None:
    service = LiveFilterService()
    service.create("session", 500, 3, [0, 1], 1.0, 30.0, 50.0)
    source = np.column_stack((np.sin(np.arange(20) / 3) * 1e-6, np.cos(np.arange(20) / 3) * 1e-6, np.arange(20))).astype(float)

    result = np.asarray(service.process("session", source.reshape(-1).tolist(), 20)).reshape(20, 3)

    np.testing.assert_array_equal(result[:, 2], source[:, 2])
    assert result.shape == source.shape
    assert np.isfinite(result[:, :2]).all()


def test_live_filter_rejects_invalid_band_and_unknown_session() -> None:
    service = LiveFilterService()
    with pytest.raises(ValueError):
        service.create("session", 100, 2, [0], 30.0, 70.0, None)
    with pytest.raises(ValueError):
        service.create("session", 500, 2, [0], 1.0, 30.0, 45.0)
    with pytest.raises(KeyError):
        service.process("missing", [0.0], 1)


def test_live_filter_50_hz_notch_suppresses_mains_component() -> None:
    """A configured notch must alter the Python-owned display output, never raw input."""
    sampling_rate_hz = 500
    duration_s = 12
    sample_indexes = np.arange(sampling_rate_hz * duration_s)
    time_s = sample_indexes / sampling_rate_hz
    # Keep 50 Hz inside the display band, mixed with a physiological 10 Hz signal.
    source = (
        8e-6 * np.sin(2 * np.pi * 10 * time_s)
        + 40e-6 * np.sin(2 * np.pi * 50 * time_s)
    ).reshape(-1, 1)
    source_values = source.reshape(-1).tolist()
    original_values = list(source_values)

    service = LiveFilterService()
    service.create("without-notch", sampling_rate_hz, 1, [0], 0.5, 100.0, None)
    service.create("with-notch", sampling_rate_hz, 1, [0], 0.5, 100.0, 50.0)

    without_notch = np.asarray(
        service.process("without-notch", source_values, source.shape[0])
    )
    with_notch = np.asarray(
        service.process("with-notch", source_values, source.shape[0])
    )

    # Discard filter settling time, then compare the remaining display signal.
    tail = slice(sampling_rate_hz * 4, None)
    assert np.std(with_notch[tail]) < np.std(without_notch[tail]) * 0.45
    assert source_values == original_values


def test_live_filter_warmup_initializes_the_same_session_before_next_batch() -> None:
    service = LiveFilterService()
    session = service.create("session", 500, 2, [0], 1.0, 30.0, None)
    warmup = np.column_stack((np.sin(np.arange(500) / 5) * 1e-6, np.arange(500))).astype(float)
    incoming = np.column_stack((np.sin(np.arange(20) / 5) * 1e-6, np.arange(500, 520))).astype(float)

    warmed_values = session.process(warmup.reshape(-1).tolist(), 500)
    result = np.asarray(service.process("session", incoming.reshape(-1).tolist(), 20)).reshape(20, 2)

    assert len(warmed_values) == warmup.size
    np.testing.assert_array_equal(result[:, 1], incoming[:, 1])
    assert np.isfinite(result[:, 0]).all()
