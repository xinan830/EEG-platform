from copy import deepcopy
from types import SimpleNamespace
import json

import numpy as np
import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

from app.api.result_scalars import router
from app.services.result_scalars import ResultScalarsService
from app.services.topomap import TopomapService
from test_psd_topomap_density import _payload
from test_topomap import _bind_dynamic_source_context, _dynamic_window_payload, _set_dynamic_configuration

BANDS = ("delta", "theta", "alpha", "beta", "gamma")


@pytest.mark.parametrize("algorithm", ["psd", "rbp"])
def test_scalar_http_never_enters_spatial_preparation_or_interpolation(monkeypatch, algorithm):
    payload = (_payload(np.linspace(1, 50, 197), np.full((3, 197), 2e-12))
               if algorithm == "psd" else _dynamic_window_payload(["Fz", "Cz", "Pz"], absolute=True))
    _bind_dynamic_source_context(payload, ["Fz", "Cz", "Pz"])
    payload["result"]["evidence"]["extensions"]["channel_quality"] = {"by_channel": {
        channel: {"status": "clean", "reasons": []} for channel in ["Fz", "Cz", "Pz"]}}
    prefix = "psd.band-mean-density." if algorithm == "psd" else "rbp.amplitude."
    fields = [prefix + band for band in BANDS]
    window_service = SimpleNamespace(get=lambda *_args, **_kwargs: payload)
    mapped = TopomapService(None, None, window_service).build_dynamic_window("window-1", field_ids=fields)
    before = deepcopy(payload)

    def forbidden(*_args, **_kwargs):
        pytest.fail("Numeric reads must not enter spatial preparation or interpolation")

    monkeypatch.setattr(TopomapService, "_interpolate", forbidden)
    monkeypatch.setattr("app.services.topomap.resolve_dynamic_signal_provenance", forbidden)
    app = FastAPI()
    app.state.result_scalars_service = ResultScalarsService(window_service)
    app.include_router(router)
    with TestClient(app) as client:
        response = client.get("/api/analysis-windows/window-1/scalar-values",
                              params=[("field_ids", field) for field in fields])
    assert response.status_code == 200, response.text
    result = response.json()
    assert result["window_id"] == "window-1"
    for scalar, spatial in zip(result["fields"], mapped.fields, strict=True):
        assert scalar["unit"] == spatial.unit
        assert all(
            scalar["provenance"][key] == spatial.provenance[key] for key in scalar["provenance"])
        assert [point["value"] for point in scalar["points"]] == [point.value for point in spatial.points]
        assert "grid" not in scalar
        assert all(set(point) == {"channel", "value", "quality_status", "quality_reasons"} for point in scalar["points"])
    assert payload == before


def test_scalar_values_need_quality_and_power_but_do_not_need_electrode_coordinates():
    channels = ["Fz-Cz", "Missing", "Pz"]
    payload = _dynamic_window_payload(channels, absolute=True)
    configuration = json.loads(payload["scientific_configuration_json"])
    del configuration["context"]["signal_mapping"]["signal_source"]["map_positions"]
    _set_dynamic_configuration(payload, configuration)
    payload["result"]["arrays"]["alpha_absolute_power"] = [4, 9, -1]
    payload["result"]["evidence"]["extensions"]["channel_quality"] = {"by_channel": {
        channels[0]: {"status": "clean", "reasons": []},
        channels[1]: {"status": "unavailable", "reasons": ["gap"]},
        channels[2]: {"status": "clean", "reasons": []},
    }}
    service = ResultScalarsService(SimpleNamespace(get=lambda *_args, **_kwargs: payload))
    field, = service.read_window("window", field_ids=["rbp.amplitude.alpha"]).fields
    assert [point.channel for point in field.points] == channels
    assert [point.value for point in field.points] == [2, None, None]
    assert field.points[1].quality_reasons == ["gap"]
    assert "SCALAR_VALUE_UNAVAILABLE" in field.points[2].quality_reasons


def test_scalar_missing_window_invalid_fields_and_missing_power_are_explicit():
    payload = _dynamic_window_payload(["Fz", "Cz", "Pz"], absolute=True)
    del payload["result"]["arrays"]["alpha_absolute_power"]
    def get(window_id, **_kwargs):
        if window_id == "missing":
            raise KeyError(window_id)
        return payload
    app = FastAPI()
    app.state.result_scalars_service = ResultScalarsService(SimpleNamespace(get=get))
    app.include_router(router)
    with TestClient(app) as client:
        assert client.get("/api/analysis-windows/missing/scalar-values",
                          params={"field_ids": "rbp.amplitude.alpha"}).status_code == 404
        assert client.get("/api/analysis-windows/window/scalar-values",
                          params={"field_ids": "rbp.invalid.alpha"}).status_code == 422
        field, = client.get("/api/analysis-windows/window/scalar-values",
                            params={"field_ids": "rbp.amplitude.alpha"}).json()["fields"]
    assert field["failure"]["code"] == "ABSOLUTE_POWER_NOT_AVAILABLE"
    assert field["status"] == "unavailable"
    assert all(point["value"] is None for point in field["points"])


@pytest.mark.parametrize("quality", ["unknown", "unavailable"])
def test_unknown_quality_and_pending_windows_do_not_expose_valid_values(quality):
    payload = _dynamic_window_payload(["Fz", "Cz", "Pz"], absolute=True)
    payload["result"]["evidence"]["extensions"]["channel_quality"] = {"by_channel": {
        channel: {"status": quality, "reasons": ["source_quality"]} for channel in ["Fz", "Cz", "Pz"]}}
    service = ResultScalarsService(SimpleNamespace(get=lambda *_args, **_kwargs: payload))
    result = service.read_window("window", field_ids=["rbp.amplitude.alpha"])
    assert result.status == "unavailable"
    assert all(point.value is None and point.quality_status == quality for point in result.fields[0].points)
    payload["state"] = "running"
    result = service.read_window("window", field_ids=["rbp.amplitude.alpha"])
    assert result.status == "unavailable"
    assert result.fields == []
    assert result.failure["code"] == "SCALAR_WINDOW_UNAVAILABLE"
