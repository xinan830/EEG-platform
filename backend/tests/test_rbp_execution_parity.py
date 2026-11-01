"""Standalone RBP must keep one scientific outcome across public strategies."""

from types import SimpleNamespace

import numpy as np
import pytest

from app.algorithm_runtime.errors import InvalidAlgorithmResultError
from app.algorithm_runtime.executor import AlgorithmRuntime
from app.algorithm_runtime.on_demand import DynamicSessionIdentity, DynamicWindowIdentity
from app.algorithm_runtime.on_demand_result_validation import validate_window_result
from app.algorithm_runtime.registry import AlgorithmRegistry
from app.algorithm_runtime.result_validation import validate_algorithm_result
from app.algorithms.rbp.config import RbpConfig
from app.algorithms.rbp.runner import RbpAlgorithm
from app.scientific.quality import SpectralQualityGateError


def recording(outcome):
    calls = []

    def load_spectrum(**kwargs):
        calls.append(kwargs)
        if outcome == "raised_gate":
            raise SpectralQualityGateError({"gate_failed": "amplitude", "clean_ratio": 0.0,
                                           "evidence": {"source": "fixture"}})
        frequencies = np.arange(1.0, 50.25, 0.25)
        psd = np.vstack([np.ones_like(frequencies), np.ones_like(frequencies) * 2])
        if outcome == "partial":
            psd[1] = 0
        if outcome == "zero":
            psd[:] = 0
        return SimpleNamespace(freqs=frequencies, psd=psd,
            gate_failed="amplitude" if outcome == "returned_gate" else None,
            signal_quality=1.0, clean_epochs=2, total_epochs=2,
            rejected_reasons=[], evidence={"source": "fixture"})

    return SimpleNamespace(id="rbp-parity", channel_names=["F4-REF", "F3-REF"],
        sfreq_hz=200.0, duration_s=6.0, load_spectrum=load_spectrum), calls


def window(algorithm, inputs, config, check=None):
    session = DynamicSessionIdentity(recording_id=inputs.recording_id, source_sha256="source",
        algorithm_id="rbp", definition_id="definition",
        definition_version=algorithm.manifest.definition_persistence.version,
        scientific_version=algorithm.manifest.scientific_version,
        implementation_identity=algorithm.manifest.implementation_identity,
        configuration_sha256="config", window_policy_version=algorithm.window_policy_version)
    plan = algorithm.plan_window(inputs, config, 1000, session)
    identity = DynamicWindowIdentity(**session.model_dump(),
                                    start_sample=plan.start_sample, end_sample=plan.end_sample)
    return algorithm.execute_window(inputs, config, plan, identity, check_cancelled=check)


@pytest.mark.parametrize("outcome,quality", [
    ("clean", "clean"), ("partial", "partial"), ("zero", "unavailable"),
    ("raised_gate", "gate_failed"), ("returned_gate", "gate_failed"),
])
def test_static_full_and_window_share_values_failures_and_evidence(outcome, quality):
    source, calls = recording(outcome)
    algorithm = RbpAlgorithm()
    registry = AlgorithmRegistry()
    registry.register(algorithm)
    runtime = AlgorithmRuntime(registry)
    payload = dict(channel="F4-REF", channels=["F4-REF", "F3-REF"], start_s=0, end_s=5,
                   window_s=5, step_s=1)
    static = runtime.execute(algorithm_id="rbp", recording=source, config=payload | {"mode": "static"})
    full = runtime.execute(algorithm_id="rbp", recording=source, config=payload | {"mode": "dynamic"})
    config = RbpConfig(**payload, mode="dynamic")
    before = len(calls)
    target = window(algorithm, algorithm.resolve_inputs(source, config), config)
    assert len(calls) - before == 1
    assert all(call["channels"] == ["F4-REF", "F3-REF"] for call in calls)
    validate_window_result(target, algorithm.manifest)
    last = full.windows[-1]
    assert (last.start_sample, last.end_sample) == (0, 1000)
    assert static.channel_order == full.channel_order == ["F4-REF", "F3-REF"]
    assert static.quality == last.quality == target.quality == quality
    assert full.quality == ("clean" if quality == "clean" else "partial")
    for name in static.arrays:
        np.testing.assert_array_equal(static.arrays[name], full.arrays[name][-1])
        np.testing.assert_array_equal(static.arrays[name], target.arrays[name])
    for key in ("channel_failures", "channel_order"):
        assert static.evidence["extensions"][key] == last.evidence["extensions"][key]
        assert target.evidence["extensions"][key] == last.evidence["extensions"][key]
    assert static.evidence["spectral_evidence"] == last.evidence["spectral_evidence"]
    assert target.evidence["spectral_evidence"] == last.evidence["spectral_evidence"]
    assert target.evidence["spectral_evidence"]["source"] == "fixture"
    assert last.provenance["filter_bandpass_hz"] == target.provenance["filter_bandpass_hz"] == [1, 50]


def test_cancel_after_preparation_stops_before_formula(monkeypatch):
    from app.algorithms.rbp import official

    source, calls = recording("clean")
    algorithm = RbpAlgorithm()
    config = RbpConfig(channel="F4-REF", mode="dynamic", start_s=0, end_s=5, window_s=5, step_s=1)

    def check():
        if calls:
            raise RuntimeError("cancelled after preparation")

    monkeypatch.setattr(official, "five_band_shares", lambda _: pytest.fail("formula ran after cancellation"))
    with pytest.raises(RuntimeError, match="cancelled after preparation"):
        window(algorithm, algorithm.resolve_inputs(source, config), config, check)
    assert len(calls) == 1


def test_channel_set_contract_still_rejects_order_and_dimension_mismatch():
    source, _ = recording("clean")
    algorithm = RbpAlgorithm()
    config = RbpConfig(channel="F4-REF", channels=["F4-REF", "F3-REF"],
                       mode="static", start_s=0, end_s=5)
    result = algorithm.execute_static(algorithm.resolve_inputs(source, config), config)
    with pytest.raises(InvalidAlgorithmResultError, match="order"):
        validate_algorithm_result(result, algorithm.manifest, "static", ["F3-REF", "F4-REF"])
    with pytest.raises(InvalidAlgorithmResultError, match="duplicate"):
        validate_algorithm_result(result, algorithm.manifest, "static", ["F4-REF", "f4-ref"])
    result.channel_order = ["F4-REF"]
    with pytest.raises(InvalidAlgorithmResultError, match="shape"):
        validate_algorithm_result(result, algorithm.manifest, "static", ["F4-REF"])
