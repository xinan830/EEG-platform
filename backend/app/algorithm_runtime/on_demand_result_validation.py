"""Validate module-declared fields/units before any window payload is published."""

from app.algorithm_runtime.on_demand import DynamicWindowResult
import numpy as np


def validate_window_result(result, manifest):
    checked = DynamicWindowResult.model_validate(result.model_dump())
    if checked.state != "ready" and not checked.axes and not checked.arrays:
        return checked
    declared = {field["name"]: field["unit"] for field in (
        manifest.output_schema["fields"] + manifest.output_schema.get("window_axes", []))}
    actual = {**checked.axis_units, **checked.array_units}
    if set(checked.axes) & set(checked.arrays) or actual != declared:
        raise ValueError("dynamic window fields or units do not match the module's output schema")
    expected_kind = manifest.output_schema.get("window_output_kind")
    if expected_kind is not None and checked.output_kind != expected_kind:
        raise ValueError("dynamic window output kind does not match the module's output schema")
    shapes = manifest.output_schema.get("window_shapes")
    if shapes is not None:
        if set(shapes) != set(actual):
            raise ValueError("dynamic window shape schema must cover every output field")
        dimensions = {}
        for name, value in {**checked.axes, **checked.arrays}.items():
            shape = np.asarray(value).shape
            expected = shapes[name]
            if len(shape) != len(expected):
                raise ValueError(f"dynamic window {name} rank does not match its schema")
            for length, dimension in zip(shape, expected):
                if type(dimension) is int:
                    valid = length == dimension
                elif isinstance(dimension, str):
                    valid = length == dimensions.setdefault(dimension, length)
                else:
                    raise ValueError("dynamic window shape dimension must be an integer or a named dimension")
                if not valid:
                    raise ValueError(f"dynamic window {name} shape does not match its schema")
    for name, value in checked.axes.items():
        axis = np.asarray(value)
        if axis.ndim != 1 or np.any(np.diff(axis) <= 0):
            raise ValueError(f"dynamic window axis {name} must be one-dimensional and strictly increasing")
    return checked
