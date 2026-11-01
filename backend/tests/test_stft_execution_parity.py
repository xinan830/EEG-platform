"""Exercise shared source validation through all three public STFT adapters."""

from types import SimpleNamespace

import numpy as np
import pytest

from app.algorithm_runtime.result_validation import validate_algorithm_result
from app.algorithms.stft.runner import StftAlgorithm
from app.scientific.quality import SpectralQualityGateError
from tests.test_stft_on_demand_capability import _config, _identity, _inputs, _payload, _session


def _execute(loader):
    algorithm = StftAlgorithm()
    config = _config().model_copy(update={"start_s": 10, "end_s": 15})
    inputs = _inputs(SimpleNamespace(load_spectrogram=loader))
    static = algorithm.execute_static(inputs, config)
    full = algorithm.execute_dynamic(inputs, config)
    plan = algorithm.plan_window(inputs, config, 3000, _session())
    window = algorithm.execute_window(inputs, config, plan, _identity(plan))
    validate_algorithm_result(static, algorithm.manifest, "static")
    validate_algorithm_result(full, algorithm.manifest, "dynamic")
    return static, full, window


def test_static_full_and_window_share_source_units_evidence_and_nonzero_coordinates():
    requests = []

    def loader(**kwargs):
        requests.append(kwargs)
        return _payload(kwargs["window_s"], start_s=kwargs["start_s"])

    static, full, window = _execute(loader)
    assert len(requests) == 3  # one source load per strategy for this nonzero range
    assert requests[0] == requests[1] == requests[2]
    assert requests[0] == dict(start_s=10, window_s=5, channels=["F3"], low_hz=1,
                               high_hz=30, notch_hz=None, output_low_hz=1, output_high_hz=30)
    assert static.actual_range == {"start_s": 10, "end_s": 15}
    np.testing.assert_array_equal(static.axes["time_center_s"], [12, 13])
    np.testing.assert_array_equal(window.axes["time_center_s"], [2, 3])
    for name in static.arrays:
        np.testing.assert_array_equal(static.arrays[name], full.arrays[name][0])
        np.testing.assert_array_equal(static.arrays[name], window.arrays[name])
    assert static.arrays["power_linear"][0, 0] == 1e-12
    assert static.arrays["power_db"][0, 0] == 10
    assert static.evidence == full.windows[0].evidence == window.evidence
    assert window.provenance == full.windows[0].provenance
    assert window.provenance["analysis_state"] == "Complete"


def test_only_full_strategy_pads_warmup_without_inventing_valid_rows():
    algorithm = StftAlgorithm()
    config = _config()
    inputs = _inputs(SimpleNamespace(load_spectrogram=lambda **kw: _payload(
        kw["window_s"], start_s=kw["start_s"],
    )))
    full = algorithm.execute_dynamic(inputs, config)
    plan = algorithm.plan_window(inputs, config, 800, _session())
    window = algorithm.execute_window(inputs, config, plan, _identity(plan))
    assert full.windows[0].state == window.provenance["analysis_state"] == "Partial"
    assert window.arrays["power_linear"].shape[0] == 1
    np.testing.assert_array_equal(window.arrays["power_linear"][0], full.arrays["power_linear"][0, 0])
    assert np.isnan(full.arrays["power_linear"][0, 1]).all()


@pytest.mark.parametrize("all_bad", [False, True])
def test_bad_inner_nan_rows_are_quality_rejections_in_both_dynamic_strategies(all_bad):
    def loader(**kwargs):
        count = 1 if kwargs["window_s"] < 5 else 2
        return _payload(kwargs["window_s"], count if all_bad else 1, kwargs["start_s"])

    static, full, window = _execute(loader)
    assert static.quality == ("gate_failed" if all_bad else "partial")
    assert np.isnan(static.arrays["power_linear"][0]).all()
    if not all_bad:
        assert np.isfinite(static.arrays["power_linear"][1]).all()
    assert all(item.state == "Rejected" for item in full.windows)
    assert np.isnan(full.arrays["power_linear"]).all()
    assert np.isnan(full.arrays["power_db"]).all()
    assert window.state == "rejected"
    assert window.axes == window.arrays == {}
    assert window.failure["code"] == full.windows[0].failure.code == "STFT_WINDOW_QUALITY_GATE_FAILED"
    assert static.evidence == full.windows[0].evidence == window.evidence


