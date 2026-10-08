"""Regressions found by the real-recording algorithm audit."""

from types import SimpleNamespace

import numpy as np
import pytest

from app.algorithms.band_ratio.runner import BandRatioAlgorithm
from app.algorithms.peak_frequency.runner import PeakFrequencyAlgorithm
from app.algorithms.peak_frequency.compute import compute_peak_frequency
from app.algorithms.psd.runner import PsdAlgorithm
from app.algorithms.stft.runner import StftAlgorithm
from app.models.run import RunCreateRequest, RunStatus
from app.services.run_queue import PersistentRunQueue
from tests.test_official_algorithm_runs import _service


def config(module, mode="static", **extra):
    values = dict(channel="F3", mode=mode, start_s=0, end_s=10, window_s=5, step_s=1)
    if isinstance(module, PeakFrequencyAlgorithm):
        values.update(low_hz=8, high_hz=13)
    if isinstance(module, BandRatioAlgorithm):
        values.update(numerator_low_hz=4, numerator_high_hz=8,
                      denominator_low_hz=13, denominator_high_hz=30)
    return values | extra


@pytest.mark.parametrize("module", [PeakFrequencyAlgorithm(), BandRatioAlgorithm()])
@pytest.mark.parametrize("mode", ["static", "dynamic"])
@pytest.mark.parametrize("edge", [0, .5, 31, float("nan"), float("inf")])
def test_out_of_domain_config_is_rejected(module, mode, edge):
    field = "low_hz" if isinstance(module, PeakFrequencyAlgorithm) else "denominator_low_hz"
    with pytest.raises(ValueError):
        module.config_model.model_validate(config(module, mode, **{field: edge}))


@pytest.mark.parametrize("module", [PeakFrequencyAlgorithm(), BandRatioAlgorithm()])
def test_config_and_catalog_have_identical_domain(module):
    schema = module.config_model.model_json_schema()["properties"]
    for parameter in module.parameter_schema().parameters:
        if parameter.key.endswith("_hz"):
            assert parameter.minimum == schema[parameter.key]["minimum"] == 1
            assert parameter.maximum == schema[parameter.key]["maximum"] == 30


def test_peak_snapshot_does_not_treat_search_band_as_filter():
    module = PeakFrequencyAlgorithm()
    parsed = module.config_model.model_validate(config(module))
    snapshot = module.execution_snapshot(parsed)
    assert snapshot.filters["bandpass_hz"] == [1, 30]
    assert snapshot.filters["notch_hz"] is None
    assert snapshot.window["frequency_band_hz"] == [8, 13]


@pytest.mark.parametrize("module", [PsdAlgorithm(), StftAlgorithm()])
@pytest.mark.parametrize("mode", ["static", "dynamic"])
@pytest.mark.parametrize("notch", [None, 50, 60])
@pytest.mark.parametrize("high", [30, 50])
def test_snapshot_preserves_explicit_notch_independently_of_high_cut(module, mode, notch, high):
    parsed = module.config_model.model_validate(config(module, mode, low_hz=1, high_hz=high, notch_hz=notch))
    filters = module.execution_snapshot(parsed).filters
    assert filters["bandpass_hz"] == [1, high]
    assert filters["notch_hz"] == notch
    assert filters["notch_quality_factor"] == (None if notch is None else 30)


@pytest.mark.parametrize("mode", ["static", "dynamic"])
@pytest.mark.parametrize("algorithm", ["peak_frequency", "band_ratio"])
def test_queue_rejects_unsupported_bands_before_loading_or_persisting(tmp_path, algorithm, mode):
    service, recording_id = _service(tmp_path, sfreq_hz=250)
    queue = PersistentRunQueue(service.recordings, service.recordings.database_path, tmp_path / "queued")
    if algorithm == "peak_frequency":
        extra = dict(low_hz=25, high_hz=40)
    else:
        extra = dict(numerator_low_hz=4, numerator_high_hz=8, denominator_low_hz=30, denominator_high_hz=50)
    payload = dict(algorithm_id=algorithm, channel="F3", mode=mode,
                   time=dict(start_s=0, end_s=10), dynamic_window_s=5, refresh_step_s=1, **extra)
    with pytest.raises(ValueError):
        queue.enqueue(RunCreateRequest(recording_id=recording_id, analysis_type="official_algorithm", config=payload))
    assert service.recordings.load_data_calls == 0
    assert queue.list() == []


@pytest.mark.parametrize("algorithm", ["psd", "stft", "peak_frequency"])
@pytest.mark.parametrize("mode", ["static", "dynamic"])
def test_queued_and_completed_filters_match_actual_contract(tmp_path, algorithm, mode):
    service, recording_id = _service(tmp_path, sfreq_hz=250)
    queue = PersistentRunQueue(service.recordings, service.recordings.database_path, tmp_path / "queued")
    payload = dict(algorithm_id=algorithm, channel="F3", mode=mode,
                   time=dict(start_s=0, end_s=10), dynamic_window_s=5, refresh_step_s=1)
    if algorithm == "peak_frequency":
        payload.update(low_hz=8, high_hz=13)
        expected_band, expected_notch = [1, 30], None
    else:
        payload.update(low_hz=1, high_hz=50, notch_hz=60)
        expected_band, expected_notch = [1, 50], 60
    queued = queue.enqueue(RunCreateRequest(recording_id=recording_id, analysis_type="official_algorithm", config=payload))
    assert queued.filters["bandpass_hz"] == expected_band
    assert queued.filters["notch_hz"] == expected_notch
    final = queue.process_next()
    assert final.status is RunStatus.COMPLETED
    assert final.filters == queued.filters
    assert queue.get(final.run_id).filters == queued.filters


def test_peak_partial_axis_cannot_report_a_clean_result():
    spectrum = SimpleNamespace(freqs=np.array([8., 9., 10.]), psd=np.array([[1., 4., 2.]]), evidence={})
    result = compute_peak_frequency(spectrum, channel="F3", low_hz=8, high_hz=13,
                                    requested_range={"start_s": 0, "end_s": 10})
    assert result.value is None
    assert result.quality == "unavailable"
    assert result.failure.code == "PEAK_FREQUENCY_BAND_NOT_COVERED"
