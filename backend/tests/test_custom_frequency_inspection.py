import numpy as np
import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

from app.api.custom_frequency_inspection import router
from app.models.custom_frequency_inspection import (
    CustomFrequencyRangeRequest,
    CustomFrequencyWindowPair,
)
from app.services.custom_frequency_inspection import CustomFrequencyInspectionService
from app.services.result_scalar_inputs import ResultProjectionUnavailable


FREQUENCIES = [1.0, 2.0, 4.0, 10.0, 30.0, 50.0]
CHANNELS = ["Fz-AVG", "Pz-AVG"]


def _quality(channels, status="clean"):
    return {"extensions": {"channel_quality": {"by_channel": {
        channel: {"status": status, "reasons": [] if status == "clean" else ["gap"]}
        for channel in channels
    }}}}


def _payload(algorithm, window_id):
    identity = {
        "algorithm_id": algorithm, "recording_id": "recording", "source_sha256": "source",
        "start_sample": 2000, "end_sample": 4000, "scientific_version": f"{algorithm}-v1",
        "implementation_identity": f"{algorithm}-impl-v1", "configuration_sha256": f"{algorithm}-config",
        "window_policy_version": f"{algorithm}-window-v1",
    }
    context = {
        "sampling_rate_hz": 200.0,
        "signal_mapping": {"channels": CHANNELS},
        "reference": "original_recording_no_software_rereference",
        "filters": {"bandpass_hz": [1.0, 50.0], "notch_hz": 50.0},
        "window": {"welch_segment_s": 4.0, "welch_overlap": 0.5,
                   "dynamic_window_s": 10.0, "refresh_step_s": 2.0, "alignment": "window_end"},
    }
    if algorithm == "psd":
        result = {
            "axes": {"frequency_hz": FREQUENCIES}, "axis_units": {"frequency_hz": "Hz"},
            "array_units": {"psd": "V^2/Hz"},
            "arrays": {"psd": np.full((2, len(FREQUENCIES)), 2e-12).tolist()},
            "evidence": _quality(CHANNELS),
        }
    else:
        arrays = {f"{band}_absolute_power": [30.0, 30.0]
                  for band in ("delta", "theta", "alpha", "beta", "gamma")}
        arrays.update({band: [0.2, 0.2] for band in ("delta", "theta", "alpha", "beta", "gamma")})
        arrays["spectral_psd"] = np.full((2, len(FREQUENCIES)), 4e-12).tolist()
        result = {
            "axes": {"frequency_hz": FREQUENCIES}, "axis_units": {"frequency_hz": "Hz"}, "arrays": arrays,
            "array_units": {"spectral_psd": "V^2/Hz", **{
                f"{band}_absolute_power": "uV^2" for band in ("delta", "theta", "alpha", "beta", "gamma")}},
            "evidence": {**_quality(CHANNELS), "extensions": {
                **_quality(CHANNELS)["extensions"], "channel_order": {"channels": CHANNELS},
            }},
        }
    return {
        "window_id": window_id, "state": "ready", "identity": identity,
        "scientific_configuration_json": __import__("json").dumps({"context": context}),
        "result": result,
    }


def _service(psd=None, rbp=None):
    payloads = {"psd-window": psd or _payload("psd", "psd-window"),
                "rbp-window": rbp or _payload("rbp", "rbp-window")}
    service = CustomFrequencyInspectionService(type("Windows", (), {
        "get": lambda _, window_id, **__: payloads[window_id],
    })())
    return service


def test_bin_projection_uses_actual_midpoint_bounds_and_keeps_channel_order():
    result = _service().read_bins(CustomFrequencyWindowPair(
        psd_window_id="psd-window", rbp_window_id="rbp-window"))

    assert result.frequency_hz == FREQUENCIES
    assert [(item.low_hz, item.high_hz) for item in result.bin_intervals] == [
        (1.0, 1.5), (1.5, 3.0), (3.0, 7.0), (7.0, 20.0), (20.0, 40.0), (40.0, 50.0),
    ]
    assert [row.channel for row in result.channels] == CHANNELS
    assert result.channels[0].psd_density_uv2_per_hz == pytest.approx([2.0] * 6)
    assert result.channels[0].rbp_denominator_uv2 == pytest.approx(150.0)
    assert result.channels[0].rbp_absolute_power_uv2 == pytest.approx([2.0, 6.0, 16.0, 52.0, 80.0, 40.0])
    assert result.channels[0].amplitude_rms_uv == pytest.approx(np.sqrt([2, 6, 16, 52, 80, 40]))
    assert result.channels[0].rbp_relative_power_percent == pytest.approx(np.array([2, 6, 16, 52, 80, 40]) / 150 * 100)


