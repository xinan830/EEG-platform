from __future__ import annotations

import pytest
from pydantic import ValidationError

from app.algorithm_runtime.contracts import AlgorithmConfigBase, AlgorithmFailure, AlgorithmResult
from app.algorithm_runtime.parameter_schema import AlgorithmParameter, ParameterSchema
from app.algorithm_runtime.executor import AlgorithmRuntime


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


def test_runtime_attaches_canonical_sample_coordinate_without_replacing_display_range() -> None:
    evidence = AlgorithmRuntime._with_sample_coordinate(
        {"source_quality": {}}, {"start_s": 1.001, "end_s": 3.499}, 100.0,
    )
    assert evidence["extensions"]["sample_coordinate"] == {
        "coordinate_system": "recording_relative_sample",
        "sample_range": {"start_sample": 100, "end_sample": 350, "half_open": True},
        "sfreq_hz": 100.0,
    }


def test_dynamic_result_state_contract_is_versioned_and_legacy_warmup_is_retained() -> None:
    from app.algorithm_runtime.contracts import AlgorithmSeriesResult, DYNAMIC_ANALYSIS_RESULT_CONTRACT_VERSION

    result = AlgorithmSeriesResult(
        values=[None, 1.0], time_s=[4.0, 5.0], unit="Hz", channel="Fz",
        windows=[{"start_s": 0.0, "end_s": 4.0}, {"start_s": 1.0, "end_s": 5.0}],
        quality=["gate_failed", "clean"], failures=[
            AlgorithmFailure(code="GAP", message="缺口"), None,
        ], warmups=[True, False], states=["Rejected", "Complete"],
    )
    assert DYNAMIC_ANALYSIS_RESULT_CONTRACT_VERSION == "dynamic-analysis-frame-v5"
    assert result.states == ["Rejected", "Complete"]
    assert result.warmups == [True, False]


def test_window_planner_uses_state_as_source_of_truth_and_exposes_legacy_warmup_view() -> None:
    from app.algorithm_runtime.windows import build_dynamic_analysis_frames, build_playback_windows

    planned = build_playback_windows(0, 12, duration_s=20, window_s=10, step_s=1)
    assert planned[0].state == "Partial"
    assert planned[0].warmup is True
    assert planned[-1].state == "Complete"
    assert planned[-1].warmup is False

    frames = build_dynamic_analysis_frames(0, 12, duration_s=20, window_s=10, step_s=1)
    assert [frame.state for frame in frames] == ["Partial"] * 6 + ["Complete"] * 3
    assert [frame.warmup for frame in frames] == [True] * 6 + [False] * 3


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


def test_parameter_schema_validates_numeric_constraints_and_enum_options() -> None:
    parameter = AlgorithmParameter(
        key="window_s",
        label_zh="窗口",
        value_type="number",
        minimum=4,
        maximum=20,
        step=1,
    )
    parameter.validate_value(10)
    with pytest.raises(ValueError, match="below its minimum"):
        parameter.validate_value(3)
    with pytest.raises(ValueError, match="step"):
        parameter.validate_value(4.5)

    with pytest.raises(ValueError, match="non-empty"):
        AlgorithmParameter(
            key="mode",
            label_zh="模式",
            value_type="enum",
            options=[],
        )
