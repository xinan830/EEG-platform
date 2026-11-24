"""Closed, import-time registry for approved research primitive nodes."""

from __future__ import annotations

from collections.abc import Callable

from . import math_nodes, signal_nodes, spectral_nodes


class UnknownNodeError(ValueError):
    """Raised before any execution when a graph names a non-approved node."""


NODE_REGISTRY: dict[str, Callable[..., object]] = {
    "channel_select": signal_nodes.select_channels,
    "rereference": signal_nodes.rereference,
    "bandpass": signal_nodes.bandpass,
    "notch": signal_nodes.notch,
    "resample": signal_nodes.resample,
    "detrend": signal_nodes.detrend,
    "window": signal_nodes.window,
    "welch_psd": spectral_nodes.welch_psd,
    "band_power": spectral_nodes.band_power,
    "relative_band_power": spectral_nodes.relative_band_power,
    "quality_gate": spectral_nodes.quality_gate,
    "add": math_nodes.add,
    "subtract": math_nodes.subtract,
    "multiply": math_nodes.multiply,
    "divide": math_nodes.divide,
    "ln": math_nodes.natural_log,
    "mean": math_nodes.mean,
    "median": math_nodes.median,
    "std": math_nodes.standard_deviation,
    "cv": math_nodes.coefficient_of_variation,
    "weighted_sum": math_nodes.weighted_sum,
    "output": math_nodes.output,
}


def resolve_node(name: str) -> Callable[..., object]:
    try:
        return NODE_REGISTRY[name]
    except KeyError as exc:
        raise UnknownNodeError(f"unknown research primitive node: {name}") from exc
