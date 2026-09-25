from __future__ import annotations

import numpy as np
import pytest

from app.scientific.primitives import DEFAULT_SPECTRAL_GATEWAY, real_fft


def test_real_fft_matches_numpy_and_preserves_channel_order_and_range():
    sfreq = 100.0
    samples = np.arange(100, dtype=float)
    values = np.column_stack((
        2.0 * np.sin(2 * np.pi * 10.0 * samples / sfreq),
        3.0 * np.cos(2 * np.pi * 20.0 * samples / sfreq),
    )) * 1e-6

    result = real_fft(values, sfreq, start_sample=200)

    np.testing.assert_allclose(result.frequencies_hz, np.fft.rfftfreq(100, 1 / sfreq))
    np.testing.assert_allclose(result.coefficients_v, np.fft.rfft(values, axis=0))
    assert result.sample_range.as_dict() == {"start_sample": 200, "end_sample": 300, "half_open": True}
    assert result.transform_padding.as_dict() == {"used": False, "kind": "none", "samples": 0}


def test_real_fft_reports_transform_padding_separately_from_input_gap():
    values = np.ones((100, 1), dtype=float)
    result = real_fft(values, 100.0, transform_length=128)

    assert result.transform_padding.as_dict() == {"used": True, "kind": "fft_boundary", "samples": 28}
    assert result.transform_length == 128

    with pytest.raises(ValueError, match="non-finite"):
        real_fft(np.array([[1.0], [np.nan]]), 100.0, transform_length=4)


def test_spectral_gateway_exposes_the_same_canonical_fft():
    values = np.arange(16, dtype=float)[:, None] * 1e-6
    direct = real_fft(values, 200.0, start_sample=7, transform_length=32)
    via_gateway = DEFAULT_SPECTRAL_GATEWAY.fourier(
        values, 200.0, start_sample=7, transform_length=32,
    )

    np.testing.assert_array_equal(via_gateway.frequencies_hz, direct.frequencies_hz)
    np.testing.assert_array_equal(via_gateway.coefficients_v, direct.coefficients_v)
    assert via_gateway.sample_range == direct.sample_range
    assert via_gateway.transform_padding == direct.transform_padding


@pytest.mark.parametrize(
    "values, rate, length, message",
    [
        (np.empty((0, 1)), 100.0, None, "non-empty"),
        (np.ones((4, 1)), 0.0, None, "sampling rate"),
        (np.ones((4, 1)), 100.0, 3, "at least"),
        (np.ones((4, 1)), 100.0, 4.5, "integer"),
    ],
)
def test_real_fft_rejects_invalid_requests(values, rate, length, message):
    with pytest.raises(ValueError, match=message):
        real_fft(values, rate, transform_length=length)
