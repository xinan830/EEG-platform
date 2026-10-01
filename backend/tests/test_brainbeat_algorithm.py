from __future__ import annotations

import numpy as np
import pytest
from pydantic import ValidationError

from app.algorithms.brainbeat.runner import BrainbeatAlgorithm
from app.algorithm_runtime.contracts import AlgorithmInputs
from app.models.official_algorithm_run import OfficialAlgorithmRunConfig
from app.scientific.primitives import SpectralEstimate


class FakeRecording:
    id = "brainbeat-test"
    channel_names = ["FZ", "PZ", "O1"]
    sfreq_hz = 100.0
    duration_s = 20.0

    def __init__(self, *, gate_failed: str | None = None):
        self.calls: list[dict[str, object]] = []
        self.gate_failed = gate_failed

    def load_spectrum(self, *, start_s: float, window_s: float, channels: list[str]):
        self.calls.append({"start_s": start_s, "window_s": window_s, "channels": channels})
        frequencies = np.arange(1.0, 30.25, 0.25)
        fz = np.ones_like(frequencies)
        pz = np.ones_like(frequencies)
        fz[(frequencies >= 4.0) & (frequencies <= 8.0)] = 3.0
        pz[(frequencies >= 8.0) & (frequencies <= 12.0)] = 2.0
        return SpectralEstimate(
            frequencies,
            np.vstack([fz, pz]) * 1e-12,
            1.0 if self.gate_failed is None else 0.5,
            4 if self.gate_failed is None else 2,
            4,
            self.gate_failed,
            ("amplitude_threshold",) if self.gate_failed else (),
            evidence={"fixture": True},
        )


def _config(**overrides):
    values = {
        "channel": "Fz", "secondary_channel": "Pz", "mode": "static",
        "start_s": 0.0, "end_s": 10.0, "window_s": 5.0, "step_s": 1.0,
    }
    values.update(overrides)
    return BrainbeatAlgorithm.config_model.model_validate(values)


def test_static_brainbeat_uses_explicit_fz_pz_and_preserves_ratio_result():
    recording = FakeRecording()
    algorithm = BrainbeatAlgorithm()
    config = _config(channel="fz", secondary_channel="pz")
    inputs = algorithm.resolve_inputs(recording, config)

    result = algorithm.execute_static(inputs, config)

    assert result.value is not None and result.value > 0
    assert result.channel == "FZ/PZ"
    assert recording.calls == [{"start_s": 0.0, "window_s": 10.0, "channels": ["FZ", "PZ"]}]
    assert result.evidence["extensions"]["brainbeat_evidence"]["formula"] == "relative_theta(Fz) / relative_alpha(Pz)"


def test_brainbeat_rejects_missing_or_identical_source_channels():
    algorithm = BrainbeatAlgorithm()
    with pytest.raises(ValidationError, match="requires Fz"):
        _config(channel="Fp1", secondary_channel="Pz")
    with pytest.raises(ValueError, match="must be different"):
        _config(secondary_channel="Fz")
    recording = FakeRecording()
    recording.channel_names = ["Fz", "O1"]
    with pytest.raises(ValueError, match="does not exist"):
        algorithm.resolve_inputs(recording, _config())


def test_dynamic_brainbeat_calculates_independent_windows_and_keeps_quality_failure_null():
    recording = FakeRecording(gate_failed="low_quality")
    algorithm = BrainbeatAlgorithm()
    config = _config(mode="dynamic", end_s=8.0)
    inputs = algorithm.resolve_inputs(recording, config)

    result = algorithm.execute_dynamic(inputs, config)

    assert len(result.values) == len(result.time_s) == len(result.windows)
    assert len(result.values) == 5
    assert all(value is None for value in result.values)
    assert all(state == "Rejected" for state in result.states)
    assert all(failure is not None and failure.code == "PSD_QUALITY_GATE_FAILED" for failure in result.failures)
    assert [call["start_s"] for call in recording.calls] == [0.0, 0.0, 1.0, 2.0, 3.0]
    assert [call["window_s"] for call in recording.calls] == [4.0, 5.0, 5.0, 5.0, 5.0]


def test_official_run_boundary_accepts_brainbeat_secondary_channel():
    config = OfficialAlgorithmRunConfig.model_validate({
        "algorithm_id": "brainbeat", "channel": "Fz", "secondary_channel": "Pz",
        "time": {"start_s": 0.0, "end_s": 10.0}, "mode": "static",
    })
    assert config.runtime_config()["secondary_channel"] == "Pz"

    with pytest.raises(ValidationError, match="Fz and Pz"):
        OfficialAlgorithmRunConfig.model_validate({
            "algorithm_id": "brainbeat", "channel": "Fz",
            "time": {"start_s": 0.0, "end_s": 10.0},
        })
