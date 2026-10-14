"""The module's structured window schema is validated before artifact writes."""

import pytest

from app.algorithm_runtime.on_demand import DynamicWindowResult
from app.algorithm_runtime.on_demand_result_validation import validate_window_result
from app.algorithms.psd.manifest import MANIFEST
from test_on_demand_window_contracts import _identity


def result():
    return DynamicWindowResult(identity=_identity(), state="ready", output_kind="frequency_series",
        axes={"frequency_hz": [1, 2]}, axis_units={"frequency_hz": "Hz"},
        arrays={"psd": [1, 2], "band_share": [0.2] * 5},
        array_units={"psd": "V^2/Hz", "band_share": "ratio"}, quality="clean",
        provenance={"preprocessing_scope": "whole_recording_before_window_slice"})


@pytest.mark.parametrize("damage", ["axis_length", "band_length", "rank", "kind", "axis_order", "unit"])
def test_invalid_window_shape_kind_axis_or_unit_is_not_publishable(damage):
    current = result()
    if damage == "axis_length":
        current.arrays["psd"] = [1]
    elif damage == "band_length":
        current.arrays["band_share"] = [0.2]
    elif damage == "rank":
        current.arrays["psd"] = [[1, 2]]
    elif damage == "kind":
        current.output_kind = "scalar"
    elif damage == "axis_order":
        current.axes["frequency_hz"] = [2, 1]
    else:
        current.array_units["psd"] = "V"
    with pytest.raises(ValueError):
        validate_window_result(current, MANIFEST)


def test_valid_module_shapes_are_preserved():
    current = result()
    checked = validate_window_result(current, MANIFEST)
    assert checked.arrays == current.arrays
    assert checked.axes == current.axes
