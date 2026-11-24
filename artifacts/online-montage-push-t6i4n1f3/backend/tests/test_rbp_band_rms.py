import numpy as np

from app.algorithms.rbp.amplitude import band_rms_uv


def test_band_rms_preserves_units_shape_and_unavailable_values():
    powers = np.array([[0.0, 4.0, 2.25], [np.nan, -1.0, np.inf]])

    result = band_rms_uv(powers)

    np.testing.assert_allclose(result[0], [0.0, 2.0, 1.5])
    assert np.isnan(result[1]).all()
    np.testing.assert_array_equal(powers[0], [0.0, 4.0, 2.25])
