from types import SimpleNamespace

import pytest

from app.eeg_core.definition_engine import DefinitionEngineError
from app.eeg_core.primitives.units import Unit
from app.eeg_core.quality import SpectralQualityGateError
from app.models.algorithm_definition import DefinitionVersionDraft
from app.models.definition_metric_run import DefinitionMetricConfig
from app.services.definition_metric_runner import DefinitionMetricRunner


def _draft(left_feature: str = "theta_power", right_feature: str = "beta_power") -> DefinitionVersionDraft:
    return DefinitionVersionDraft.model_validate({
        "semver": "1.0.0",
        "graph": {"nodes": [], "outputs": ["result"]},
        "inputs": {
            "input_left": {"feature": left_feature, "unit": "uV^2"},
            "input_right": {"feature": right_feature, "unit": "uV^2"},
        },
    })


class FakeRecordings:
    def __init__(self):
        self.calls: list[tuple[object, float, float, list[str]]] = []

    def load_spectrum(self, recording, start_s: float, window_s: float, channels: list[str]):
        self.calls.append((recording, start_s, window_s, channels))
        if channels != ["F3"]:
            raise ValueError("频谱分析请求包含不存在的通道")
        return {
            "channels": ["F3"],
            "window_start_s": start_s,
            "window_duration_s": window_s,
            "band_power": {"F3": {"delta": 1.0, "theta": 4.0, "alpha": 9.0, "beta": 2.0}},
            "relative_band_power": {"F3": {"delta": 0.1, "theta": 0.4, "alpha": 0.3, "beta": 0.2}},
            "quality": {"clean_segments": 14, "total_segments": 14, "clean_ratio": 1.0},
        }


def test_resolves_absolute_theta_and_beta_for_requested_channel():
    recordings = FakeRecordings()
    runner = DefinitionMetricRunner(recordings)
    resolution = runner.resolve_inputs(
        SimpleNamespace(id="recording-1"), _draft(),
        DefinitionMetricConfig.model_validate({"channel": "F3", "time": {"start_s": 10, "end_s": 40}}),
    )

    assert resolution.inputs["input_left"].value == 4.0
    assert resolution.inputs["input_left"].unit is Unit.UV2
    assert resolution.inputs["input_right"].value == 2.0
    assert resolution.snapshot["input_left"] == {"feature": "theta_power", "value": 4.0, "unit": "uV^2", "channel": "F3"}
    assert recordings.calls[0][1:] == (10.0, 30.0, ["F3"])


def test_resolves_relative_power_as_a_ratio():
    resolution = DefinitionMetricRunner(FakeRecordings()).resolve_inputs(
        SimpleNamespace(id="recording-1"), _draft("theta_rbp", "beta_rbp"),
        DefinitionMetricConfig.model_validate({"channel": "F3", "time": {"start_s": 0, "end_s": 30}}),
    )

    assert resolution.inputs["input_left"].value == 0.4
    assert resolution.inputs["input_left"].unit is Unit.RATIO


def test_rejects_unsupported_feature_and_missing_channel():
    runner = DefinitionMetricRunner(FakeRecordings())
    config = DefinitionMetricConfig.model_validate({"channel": "F3", "time": {"start_s": 0, "end_s": 30}})

    with pytest.raises(DefinitionEngineError, match="unsupported metric feature") as unsupported:
        runner.resolve_inputs(SimpleNamespace(id="recording-1"), _draft("not_a_feature"), config)
    assert unsupported.value.code == "METRIC_FEATURE_UNSUPPORTED"

    with pytest.raises(DefinitionEngineError, match="metric channel is unavailable") as missing:
        runner.resolve_inputs(SimpleNamespace(id="recording-1"), _draft(), config.model_copy(update={"channel": "Missing"}))
    assert missing.value.code == "METRIC_CHANNEL_UNAVAILABLE"


def test_preserves_spectral_quality_gate_for_the_run_lifecycle():
    class GatedRecordings:
        def load_spectrum(self, *_args, **_kwargs):
            raise SpectralQualityGateError({"clean_segments": 0, "total_segments": 4, "gate_failed": "clean_ratio_below_threshold"})

    with pytest.raises(SpectralQualityGateError):
        DefinitionMetricRunner(GatedRecordings()).resolve_inputs(
            SimpleNamespace(id="recording-1"), _draft(),
            DefinitionMetricConfig.model_validate({"channel": "F3", "time": {"start_s": 0, "end_s": 30}}),
        )
