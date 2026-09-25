from __future__ import annotations

import pytest
import numpy as np
from pydantic import ValidationError

from app.algorithm_runtime.contracts import (
    AlgorithmConfigBase,
    AlgorithmFailure,
    AlgorithmResult,
    AlgorithmStructuredResult,
    AlgorithmStructuredSeriesResult,
    StructuredSeriesWindow,
)
from app.algorithm_runtime.parameter_schema import AlgorithmParameter, ParameterSchema
from app.algorithm_runtime.executor import AlgorithmRuntime
from app.services.run_analysis_executor import _serialize_structured_result, _serialize_structured_series_result


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


def test_structured_result_declares_axes_units_and_artifact_arrays() -> None:
    result = AlgorithmStructuredResult(
        output_kind="frequency_series",
        channel_order=["Oz", "Fz"],
        axes={"frequency_hz": np.array([1.0, 2.0, 3.0])},
        axis_units={"frequency_hz": "Hz"},
        arrays={"psd": np.ones((2, 3), dtype=float)},
        array_units={"psd": "V^2/Hz"},
        requested_range={"start_s": 0.0, "end_s": 4.0},
        actual_range={"start_s": 0.0, "end_s": 4.0},
        quality="clean",
        evidence={"source_quality": {}, "spectral_evidence": {}, "calculation_trace": {}},
    )

    assert result.output_kind == "frequency_series"
    assert result.channel_order == ["Oz", "Fz"]
    assert result.arrays["psd"].shape == (2, 3)


def test_structured_result_rejects_missing_axis_or_array_unit() -> None:
    with pytest.raises(ValidationError, match="explicit axes"):
        AlgorithmStructuredResult(
            output_kind="frequency_series", channel_order=["Fz"],
            arrays={"psd": np.ones((1, 2))}, array_units={"psd": "V^2/Hz"},
            requested_range={"start_s": 0.0, "end_s": 4.0}, quality="clean",
        )


def test_structured_result_serializer_keeps_axes_and_values_in_artifact_arrays() -> None:
    result = AlgorithmStructuredResult(
        output_kind="time_frequency", channel_order=["Fz"],
        axes={"time_center_s": np.array([2.0]), "frequency_hz": np.array([10.0])},
        axis_units={"time_center_s": "s", "frequency_hz": "Hz"},
        arrays={"power_linear": np.ones((1, 1, 1))},
        array_units={"power_linear": "V^2/Hz"},
        requested_range={"start_s": 0.0, "end_s": 4.0}, quality="clean",
        evidence={"source_quality": {}, "spectral_evidence": {}, "calculation_trace": {}},
    )

    summary, arrays = _serialize_structured_result(result, "stft", "时频分析")

    assert summary["axes"]["frequency_hz"] == {"array_key": "axis_frequency_hz", "unit": "Hz", "length": 1}
    assert summary["arrays"]["power_linear"] == {"unit": "V^2/Hz", "shape": [1, 1, 1]}
    assert set(arrays) == {"power_linear", "axis_time_center_s", "axis_frequency_hz"}

    with pytest.raises(ValidationError, match="array units"):
        AlgorithmStructuredResult(
            output_kind="frequency_series", channel_order=["Fz"],
            axes={"frequency_hz": np.array([1.0, 2.0])},
            axis_units={"frequency_hz": "Hz"},
            arrays={"psd": np.ones((1, 2))}, array_units={},
            requested_range={"start_s": 0.0, "end_s": 4.0}, quality="clean",
        )


def test_structured_series_requires_window_axis_and_keeps_nan_unavailable_rows() -> None:
    failure = AlgorithmFailure(code="GAP", message="记录缺口")
    result = AlgorithmStructuredSeriesResult(
        output_kind="frequency_series",
        channel_order=["Fz"],
        axes={"frequency_hz": np.array([1.0, 2.0])},
        axis_units={"frequency_hz": "Hz"},
        arrays={"psd": np.array([[1.0, 2.0], [np.nan, np.nan]])},
        array_units={"psd": "V^2/Hz"},
        windows=[
            StructuredSeriesWindow(start_sample=0, end_sample=400, start_s=0, end_s=4, state="Complete", quality="clean"),
            StructuredSeriesWindow(start_sample=100, end_sample=500, start_s=1, end_s=5, state="Unavailable", quality="unavailable", failure=failure),
        ],
        requested_range={"start_s": 0.0, "end_s": 5.0},
        quality="partial",
        evidence={"source_quality": {}, "spectral_evidence": {}, "calculation_trace": {}},
    )

    summary, arrays = _serialize_structured_series_result(result, "psd", "功率谱密度")

    assert summary["output"]["mode"] == "dynamic"
    assert summary["arrays"]["psd"] == {"unit": "V^2/Hz", "shape": [2, 2]}
    assert summary["window_state_counts"] == {"Complete": 1, "Unavailable": 1}
    assert np.isnan(arrays["psd"][1]).all()


def test_structured_series_rejects_infinity_and_wrong_window_axis() -> None:
    window = StructuredSeriesWindow(start_sample=0, end_sample=400, start_s=0, end_s=4, state="Complete", quality="clean")
    kwargs = dict(
        output_kind="frequency_series", channel_order=["Fz"],
        axes={"frequency_hz": np.array([1.0, 2.0])}, axis_units={"frequency_hz": "Hz"},
        array_units={"psd": "V^2/Hz"}, windows=[window],
        requested_range={"start_s": 0.0, "end_s": 4.0}, quality="clean",
    )
    with pytest.raises(ValidationError, match="first dimension"):
        AlgorithmStructuredSeriesResult(arrays={"psd": np.ones((2, 2))}, **kwargs)
    with pytest.raises(ValidationError, match="contains infinity"):
        AlgorithmStructuredSeriesResult(arrays={"psd": np.array([[np.inf, 1.0]])}, **kwargs)


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
