"""Explicit units and conservative unit arithmetic for research primitives."""

from __future__ import annotations

from enum import Enum


class UnitError(ValueError):
    """Raised when a calculation would mix incompatible scientific units."""


class Unit(str, Enum):
    V = "V"
    UV = "uV"
    V2 = "V^2"
    UV2 = "uV^2"
    V2_PER_HZ = "V^2/Hz"
    UV2_PER_HZ = "uV^2/Hz"
    HZ = "Hz"
    SECOND = "s"
    RATIO = "ratio"
    PERCENT = "percent"
    DIMENSIONLESS = "dimensionless"
    DB_RE_1_UV2_PER_HZ = "dB re 1 uV^2/Hz"


_FAMILIES = {
    Unit.V: "voltage", Unit.UV: "voltage",
    Unit.V2: "power", Unit.UV2: "power",
    Unit.V2_PER_HZ: "density", Unit.UV2_PER_HZ: "density",
    Unit.HZ: "frequency", Unit.SECOND: "time",
    Unit.RATIO: "ratio", Unit.PERCENT: "percent", Unit.DIMENSIONLESS: "dimensionless",
    Unit.DB_RE_1_UV2_PER_HZ: "db_density",
}

_TO_BASE = {
    Unit.V: 1.0, Unit.UV: 1e-6,
    Unit.V2: 1.0, Unit.UV2: 1e-12,
    Unit.V2_PER_HZ: 1.0, Unit.UV2_PER_HZ: 1e-12,
    Unit.HZ: 1.0, Unit.SECOND: 1.0, Unit.RATIO: 1.0,
    Unit.PERCENT: 0.01, Unit.DIMENSIONLESS: 1.0,
}


def compatible(left: Unit, right: Unit) -> bool:
    """Return whether two units describe the same physical family."""
    return _FAMILIES[left] == _FAMILIES[right]


def require_same(left: Unit, right: Unit) -> Unit:
    """Require literally identical units; conversion must be requested explicitly."""
    if left != right:
        raise UnitError(f"implicit unit conversion is forbidden: {left.value} and {right.value}")
    return left


def convert_factor(source: Unit, target: Unit) -> float:
    """Return an explicit linear conversion factor for compatible linear units."""
    if source == target:
        return 1.0
    if not compatible(source, target) or source not in _TO_BASE or target not in _TO_BASE:
        raise UnitError(f"cannot convert {source.value} to {target.value}")
    return _TO_BASE[source] / _TO_BASE[target]


def multiply_unit(left: Unit, right: Unit) -> Unit:
    """Resolve only products required by the first primitive set."""
    if left == Unit.RATIO:
        return right
    if right == Unit.RATIO:
        return left
    if left == right == Unit.V:
        return Unit.V2
    if left == right == Unit.UV:
        return Unit.UV2
    if {left, right} == {Unit.V2_PER_HZ, Unit.HZ}:
        return Unit.V2
    if {left, right} == {Unit.UV2_PER_HZ, Unit.HZ}:
        return Unit.UV2
    raise UnitError(f"unsupported multiplication: {left.value} * {right.value}")


def divide_unit(numerator: Unit, denominator: Unit) -> Unit:
    """Resolve a deliberately small, auditable set of division rules."""
    if numerator == denominator:
        return Unit.DIMENSIONLESS
    if numerator in (Unit.V2, Unit.UV2) and denominator == Unit.HZ:
        return Unit.V2_PER_HZ if numerator == Unit.V2 else Unit.UV2_PER_HZ
    raise UnitError(f"unsupported division: {numerator.value} / {denominator.value}")
