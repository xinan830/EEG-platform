from types import SimpleNamespace

import numpy as np
import pytest

from app.algorithm_runtime.contracts import AlgorithmInputs
from app.algorithm_runtime.on_demand import DynamicSessionIdentity, DynamicWindowIdentity
from app.algorithm_runtime.on_demand_result_validation import validate_window_result
from app.algorithms.band_ratio.config import BandRatioConfig
from app.algorithms.band_ratio.runner import BandRatioAlgorithm
from app.algorithms.iapf.config import IapfConfig
from app.algorithms.iapf.runner import IapfAlgorithm
from app.algorithms.peak_frequency.config import PeakFrequencyConfig
from app.algorithms.peak_frequency.runner import PeakFrequencyAlgorithm
from app.algorithms.theta_beta.config import ThetaBetaConfig
from app.algorithms.theta_beta.runner import ThetaBetaAlgorithm


SFREQ = 200.0
DURATION = 22.35


def _session(algorithm) -> DynamicSessionIdentity:
    manifest = algorithm.manifest
    return DynamicSessionIdentity(
        recording_id="recording", source_sha256="source", algorithm_id=manifest.algorithm_id,
        definition_id="definition", definition_version=manifest.definition_persistence.version,
        scientific_version=manifest.scientific_version,
        implementation_identity=manifest.implementation_identity,
        configuration_sha256="config", window_policy_version=algorithm.window_policy_version,
    )


def _inputs(loader) -> AlgorithmInputs:
    return AlgorithmInputs(
        recording_id="recording", channel="F3", sfreq_hz=SFREQ,
        duration_s=DURATION, payload=SimpleNamespace(load_spectrum=loader),
    )


def _identity(session, plan) -> DynamicWindowIdentity:
    return DynamicWindowIdentity(
        **session.model_dump(), start_sample=plan.start_sample, end_sample=plan.end_sample,
    )


def _config(algorithm_id: str):
    common = dict(channel="F3", mode="dynamic", start_s=0, end_s=DURATION,
                  window_s=5, step_s=1)
    if algorithm_id == "band_ratio":
        return BandRatioConfig(**common, numerator_low_hz=8, numerator_high_hz=12,
                               denominator_low_hz=15, denominator_high_hz=25)
    if algorithm_id == "peak_frequency":
        return PeakFrequencyConfig(**common, low_hz=8, high_hz=12)
    if algorithm_id == "iapf":
        return IapfConfig(**common)
    if algorithm_id == "theta_beta":
        return ThetaBetaConfig(**common)
    raise AssertionError(algorithm_id)


def _spectrum(**overrides):
    frequencies = np.arange(1.0, 51.0)
    # A strictly positive 1/f background plus a clear alpha peak gives IAPF a
    # deterministic COG/peak and leaves all scalar algorithms on one source.
    psd = 1e-12 / frequencies
    psd = psd + 8e-12 * np.exp(-0.5 * ((frequencies - 10.0) / 0.8) ** 2)
    values = dict(
        freqs=frequencies, psd=psd[None, :], clean_epochs=1, total_epochs=1,
        signal_quality=1.0, gate_failed=None, rejected_reasons=[],
        evidence={"sfreq_hz": SFREQ, "welch": "synthetic"},
    )
    values.update(overrides)
    return SimpleNamespace(**values)


SPECS = [
    ("band_ratio", BandRatioAlgorithm, "ratio", "ratio"),
    ("peak_frequency", PeakFrequencyAlgorithm, "peak_frequency_hz", "Hz"),
    ("iapf", IapfAlgorithm, "iapf_hz", "Hz"),
    ("theta_beta", ThetaBetaAlgorithm, "theta_beta", "dimensionless"),
]


@pytest.mark.parametrize("algorithm_id,algorithm_type,field,unit", SPECS)
def test_scalar_window_matches_full_dynamic_point(algorithm_id, algorithm_type, field, unit):
    algorithm = algorithm_type()
    config = _config(algorithm_id)
    source = _spectrum()
    inputs = _inputs(lambda **_: source)
    full = algorithm.execute_dynamic(inputs, config)
    session = _session(algorithm)

    assert full.values
    assert full.unit == unit
    for index, frame in enumerate(full.windows):
        plan = algorithm.plan_window(inputs, config,
                                     int(round(frame["end_s"] * SFREQ)), session)
        result = algorithm.execute_window(inputs, config, plan, _identity(session, plan))
        assert (plan.start_s, plan.end_s) == pytest.approx(
            (frame["start_s"], frame["end_s"])
        )
        assert result.state == "ready"
        validate_window_result(result, algorithm.manifest)
        np.testing.assert_allclose(result.axes["time_s"], [frame["end_s"]],
                                   rtol=0, atol=1e-12)
        np.testing.assert_allclose(result.arrays[field], [full.values[index]],
                                   rtol=1e-12, atol=1e-12)
        assert result.array_units[field] == unit
        assert result.evidence == full.point_evidence[index]
        expected_state = "Partial" if frame["end_s"] - frame["start_s"] < config.window_s else "Complete"
        assert result.provenance["analysis_state"] == expected_state
        assert result.provenance["preprocessing_scope"] == "whole_recording_before_window_slice"