@pytest.mark.parametrize("damage", ["shape", "nan", "infinity", "negative", "time", "frequency", "quality", "missing"])
def test_malformed_source_is_unavailable_for_every_strategy(damage):
    def loader(**kwargs):
        payload = _payload(kwargs["window_s"], start_s=kwargs["start_s"])
        if damage == "shape":
            payload["power_linear"]["F3"][0].pop()
        elif damage in {"nan", "infinity", "negative"}:
            payload["power_linear"]["F3"][0][0] = {
                "nan": np.nan, "infinity": np.inf, "negative": -1,
            }[damage]
        elif damage == "time":
            payload["times_s"][0] += 0.5
        elif damage == "frequency":
            payload["frequencies_hz"][0] = np.nan
        elif damage == "quality":
            payload["quality"]["bad_windows"] = 1
        else:
            del payload["power_db"]
        return payload

    static, full, window = _execute(loader)
    assert static.quality == "unavailable"
    assert static.failure.code == window.failure["code"] == "STFT_WINDOW_UNAVAILABLE"
    assert static.actual_range is None
    assert all(item.state == "Unavailable" for item in full.windows)
    assert np.isnan(full.arrays["power_linear"]).all()
    assert window.state == "unavailable"
    assert window.axes == window.arrays == {}


def test_raised_source_quality_failure_has_identical_failure_and_evidence():
    quality = {"total_windows": 2, "bad_windows": 2, "evidence": {"gap": True}}

    def loader(**_):
        raise SpectralQualityGateError(quality)

    static, full, window = _execute(loader)
    assert static.quality == "gate_failed"
    assert window.state == "rejected"
    assert static.failure == full.windows[0].failure
    assert static.failure.detail == window.failure["details"] == quality
    assert static.evidence == full.windows[0].evidence == window.evidence


def test_malformed_first_frame_does_not_poison_later_frequency_grid():
    calls = 0

    def loader(**kwargs):
        nonlocal calls
        calls += 1
        payload = _payload(kwargs["window_s"], start_s=kwargs["start_s"])
        if calls == 1:
            payload["frequencies_hz"] = [1]
        return payload

    algorithm = StftAlgorithm()
    inputs = _inputs(SimpleNamespace(load_spectrogram=loader))
    full = algorithm.execute_dynamic(inputs, _config())
    assert calls == len(full.windows)
    assert full.windows[0].state == "Unavailable"
    assert all(item.state == "Complete" for item in full.windows[1:])
    assert np.isfinite(full.arrays["power_linear"][1:]).all()


@pytest.mark.parametrize("strategy", ["static", "full", "window"])
@pytest.mark.parametrize("after_load", [False, True])
@pytest.mark.parametrize("exception_type", [RuntimeError, ValueError])
def test_cancellation_propagates_instead_of_becoming_source_failure(strategy, after_load, exception_type):
    loaded = 0

    def loader(**kwargs):
        nonlocal loaded
        loaded += 1
        return _payload(kwargs["window_s"], start_s=kwargs["start_s"])

    def check():
        if not after_load or loaded:
            raise exception_type("cancelled")

    algorithm = StftAlgorithm()
    config = _config()
    inputs = _inputs(SimpleNamespace(load_spectrogram=loader))
    inputs.execution_context = SimpleNamespace(check_cancelled=check, window_started=lambda: None,
                                               window_completed=lambda: None)
    with pytest.raises(exception_type, match="cancelled"):
        if strategy == "static":
            algorithm.execute_static(inputs, config)
        elif strategy == "full":
            algorithm.execute_dynamic(inputs, config)
        else:
            plan = algorithm.plan_window(inputs, config, 1000, _session())
            algorithm.execute_window(inputs, config, plan, _identity(plan), check_cancelled=check)
    assert loaded == int(after_load)
