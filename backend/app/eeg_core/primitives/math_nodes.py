"""Safe arithmetic and statistical primitives without expression evaluation."""

from __future__ import annotations

from dataclasses import replace

import numpy as np

from .types import PrimitiveValueError, QualityMask, Scalar, append_provenance
from .units import Unit, divide_unit, multiply_unit, require_same


def _unavailable(unit: Unit, *items: Scalar, node: str, **parameters: object) -> Scalar:
    quality = QualityMask.combine([item.quality for item in items])
    return Scalar(None, unit, quality, append_provenance(tuple(step for item in items for step in item.provenance), node, **parameters))


def add(left: Scalar, right: Scalar) -> Scalar:
    unit = require_same(left.unit, right.unit)
    if left.value is None or right.value is None:
        return _unavailable(unit, left, right, node="add")
    return Scalar(left.value + right.value, unit, QualityMask.combine((left.quality, right.quality)), append_provenance(left.provenance + right.provenance, "add"))


def subtract(left: Scalar, right: Scalar) -> Scalar:
    unit = require_same(left.unit, right.unit)
    if left.value is None or right.value is None:
        return _unavailable(unit, left, right, node="subtract")
    return Scalar(left.value - right.value, unit, QualityMask.combine((left.quality, right.quality)), append_provenance(left.provenance + right.provenance, "subtract"))


def multiply(left: Scalar, right: Scalar) -> Scalar:
    unit = multiply_unit(left.unit, right.unit)
    if left.value is None or right.value is None:
        return _unavailable(unit, left, right, node="multiply")
    return Scalar(left.value * right.value, unit, QualityMask.combine((left.quality, right.quality)), append_provenance(left.provenance + right.provenance, "multiply"))


def divide(left: Scalar, right: Scalar) -> Scalar:
    unit = divide_unit(left.unit, right.unit)
    if left.value is None or right.value is None:
        return _unavailable(unit, left, right, node="divide")
    if right.value == 0:
        return Scalar(None, unit, QualityMask("bad", ("division_by_zero",)), append_provenance(left.provenance + right.provenance, "divide"))
    return Scalar(left.value / right.value, unit, QualityMask.combine((left.quality, right.quality)), append_provenance(left.provenance + right.provenance, "divide"))


def natural_log(source: Scalar) -> Scalar:
    if source.value is None:
        return _unavailable(Unit.DIMENSIONLESS, source, node="ln")
    if source.value <= 0:
        return Scalar(None, Unit.DIMENSIONLESS, QualityMask("bad", ("non_positive_log_input",)), append_provenance(source.provenance, "ln"))
    return Scalar(float(np.log(source.value)), Unit.DIMENSIONLESS, source.quality, append_provenance(source.provenance, "ln"))


def _statistic(values: tuple[Scalar, ...] | list[Scalar], node: str) -> Scalar:
    items = tuple(values)
    if not items:
        raise PrimitiveValueError(f"{node} requires at least one scalar")
    unit = items[0].unit
    for item in items[1:]:
        require_same(unit, item.unit)
    if any(item.value is None for item in items):
        return _unavailable(unit, *items, node=node)
    data = np.asarray([item.value for item in items], dtype=float)
    operation = {"mean": np.mean, "median": np.median, "std": np.std}[node]
    return Scalar(float(operation(data)), unit, QualityMask.combine([item.quality for item in items]), append_provenance(tuple(step for item in items for step in item.provenance), node))


def mean(values: tuple[Scalar, ...] | list[Scalar]) -> Scalar:
    return _statistic(values, "mean")


def median(values: tuple[Scalar, ...] | list[Scalar]) -> Scalar:
    return _statistic(values, "median")


def standard_deviation(values: tuple[Scalar, ...] | list[Scalar]) -> Scalar:
    return _statistic(values, "std")


def coefficient_of_variation(values: tuple[Scalar, ...] | list[Scalar]) -> Scalar:
    average = mean(values)
    deviation = standard_deviation(values)
    return divide(Scalar(deviation.value, average.unit, deviation.quality, deviation.provenance), average)


def weighted_sum(values: tuple[Scalar, ...] | list[Scalar], weights: tuple[float, ...] | list[float]) -> Scalar:
    items = tuple(values)
    if not items or len(items) != len(weights) or not np.isfinite(weights).all():
        raise PrimitiveValueError("weighted_sum requires finite weights for each scalar")
    unit = items[0].unit
    for item in items[1:]:
        require_same(unit, item.unit)
    if any(item.value is None for item in items):
        return _unavailable(unit, *items, node="weighted_sum", weights=tuple(weights))
    return Scalar(float(np.dot([item.value for item in items], weights)), unit, QualityMask.combine([item.quality for item in items]),
                  append_provenance(tuple(step for item in items for step in item.provenance), "weighted_sum", weights=tuple(weights)))


def output(source: Scalar) -> Scalar:
    """Explicit graph output marker; it does not alter a scientific value."""
    return replace(source, provenance=append_provenance(source.provenance, "output"))
