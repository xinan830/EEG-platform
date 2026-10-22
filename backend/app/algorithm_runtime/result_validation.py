"""Validate module result values against their declared output contract."""

from __future__ import annotations

from typing import Any

import numpy as np

from .contracts import (
    AlgorithmEvidence,
    AlgorithmManifest,
    AlgorithmResult,
    AlgorithmSeriesResult,
    AlgorithmStructuredResult,
    AlgorithmStructuredSeriesResult,
)
from .errors import InvalidAlgorithmResultError


def validate_algorithm_result(
    result: Any, manifest: AlgorithmManifest, mode: str,
    requested_channels: list[str] | None = None,
) -> None:
    """Reject result-family, field, unit, or role mismatches before persistence."""
    contract = manifest.output_contract
    if contract is None:
        if manifest.is_current_version:
            raise InvalidAlgorithmResultError("current official module requires an output contract")
        return
    expected_type = (
        contract.static_result_type if mode == "static" else contract.dynamic_result_type
    )
    type_by_result = {
        AlgorithmResult: "scalar",
        AlgorithmSeriesResult: "series",
        AlgorithmStructuredResult: "structured",
        AlgorithmStructuredSeriesResult: "structured_series",
    }
    actual_type = next(
        (name for result_type, name in type_by_result.items() if isinstance(result, result_type)),
        None,
    )
    if actual_type != expected_type:
        raise InvalidAlgorithmResultError(
            f"{manifest.algorithm_id} returned {actual_type!r}; declared {mode} result is {expected_type!r}"
        )

    evidence_rows = [result.evidence]
    if isinstance(result, AlgorithmSeriesResult):
        evidence_rows = result.point_evidence
    elif isinstance(result, AlgorithmStructuredSeriesResult):
        evidence_rows.extend(window.evidence for window in result.windows)
    for row in evidence_rows:
        parsed = AlgorithmEvidence.model_validate(row)
        if set(parsed.extensions) & {"source_quality", "spectral_evidence", "calculation_trace", "algorithm_id"}:
            raise InvalidAlgorithmResultError("algorithm evidence extension shadows a common field")

    declared_fields = {field.name: field.unit for field in contract.fields}
    if requested_channels is not None:
        if len(requested_channels) != len(contract.channel_role_order):
            raise InvalidAlgorithmResultError("requested channels do not match declared source roles")
        if isinstance(result, (AlgorithmResult, AlgorithmSeriesResult)):
            matches = result.channel.casefold() == "/".join(requested_channels).casefold()
        else:
            matches = [name.casefold() for name in result.channel_order] == [
                name.casefold() for name in requested_channels
            ]
        if not matches:
            raise InvalidAlgorithmResultError("result channel order differs from requested role order")
    if isinstance(result, (AlgorithmResult, AlgorithmSeriesResult)):
        if not contract.fields or result.unit != contract.fields[0].unit:
            raise InvalidAlgorithmResultError(f"{manifest.algorithm_id} scalar result unit violates its output contract")
        values = result.output_values if isinstance(result, AlgorithmResult) else None
        series_values = result.output_values if isinstance(result, AlgorithmSeriesResult) else None
        if values is not None and set(values) != set(declared_fields):
            raise InvalidAlgorithmResultError(f"{manifest.algorithm_id} scalar output fields violate its output contract")
        if series_values and any(set(row) != set(declared_fields) for row in series_values):
            raise InvalidAlgorithmResultError(f"{manifest.algorithm_id} scalar series fields violate its output contract")
        if len(declared_fields) > 1 and values is None and not isinstance(result, AlgorithmSeriesResult):
            raise InvalidAlgorithmResultError("multi-field scalar result must provide all output values")
        if isinstance(result, AlgorithmSeriesResult):
            count = len(result.values)
            for name in ("time_s", "windows", "quality", "failures", "states", "point_evidence"):
                if len(getattr(result, name)) != count:
                    raise InvalidAlgorithmResultError(f"scalar series {name} count differs from values")
            if result.output_values or len(declared_fields) > 1:
                if len(result.output_values) != count:
                    raise InvalidAlgorithmResultError("scalar series output value count differs from points")
            for index, (quality, failure, state) in enumerate(zip(
                result.quality, result.failures, result.states, strict=True,
            )):
                _validate_quality(quality, failure, contract.quality_values)
                if state not in contract.dynamic_states:
                    raise InvalidAlgorithmResultError(f"scalar point {index} has an undeclared state")
                if state in {"Rejected", "Unavailable"} and failure is None:
                    raise InvalidAlgorithmResultError("rejected or unavailable point requires a failure")
            for row in result.windows:
                if row["end_s"] <= row["start_s"]:
                    raise InvalidAlgorithmResultError("scalar window range must be positive")
        else:
            _validate_quality(result.quality, result.failure, contract.quality_values)
        scalar_values = result.values if isinstance(result, AlgorithmSeriesResult) else [result.value]
        for value in scalar_values:
            if value is not None and not np.isfinite(value):
                raise InvalidAlgorithmResultError("scalar result must use null for unavailable values")
        for row in ([values] if values is not None else series_values or []):
            if any(value is not None and not np.isfinite(value) for value in row.values()):
                raise InvalidAlgorithmResultError("multi-field result must use null for unavailable values")
        return

    actual_units = {**result.axis_units, **result.array_units}
    if set(result.axes) & set(result.arrays) or actual_units != declared_fields:
        raise InvalidAlgorithmResultError(
            f"{manifest.algorithm_id} structured result fields/units violate its output contract: "
            f"declared={declared_fields!r} actual={actual_units!r}"
        )
    if result.output_kind != manifest.output_schema["window_output_kind"]:
        raise InvalidAlgorithmResultError("structured result kind differs from its declaration")
    dimensions = {}
    for name, value in {**result.axes, **result.arrays}.items():
        array = np.asarray(value)
        shape = array.shape
        if isinstance(result, AlgorithmStructuredSeriesResult) and name in result.arrays:
            if not shape or shape[0] != len(result.windows):
                raise InvalidAlgorithmResultError("structured series must use windows as its first dimension")
            shape = shape[1:]
        expected = next(field.shape for field in contract.fields if field.name == name)
        if len(shape) != len(expected):
            raise InvalidAlgorithmResultError(f"structured field {name} rank differs from declaration")
        for length, dimension in zip(shape, expected, strict=True):
            wanted = dimension if isinstance(dimension, int) else dimensions.setdefault(dimension, length)
            if wanted != length:
                raise InvalidAlgorithmResultError(f"structured field {name} shape differs from declaration")
        if np.any(np.isinf(array)):
            raise InvalidAlgorithmResultError(f"structured field {name} contains infinity")
        if name in result.axes and (array.ndim != 1 or not np.isfinite(array).all() or np.any(np.diff(array) <= 0)):
            raise InvalidAlgorithmResultError(f"structured axis {name} must be finite and strictly increasing")
    _validate_quality(result.quality, result.failure, contract.quality_values)
    if isinstance(result, AlgorithmStructuredSeriesResult):
        for window in result.windows:
            _validate_quality(window.quality, window.failure, contract.quality_values)
            if window.state not in contract.dynamic_states:
                raise InvalidAlgorithmResultError("structured window has an undeclared state")


def _validate_quality(quality, failure, allowed):
    if quality not in allowed:
        raise InvalidAlgorithmResultError(f"undeclared result quality {quality!r}")
    if quality in {"gate_failed", "unavailable", "insufficient_data", "failed"} and failure is None:
        raise InvalidAlgorithmResultError("unavailable or rejected result requires an explicit failure")
