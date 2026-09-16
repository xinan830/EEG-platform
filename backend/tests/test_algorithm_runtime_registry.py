from __future__ import annotations

import pytest
from pydantic import BaseModel

from app.algorithm_runtime.contracts import AlgorithmConfigBase, AlgorithmInputs, AlgorithmManifest, AlgorithmResult
from app.algorithm_runtime.errors import DuplicateAlgorithmError, UnknownAlgorithmError
from app.algorithm_runtime.executor import AlgorithmRuntime
from app.algorithm_runtime.parameter_schema import ParameterSchema
from app.algorithm_runtime.registry import AlgorithmRegistry
from app.algorithm_runtime.windows import build_windows


class DemoConfig(AlgorithmConfigBase):
    pass


class DemoAlgorithm:
    manifest = AlgorithmManifest(
        algorithm_id="demo",
        display_name_zh="测试算法",
        abbreviation="DEMO",
        purpose_zh="运行时测试",
        scientific_version="1.0.0",
        implementation_identity="test-demo-1",
        supported_modes=["static"],
        output_unit="dimensionless",
    )
    config_model = DemoConfig

    def parameter_schema(self) -> ParameterSchema:
        return ParameterSchema()

    def resolve_inputs(self, recording, config):
        return AlgorithmInputs(recording_id="r1", channel=config.channel, sfreq_hz=500, duration_s=20, payload=recording)

    def execute_static(self, inputs, config):
        return AlgorithmResult(
            value=1.0,
            unit="dimensionless",
            channel=inputs.channel,
            requested_range={"start_s": config.start_s, "end_s": config.end_s},
            actual_range={"start_s": config.start_s, "end_s": config.end_s},
            quality="clean",
        )

    def execute_dynamic(self, inputs, config):
        raise AssertionError("unsupported mode should be rejected")


def test_registry_rejects_duplicate_identity_and_unknown_id() -> None:
    registry = AlgorithmRegistry()
    registry.register(DemoAlgorithm())
    with pytest.raises(DuplicateAlgorithmError):
        registry.register(DemoAlgorithm())
    with pytest.raises(UnknownAlgorithmError):
        registry.get("missing")


def test_runtime_validates_config_and_dispatches_without_algorithm_branch() -> None:
    registry = AlgorithmRegistry()
    registry.register(DemoAlgorithm())
    result = AlgorithmRuntime(registry).execute(
        algorithm_id="demo",
        recording={"id": "r1"},
        config={"channel": "O2", "mode": "static", "start_s": 0, "end_s": 10},
    )
    assert result.value == 1.0
    assert result.channel == "O2"


def test_build_windows_is_deterministic_and_bounded() -> None:
    windows = build_windows(0, 10, duration_s=12, window_s=4, step_s=2)
    assert [(item.start_s, item.end_s, item.center_s) for item in windows] == [
        (0, 4, 2), (2, 6, 4), (4, 8, 6), (6, 10, 8)
    ]
    assert build_windows(10, 20, duration_s=12, window_s=4, step_s=2) == []