def test_range_projection_integrates_each_source_and_uses_full_rbp_denominator():
    result = _service().read_range(CustomFrequencyRangeRequest(
        psd_window_id="psd-window", rbp_window_id="rbp-window", low_hz=2.0, high_hz=10.0))

    assert result.channels[0].psd_density_uv2_per_hz == pytest.approx(2.0)
    assert result.channels[0].rbp_absolute_power_uv2 == pytest.approx(32.0)
    assert result.channels[0].rbp_relative_power_percent == pytest.approx(32 / 150 * 100)
    assert result.channels[0].amplitude_rms_uv == pytest.approx(np.sqrt(32.0))
    assert result.channels[0].rbp_denominator_uv2 == pytest.approx(150.0)


def test_batched_bins_match_scalar_exact_edge_projection_for_varying_spectra():
    from app.algorithms.rbp.custom_frequency import project_bins, project_interval
    frequencies = np.array(FREQUENCIES)
    spectra = np.array([[1, 4, 2, 7, 3, 9], [2, 1, 6, 3, 8, 4], [1, 1, 1, 1, 1, 1]]) * 1e-12
    denominators = [150., 200., None]
    bounds = [(1, 1.5), (1.5, 3), (3, 7), (7, 20), (20, 40), (40, 50)]
    actual = project_bins(frequencies, spectra, denominators, bounds)
    for row, denominator in enumerate(denominators):
        for column, (low, high) in enumerate(bounds):
            expected = project_interval(frequencies, spectra[row], denominator, low, high)
            for values, value in zip(actual, expected):
                if value is None:
                    assert np.isnan(values[row, column])
                else:
                    assert values[row, column] == pytest.approx(value, rel=1e-13)


@pytest.mark.parametrize("low,high", [(0.9, 2), (2, 50.1), (3, 2), (2, 2), (float("nan"), 3)])
def test_range_request_rejects_nonfinite_reversed_zero_width_or_out_of_domain(low, high):
    with pytest.raises(ValueError):
        CustomFrequencyRangeRequest(
            psd_window_id="psd-window", rbp_window_id="rbp-window", low_hz=low, high_hz=high)


def test_projection_rejects_mismatched_window_or_analysis_settings():
    rbp = _payload("rbp", "rbp-window")
    rbp["identity"]["start_sample"] += 1
    with pytest.raises(ResultProjectionUnavailable, match="窗口不匹配"):
        _service(rbp=rbp).read_bins(CustomFrequencyWindowPair(
            psd_window_id="psd-window", rbp_window_id="rbp-window"))

    rbp = _payload("rbp", "rbp-window")
    rbp["scientific_configuration_json"] = rbp["scientific_configuration_json"].replace("200.0", "250.0")
    with pytest.raises(ResultProjectionUnavailable, match="sampling_rate_hz"):
        _service(rbp=rbp).read_bins(CustomFrequencyWindowPair(
            psd_window_id="psd-window", rbp_window_id="rbp-window"))


def test_unavailable_channel_is_null_without_reordering_other_rows():
    rbp = _payload("rbp", "rbp-window")
    rbp["result"]["evidence"]["extensions"]["channel_quality"]["by_channel"]["Pz-AVG"] = {
        "status": "unavailable", "reasons": ["missing_samples"],
    }
    result = _service(rbp=rbp).read_bins(CustomFrequencyWindowPair(
        psd_window_id="psd-window", rbp_window_id="rbp-window"))

    assert result.channels[0].rbp_absolute_power_uv2[0] == pytest.approx(2.0)
    assert result.channels[1].rbp_absolute_power_uv2 == [None] * len(FREQUENCIES)
    assert result.channels[1].rbp_quality_reasons == ["missing_samples"]


def test_http_contract_returns_typed_bin_projection():
    app = FastAPI()
    app.state.custom_frequency_inspection_service = _service()
    app.include_router(router)

    with TestClient(app) as client:
        response = client.post("/api/analysis-windows/custom-frequency/bins", json={
            "psd_window_id": "psd-window", "rbp_window_id": "rbp-window",
        })

    assert response.status_code == 200
    body = response.json()
    assert body["mode"] == "bins"
    assert body["channels"][0]["rbp_absolute_power_uv2"][0] == pytest.approx(2.0)


