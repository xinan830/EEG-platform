import numpy as np
from copy import deepcopy
from types import SimpleNamespace

import pytest

from app.services.topomap import TopomapService
from app.services.result_scalar_inputs import RbpResultScalarAdapter
from test_topomap import _dynamic_window_payload


def test_rbp_amplitude_projection_uses_absolute_power_and_keeps_invalid_values_unavailable():
    powers = [0.0, 4.0, -1.0, None, 9.0]
    result = RbpResultScalarAdapter().build_input(
        recording_id="recording-1",
        payload={
            "identity": {"scientific_version": "official-rbp-v4", "implementation_identity": "build"},
            "result": {
                "arrays": {"alpha_absolute_power": powers},
                "array_units": {"alpha_absolute_power": "uV^2"},
                "evidence": {"extensions": {"channel_order": {
                    "channels": ["Fz", "Cz", "Pz", "O1", "O2"]}}},
            },
        },
        window_start_s=5.0, window_end_s=10.0,
        field_ids=["rbp.amplitude.alpha"],
    )

    values = result.values_by_band["rbp.amplitude.alpha"]
    assert values[:2] == [0.0, 2.0]
    assert np.isnan(values[2:4]).all()
    assert values[4] == 3.0
    assert result.units_by_band == {"rbp.amplitude.alpha": "uV"}
    assert result.provenance_by_field["rbp.amplitude.alpha"]["projection_identity"] == \
        "rbp-band-rms-amplitude-v1"
    assert powers == [0.0, 4.0, -1.0, None, 9.0]


def test_amplitude_http_projects_saved_result_with_quality_and_no_raw_reader():
    from fastapi import FastAPI
    from fastapi.testclient import TestClient
    from app.api.topomap import dynamic_router

    channels = ["Fz", "Cz", "Pz", "Fz-Cz"]
    payload = _dynamic_window_payload(channels, absolute=True)
    for name in payload["result"]["arrays"]:
        payload["result"]["arrays"][name] = [4.0, 9.0, -1.0, 16.0]
    payload["result"]["evidence"]["extensions"]["channel_quality"] = {"by_channel": {
        channel: {"status": "unavailable" if channel == "Cz" else "clean",
                  "reasons": ["amplitude_threshold"] if channel == "Cz" else []}
        for channel in channels
    }}
    before = deepcopy(payload)
    reads = []
    def get(window_id, **_kwargs):
        reads.append(window_id)
        return payload
    app = FastAPI()
    app.state.topomap_service = TopomapService(None, None, SimpleNamespace(get=get))
    app.include_router(dynamic_router)
    with TestClient(app) as client:
        response = client.get("/api/analysis-windows/window-1/topomap",
                              params={"field_ids": "rbp.amplitude.alpha"})
    assert response.status_code == 200
    result = response.json()
    field, = result["fields"]
    assert result["run_id"] == "window-1"
    assert (result["window_start_s"], result["window_end_s"]) == (10, 15)
    assert field["unit"] == "uV"
    assert field["provenance"]["projection_identity"] == "rbp-band-rms-amplitude-v1"
    assert [point["value"] for point in field["points"]] == [2.0, 3.0, None, 4.0]
    assert field["points"][1]["quality_status"] == "unavailable"
    assert field["points"][1]["quality_reasons"] == ["amplitude_threshold"]
    assert field["points"][3]["status"] == "excluded"
    assert reads == ["window-1"]
    assert payload == before


@pytest.mark.parametrize("bad", ["missing", "unit", "dimension"])
def test_missing_power_is_field_unavailable_without_hiding_other_bands(bad):
    payload = _dynamic_window_payload(["Fz", "Cz", "Pz"], absolute=True)
    if bad == "missing":
        del payload["result"]["arrays"]["alpha_absolute_power"]
    elif bad == "unit":
        payload["result"]["array_units"]["alpha_absolute_power"] = "V^2"
    else:
        payload["result"]["arrays"]["alpha_absolute_power"] = [1.0]
    service = TopomapService(None, None, SimpleNamespace(get=lambda *_args, **_kwargs: payload))

    result = service.build_dynamic_window("window-1", field_ids=[
        "rbp.amplitude.alpha", "rbp.amplitude.beta"])

    alpha = next(field for field in result.fields if field.field_id.endswith(".alpha"))
    beta = next(field for field in result.fields if field.field_id.endswith(".beta"))
    assert alpha.status == "unavailable"
    assert alpha.failure["code"] == "ABSOLUTE_POWER_NOT_AVAILABLE"
    assert alpha.unit == "uV"
    assert alpha.points == []
    assert [point.value for point in beta.points] == pytest.approx([1.0, np.sqrt(2), np.sqrt(3)])