@pytest.mark.parametrize("algorithm_id,algorithm_type,_,__", SPECS)
def test_scalar_window_before_first_policy_frame_does_not_load_future_samples(
    algorithm_id, algorithm_type, _, __
):
    algorithm = algorithm_type()
    config = _config(algorithm_id)
    inputs = _inputs(lambda **_: pytest.fail("future samples must not be loaded"))
    session = _session(algorithm)
    plan = algorithm.plan_window(inputs, config, 0, session)
    result = algorithm.execute_window(inputs, config, plan, _identity(session, plan))
    assert plan.state == result.state == "insufficient_data"
    assert not result.axes and not result.arrays


@pytest.mark.parametrize("algorithm_id,algorithm_type,_,__", SPECS)
def test_scalar_window_checks_cancellation_before_loading(algorithm_id, algorithm_type, _, __):
    algorithm = algorithm_type()
    config = _config(algorithm_id)
    inputs = _inputs(lambda **_: pytest.fail("cancelled science must not load data"))
    session = _session(algorithm)
    plan = algorithm.plan_window(inputs, config, 1000, session)
    with pytest.raises(RuntimeError, match="cancelled"):
        algorithm.execute_window(
            inputs, config, plan, _identity(session, plan),
            check_cancelled=lambda: (_ for _ in ()).throw(RuntimeError("cancelled")),
        )


@pytest.mark.parametrize("algorithm_id,algorithm_type,field,unit", SPECS)
def test_scalar_quality_rejection_matches_full_point(algorithm_id, algorithm_type, field, unit):
    algorithm = algorithm_type()
    config = _config(algorithm_id)
    source = _spectrum(gate_failed="amplitude_threshold", signal_quality=0.0,
                       clean_epochs=0, rejected_reasons=["amplitude_threshold"])
    inputs = _inputs(lambda **_: source)
    full = algorithm.execute_dynamic(inputs, config)
    session = _session(algorithm)
    frame = full.windows[0]
    plan = algorithm.plan_window(inputs, config, int(round(frame["end_s"] * SFREQ)), session)
    result = algorithm.execute_window(inputs, config, plan, _identity(session, plan))
    assert result.state == "rejected"
    assert full.failures[0] is not None
    assert result.failure["code"] == full.failures[0].code
    assert result.failure["details"] == full.failures[0].detail
    assert result.evidence == full.point_evidence[0]


def test_scalar_unavailable_preserves_null_for_band_ratio_and_peak_frequency():
    band = BandRatioAlgorithm()
    band_config = _config("band_ratio")
    band_inputs = _inputs(lambda **_: _spectrum(psd=np.zeros((1, 50))))
    band_session = _session(band)
    band_plan = band.plan_window(band_inputs, band_config, 1000, band_session)
    band_result = band.execute_window(band_inputs, band_config, band_plan,
                                     _identity(band_session, band_plan))
    assert band_result.state == "unavailable"
    assert band_result.failure["code"] == "BAND_RATIO_DENOMINATOR_INVALID"
    assert np.isnan(band_result.arrays["ratio"][0])

    peak = PeakFrequencyAlgorithm()
    peak_config = _config("peak_frequency")
    peak_inputs = _inputs(lambda **_: _spectrum(freqs=np.arange(1.0, 6.0),
                                                 psd=np.ones((1, 5))))
    peak_session = _session(peak)
    peak_plan = peak.plan_window(peak_inputs, peak_config, 1000, peak_session)
    peak_result = peak.execute_window(peak_inputs, peak_config, peak_plan,
                                     _identity(peak_session, peak_plan))
    assert peak_result.state == "unavailable"
    assert peak_result.failure["code"] == "PEAK_FREQUENCY_NO_BIN"
    assert np.isnan(peak_result.arrays["peak_frequency_hz"][0])


def test_iapf_unavailable_is_not_a_numeric_zero():
    algorithm = IapfAlgorithm()
    config = _config("iapf")
    frequencies = np.arange(1.0, 51.0)
    powers = 1e-12 / frequencies
    powers[(frequencies >= 7) & (frequencies <= 13)] *= 0.1
    source = _spectrum(psd=powers[None, :])
    inputs = _inputs(lambda **_: source)
    session = _session(algorithm)
    plan = algorithm.plan_window(inputs, config, 1000, session)
    result = algorithm.execute_window(inputs, config, plan, _identity(session, plan))
    assert result.state == "rejected"
    assert result.failure["code"] == "no_peak_no_cog"
    assert np.isnan(result.arrays["iapf_hz"][0])
