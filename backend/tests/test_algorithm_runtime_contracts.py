from __future__ import annotations

import pytest
from pydantic import ValidationError

from app.algorithm_runtime.contracts import AlgorithmConfigBase, AlgorithmFailure, AlgorithmResult
from app.algorithm_runtime.parameter_schema import AlgorithmParameter, ParameterSchema


def test_algorithm_config_rejects_unknown_scientific_fields() -> None:
    with pytest.raises(ValidationError):
        AlgorithmConfigBase(channel="Fz", mode="static", start_s=0, end_s=10, chart_history_s=20)


def test_algorithm_result_can_represent_unavailable_value_without_zero() -> None:
    result = AlgorithmResult(
        value=None,
        unit="dimensionless",
        channel="O2",
        requested_range={"start_s": 0, "end_s": 10},
        quality="gate_failed",
        failure=AlgorithmFailure(code="IAPF_UNAVAILABLE", message="IAPF unavailable"),
    )
    assert result.value is None
    assert result.failure is not None


def test_parameter_schema_rejects_duplicate_keys_and_display_state() -> None:
    with pytest.raises(ValueError, match="unique"):
        ParameterSchema(parameters=[
            AlgorithmParameter(key="channel", label_zh="通道", value_type="string"),
            AlgorithmParameter(key="channel", label_zh="通道 2", value_type="string"),
        ])

    with pytest.raises(ValueError, match="display-only"):
        ParameterSchema(parameters=[
            AlgorithmParameter(key="history", label_zh="展示历史", value_type="number", affects_science=False),
        ])
