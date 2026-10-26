from types import SimpleNamespace

import numpy as np
import pytest

from app.algorithm_runtime.contracts import AlgorithmEvidence
from app.algorithm_runtime.executor import AlgorithmRuntime
from app.algorithm_runtime.errors import UnsupportedAlgorithmModeError
from app.algorithm_runtime.registry import AlgorithmRegistry
from app.algorithms.tnk.runtime import build_tnk_child_adapters


class _Recording:
    id = "synthetic"
    channel_names = ["Cz", "F3", "F4", "Pz", "Oz"]
    sfreq_hz = 100.0
    duration_s = 10.0

    def load_spectrum(self, **kwargs):
        frequencies = np.arange(1.0, 30.01, 0.25)
        background = np.maximum(frequencies, 1.0) ** -1.5
        alpha_peak = np.exp(-0.5 * ((frequencies - 10.0) / 0.8) ** 2)
        psd = np.vstack([background + 5.0 * alpha_peak for _ in kwargs["channels"]])
        return SimpleNamespace(
            freqs=frequencies, psd=psd, signal_quality=1.0,
            gate_failed=None,
        )


class _MontageRecording(_Recording):
    channel_names = ["Fz-AVG", "Pz-AVG", "Oz-AVG"]


def _module(algorithm_id: str):
    return next(item for item in build_tnk_child_adapters() if item.manifest.algorithm_id == algorithm_id)


def test_tnk_static_execution_uses_registered_runtime_adapter():
    module = _module("tnk_rbp")
    config = module.config_model(channel="Cz", mode="static", start_s=0, end_s=4)
    result = module.execute_static(module.resolve_inputs(_Recording(), config), config)
    assert result.quality == "clean"
    assert result.output_values is not None
    assert set(result.output_values) == {"delta", "theta", "alpha", "beta"}


def test_tnk_dynamic_execution_replays_window_sequence():
    module = _module("tnk_rbp")
    config = module.config_model(
        channel="Cz", mode="dynamic", start_s=0, end_s=8,
        window_s=4, step_s=1,
    )
    result = module.execute_dynamic(module.resolve_inputs(_Recording(), config), config)
    assert len(result.values) == 5
    assert len(result.windows) == len(result.states) == len(result.point_evidence)
    assert all(state == "Complete" for state in result.states)


def test_tnk_faa_dynamic_execution_is_structurally_unsupported():
    module = _module("tnk_faa")
    config = module.config_model(channel="F3", secondary_channel="F4", mode="dynamic", start_s=0, end_s=20,
                                 window_s=20, step_s=1)
    with pytest.raises(UnsupportedAlgorithmModeError) as error:
        module.execute_dynamic(module.resolve_inputs(_Recording(), config), config)
    assert error.value.detail["reason_code"] == "TNK_FAA_SEGMENT_SCOPE_REQUIRED"


def test_tnk_iapf_static_evidence_uses_declared_extension_and_preserves_fit_metrics():
    module = _module("tnk_iapf")
    registry = AlgorithmRegistry()
    registry.register(module)

    result = AlgorithmRuntime(registry).execute(
        algorithm_id="tnk_iapf",
        recording=_Recording(),
        config={
            "channel": "Pz", "secondary_channel": "Oz", "mode": "static",
            "start_s": 0, "end_s": 4,
        },
    )

    evidence = AlgorithmEvidence.model_validate(result.evidence)
    tnk_evidence = evidence.extensions["tnk_evidence"]
    assert evidence.source_quality["clean_ratio"] == 1.0
    assert tnk_evidence["provenance"]["algorithm_id"] == "tnk_iapf"
    assert tnk_evidence["candidate_count"] == 1
    assert "fit_r_squared" in tnk_evidence
    assert "fit_mae" in tnk_evidence
    assert "live_hz" in tnk_evidence


def test_tnk_dynamic_evidence_uses_the_same_named_extension():
    module = _module("tnk_iapf")
    registry = AlgorithmRegistry()
    registry.register(module)

    result = AlgorithmRuntime(registry).execute(
        algorithm_id="tnk_iapf",
        recording=_Recording(),
        config={
            "channel": "Pz", "secondary_channel": "Oz", "mode": "dynamic",
            "start_s": 0, "end_s": 8, "window_s": 5, "step_s": 1,
        },
    )

    summary = AlgorithmEvidence.model_validate(result.evidence)
    assert summary.extensions["tnk_evidence"] == {
        "algorithm_id": "tnk_iapf", "window_s": 5.0, "step_s": 1.0,
    }
    for row in result.point_evidence:
        point = AlgorithmEvidence.model_validate(row)
        assert point.extensions["tnk_evidence"]["provenance"]["algorithm_id"] == "tnk_iapf"


def test_tnk_runtime_returns_selected_source_channels_while_computing_by_roles():
    module = _module("tnk_iapf")
    registry = AlgorithmRegistry()
    registry.register(module)

    result = AlgorithmRuntime(registry).execute(
        algorithm_id="tnk_iapf",
        recording=_MontageRecording(),
        config={
            "channel": "Fz-AVG", "secondary_channel": "Pz-AVG", "mode": "static",
            "start_s": 0, "end_s": 4,
        },
    )

    assert result.channel == "Fz-AVG/Pz-AVG"


def test_tnk_runtime_passes_spectral_quality_gate_to_scientific_module():
    class RejectedRecording(_Recording):
        def load_spectrum(self, **kwargs):
            spectrum = super().load_spectrum(**kwargs)
            spectrum.gate_failed = True
            spectrum.signal_quality = 0.0
            return spectrum

    module = _module("tnk_rbp")
    config = module.config_model(channel="Cz", mode="static", start_s=0, end_s=4)
    result = module.execute_static(module.resolve_inputs(RejectedRecording(), config), config)

    assert result.value is None
    assert result.quality == "unavailable"
    assert result.failure is not None
    assert result.failure.code == "TNK_QUALITY_REJECTED"