@pytest.mark.parametrize("damage", ["unit", "source", "axis", "channels", "missing_context"])
def test_invalid_source_contract_is_rejected(damage):
    import json
    rbp = _payload("rbp", "rbp-window")
    if damage == "unit":
        rbp["result"]["array_units"]["spectral_psd"] = "uV^2/Hz"
    elif damage == "source":
        del rbp["identity"]["source_sha256"]
    elif damage == "axis":
        rbp["result"]["axes"]["frequency_hz"] = list(reversed(FREQUENCIES))
    elif damage == "channels":
        rbp["result"]["evidence"]["extensions"]["channel_order"]["channels"] = list(reversed(CHANNELS))
    else:
        value = json.loads(rbp["scientific_configuration_json"])
        del value["context"]["window"]["welch_overlap"]
        rbp["scientific_configuration_json"] = json.dumps(value)
    with pytest.raises(ResultProjectionUnavailable) as error:
        _service(rbp=rbp).read_bins(CustomFrequencyWindowPair(
            psd_window_id="psd-window", rbp_window_id="rbp-window"))
    assert error.value.code == "CUSTOM_FREQUENCY_SOURCE_MISMATCH"


def test_source_quality_remains_independent_and_invalid_power_exposes_failure_reason():
    psd = _payload("psd", "psd-window")
    psd["result"]["evidence"]["extensions"]["channel_quality"]["by_channel"][CHANNELS[0]] = {
        "status": "unavailable", "reasons": ["missing_samples"]}
    rbp = _payload("rbp", "rbp-window")
    rbp["result"]["evidence"]["extensions"]["channel_failures"] = {
        "by_channel": {CHANNELS[1]: {"code": "RBP_CHANNEL_UNAVAILABLE", "message": "invalid total"}}}
    result = _service(psd, rbp).read_range(CustomFrequencyRangeRequest(
        psd_window_id="psd-window", rbp_window_id="rbp-window", low_hz=1, high_hz=3))
    assert result.channels[0].psd_density_uv2_per_hz is None
    assert result.channels[0].rbp_absolute_power_uv2 == pytest.approx(8)
    assert result.channels[1].psd_density_uv2_per_hz == pytest.approx(2)
    assert result.channels[1].rbp_absolute_power_uv2 is None
    assert result.channels[1].rbp_quality_reasons == ["RBP_CHANNEL_UNAVAILABLE"]


def test_selectable_domain_is_intersection_while_denominator_keeps_full_saved_rbp_total():
    psd = _payload("psd", "psd-window")
    psd["result"]["axes"]["frequency_hz"] = FREQUENCIES[1:-1]
    psd["result"]["arrays"]["psd"] = np.full((2, 4), 2e-12).tolist()
    service = _service(psd=psd)
    pair = CustomFrequencyWindowPair(psd_window_id="psd-window", rbp_window_id="rbp-window")
    bins = service.read_bins(pair)
    assert bins.frequency_hz == [2, 4, 10, 30]
    assert bins.bin_intervals[0].low_hz == 2
    assert bins.bin_intervals[-1].high_hz == 30
    result = service.read_range(CustomFrequencyRangeRequest(**pair.model_dump(), low_hz=2, high_hz=3))
    assert result.channels[0].rbp_absolute_power_uv2 == pytest.approx(4)
    assert result.channels[0].rbp_denominator_uv2 == 150
    with pytest.raises(ResultProjectionUnavailable) as error:
        service.read_range(CustomFrequencyRangeRequest(**pair.model_dump(), low_hz=1, high_hz=3))
    assert error.value.code == "CUSTOM_FREQUENCY_RANGE_UNSUPPORTED"


def test_http_failure_uses_stable_error_and_request_id():
    from app.core.api_contract import RequestContextMiddleware, validation_exception_handler
    from fastapi.exceptions import RequestValidationError
    app = FastAPI()
    app.add_middleware(RequestContextMiddleware)
    app.add_exception_handler(RequestValidationError, validation_exception_handler)
    app.state.custom_frequency_inspection_service = _service()
    app.include_router(router)
    with TestClient(app) as client:
        missing = client.post("/api/analysis-windows/custom-frequency/bins", json={
            "psd_window_id": "missing", "rbp_window_id": "rbp-window"})
        assert missing.status_code == 404
        assert missing.json()["code"] == "WINDOW_NOT_FOUND"
        assert missing.json()["request_id"]
        invalid = client.post("/api/analysis-windows/custom-frequency/range", json={
            "psd_window_id": "psd-window", "rbp_window_id": "rbp-window", "low_hz": 3, "high_hz": 1})
        assert invalid.status_code == 422
        assert invalid.json()["request_id"]
