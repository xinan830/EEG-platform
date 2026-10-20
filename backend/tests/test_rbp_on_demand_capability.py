from types import SimpleNamespace

import numpy as np
import pytest

from app.algorithm_runtime.contracts import AlgorithmInputs
from app.algorithm_runtime.on_demand import DynamicSessionIdentity, DynamicWindowIdentity
from app.algorithm_runtime.on_demand_result_validation import validate_window_result
from app.algorithms.rbp.config import RbpConfig
from app.algorithms.rbp.runner import RbpAlgorithm
from app.scientific.primitives.spectral import estimate_welch_psd, preprocess_offline


def config():
    return RbpConfig(channel="F3", mode="dynamic", start_s=0, end_s=22.35, window_s=5, step_s=1)


def inputs(loader):
    return AlgorithmInputs(recording_id="recording", channel="F3", sfreq_hz=200,
                           duration_s=22.35, payload=SimpleNamespace(load_spectrum=loader))


def session():
    manifest = RbpAlgorithm.manifest
    return DynamicSessionIdentity(recording_id="recording", source_sha256="source", algorithm_id="rbp",
        definition_id="definition", definition_version=manifest.definition_persistence.version,
        scientific_version=manifest.scientific_version, implementation_identity=manifest.implementation_identity,
        configuration_sha256="config", window_policy_version="rbp-dynamic-window-v1")


def execute(algorithm, source, cursor, check=None):
    plan = algorithm.plan_window(source, config(), cursor, session())
    identity = DynamicWindowIdentity(**session().model_dump(), start_sample=plan.start_sample, end_sample=plan.end_sample)
    return plan, algorithm.execute_window(source, config(), plan, identity, check_cancelled=check)


def test_rbp_continuous_source_each_window_matches_full_run_and_loads_only_one_frame():
    sfreq = 200
    time_s = np.arange(4470) / sfreq
    raw = (10e-6 * np.sin(2 * np.pi * 10 * time_s) + 3e-6 * np.sin(2 * np.pi * 6 * time_s))[:, None]
    prepared = preprocess_offline(raw, sfreq, low_hz=1, high_hz=50, notch_hz=50)
    calls = []

    def spectrum(**kwargs):
        calls.append(kwargs)
        assert (kwargs["filter_low_hz"], kwargs["filter_high_hz"], kwargs["notch_hz"]) == (1, 50, 50)
        start = int(round(kwargs["start_s"] * sfreq))
        end = start + int(round(kwargs["window_s"] * sfreq))
        return estimate_welch_psd(prepared[start:end], sfreq, low_hz=1, high_hz=50)

    source = inputs(spectrum)
    algorithm = RbpAlgorithm()
    full = algorithm.execute_dynamic(source, config())
    for index, frame in enumerate(full.windows):
        calls.clear()
        plan, window = execute(algorithm, source, int(round(frame["end_s"] * sfreq)))
        assert len(calls) == 1
        assert (plan.start_s, plan.end_s) == (frame["start_s"], frame["end_s"])
        assert window.state == "ready"
        validate_window_result(window, algorithm.manifest)
        for name, value in full.output_values[index].items():
            np.testing.assert_allclose(window.arrays[name], [value], rtol=1e-12, atol=1e-12)
        assert window.evidence == full.point_evidence[index]
        assert window.provenance["filter_bandpass_hz"] == [1, 50]
        assert sum(value[0] for value in window.arrays.values()) == pytest.approx(1)


@pytest.mark.parametrize("cursor", [0, 799])
def test_rbp_before_first_window_never_loads_future_samples(cursor):
    plan, window = execute(RbpAlgorithm(), inputs(lambda **_: pytest.fail("future samples loaded")), cursor)
    assert plan.state == "insufficient_data"
    assert window.state == "insufficient_data"
    assert not window.arrays


def test_rbp_quality_rejection_matches_full_dynamic_frame():
    spectrum = SimpleNamespace(freqs=np.arange(1, 51), psd=np.ones((1, 50)), clean_epochs=0,
        total_epochs=1, signal_quality=0, gate_failed="amplitude_threshold",
        rejected_reasons=["amplitude_threshold"], evidence={"amplitude": {"peak_uv": 350, "threshold_uv": 200}})
    source = inputs(lambda **_: spectrum)
    algorithm = RbpAlgorithm()
    full = algorithm.execute_dynamic(source, config())
    _, window = execute(algorithm, source, 800)
    assert window.state == "rejected"
    assert window.failure["code"] == full.failures[0].code
    assert window.failure["details"] == full.failures[0].detail
    assert window.evidence == full.point_evidence[0]


def test_rbp_invalid_denominator_is_unavailable_not_zero_or_execution_failure():
    spectrum = SimpleNamespace(freqs=np.arange(1, 51), psd=np.zeros((1, 50)), clean_epochs=1,
        total_epochs=1, signal_quality=1, gate_failed=None, rejected_reasons=[], evidence={})
    _, window = execute(RbpAlgorithm(), inputs(lambda **_: spectrum), 1000)
    assert window.state == "unavailable"
    assert window.failure["code"] == "RBP_WINDOW_UNAVAILABLE"
    assert all(np.isnan(value[0]) for value in window.arrays.values())


def test_rbp_cancelled_execution_does_not_load_a_spectrum():
    def cancelled():
        raise RuntimeError("cancelled")
    with pytest.raises(RuntimeError, match="cancelled"):
        execute(RbpAlgorithm(), inputs(lambda **_: pytest.fail("cancelled science loaded")), 1000, cancelled)
