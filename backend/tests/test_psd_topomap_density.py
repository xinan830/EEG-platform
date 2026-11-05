"""Numeric and HTTP regressions for band-mean PSD, distinct from band power."""

from copy import deepcopy
from types import SimpleNamespace

import numpy as np
import pytest

from app.services.topomap import PsdTopomapAdapter, TopomapService, TopomapUnavailable

BANDS = [("delta", 1.0, 4.0), ("theta", 4.0, 8.0), ("alpha", 8.0, 13.0),
         ("beta", 13.0, 30.0), ("gamma", 30.0, 50.0)]


def _payload(frequencies, rows):
    channels = ["Fz", "Cz", "Pz"][:len(rows)]
    return {
        "identity": {"algorithm_id": "psd", "recording_id": "recording-1",
                     "scientific_version": "psd-test", "implementation_identity": "psd-build"},
        "state": "ready", "plan": {"start_s": 10.0, "end_s": 20.0},
        "channel_order": channels,
        "result": {
            "axes": {"frequency_hz": list(frequencies)}, "axis_units": {"frequency_hz": "Hz"},
            "arrays": {"psd": [list(row) for row in rows]}, "array_units": {"psd": "V^2/Hz"},
            "quality": "clean", "provenance": {"source_sha256": "source-hash"},
            "evidence": {"extensions": {"channel_quality": {"by_channel": {
                channel: {"status": "clean", "reasons": []} for channel in channels
            }}}},
        },
    }


def _project(payload, field_id):
    field_ids = field_id if isinstance(field_id, list) else [field_id]
    return PsdTopomapAdapter().build_input(
        recording_id="recording-1", payload=payload,
        window_start_s=10.0, window_end_s=20.0, field_ids=field_ids)


@pytest.mark.parametrize("band,low,high", BANDS)
def test_constant_density_is_independent_of_bandwidth(band, low, high):
    frequencies = np.array([1.0, 3.1, 7.3, 11.5, 22.0, 39.0, 50.0])
    payload = _payload(frequencies, [np.full(len(frequencies), 2e-12)])
    before = deepcopy(payload)

    result = _project(payload, f"psd.band-mean-density.{band}")

    assert result.values_by_band[f"psd.band-mean-density.{band}"] == pytest.approx([2e-12], abs=1e-25)
    assert result.units_by_band[f"psd.band-mean-density.{band}"] == "V^2/Hz"
    assert result.provenance_by_field[f"psd.band-mean-density.{band}"]["bandwidth_hz"] == high - low
    assert payload == before


@pytest.mark.parametrize("band,low,high", BANDS)
def test_nonuniform_grid_uses_exact_edges_and_full_bandwidth(band, low, high):
    frequencies = np.array([1.0, 2.5, 6.3, 9.7, 14.0, 27.0, 38.0, 50.0])
    density = (frequencies + 2) * 1e-12
    result = _project(_payload(frequencies, [density, 2 * density]), f"psd.band-mean-density.{band}")

    expected = ((low + high) / 2 + 2) * 1e-12
    assert result.values_by_band[f"psd.band-mean-density.{band}"] == pytest.approx(
        [expected, 2 * expected], abs=1e-25)
    assert result.channel_order == ["Fz", "Cz"]


def test_all_band_fields_keep_independent_frequency_and_bandwidth_provenance():
    frequencies = np.arange(1.0, 51.0)
    density = frequencies + 2
    field_ids = [f"psd.band-mean-density.{band}" for band, _, _ in BANDS]

    result = _project(_payload(frequencies, [density, 2 * density]), field_ids)

    assert list(result.values_by_band) == field_ids
    for (band, low, high), field_id in zip(BANDS, field_ids, strict=True):
        assert result.metadata_by_field[field_id]["frequency_range_hz"] == (low, high)
        assert result.provenance_by_field[field_id]["bandwidth_hz"] == high - low
        assert result.values_by_band[field_id] == pytest.approx(
            [(low + high) / 2 + 2, 2 * ((low + high) / 2 + 2)])
    assert "bandwidth_hz" not in result.provenance


def test_psd_adapter_rejects_duplicate_or_mixed_field_kinds():
    payload = _payload([1, 2, 50], [[1e-12, 2e-12, 3e-12]])
    with pytest.raises(TopomapUnavailable) as duplicate:
        _project(payload, ["psd.band-mean-density.alpha"] * 2)
    assert duplicate.value.code == "TOPOMAP_FIELD_ID_INVALID"

    with pytest.raises(TopomapUnavailable) as mixed:
        _project(payload, ["psd.band-mean-density.alpha", "psd.frequency-bin.1"])
    assert mixed.value.code == "TOPOMAP_FIELD_ID_INVALID"

    with pytest.raises(TopomapUnavailable) as multiple_bins:
        _project(payload, ["psd.frequency-bin.1", "psd.frequency-bin.2"])
    assert multiple_bins.value.code == "TOPOMAP_FIELD_ID_INVALID"


@pytest.mark.parametrize("field_id", ["psd.band-power.alpha", "psd.band-mean-density.ultra",
                                         "rbp.absolute.alpha"])
def test_wrong_metric_and_unknown_band_are_rejected(field_id):
    with pytest.raises(TopomapUnavailable, match="字段 ID 无效") as error:
        _project(_payload([1, 50], [[1e-12, 1e-12]]), field_id)
    assert error.value.code == "TOPOMAP_FIELD_ID_INVALID"


def test_negative_psd_density_is_rejected_instead_of_becoming_a_topomap_value():
    payload = _payload([1, 50], [[1e-12, -1e-12]])

    with pytest.raises(TopomapUnavailable, match="PSD 密度不得为负值") as error:
        _project(payload, "psd.band-mean-density.alpha")

    assert error.value.code == "TOPOMAP_PSD_CONTRACT_INVALID"


def test_dynamic_http_response_preserves_density_units_range_and_source_without_rereading_samples():
    from fastapi import FastAPI
    from fastapi.testclient import TestClient
    from app.api.topomap import dynamic_router
    from test_topomap import _bind_dynamic_source_context

    payload = _payload([1, 50], [[2e-12, 2e-12], [4e-12, 4e-12], [6e-12, 6e-12]])
    _bind_dynamic_source_context(payload, payload["channel_order"])
    before = deepcopy(payload)
    service = TopomapService(None, None, SimpleNamespace(get=lambda *_args, **_kwargs: payload))
    app = FastAPI()
    app.state.topomap_service = service
    app.include_router(dynamic_router)

    field_ids = [f"psd.band-mean-density.{band}" for band, _, _ in BANDS]
    with TestClient(app) as client:
        response = client.get("/api/analysis-windows/window-1/topomap",
                              params=[("field_ids", field_id) for field_id in field_ids])
        obsolete = client.get("/api/analysis-windows/window-1/topomap",
                              params={"field_ids": "psd.band-power.alpha"})

    assert response.status_code == 200
    result = response.json()
    fields = result["fields"]
    assert result["status"] == "ready"
    assert (result["window_start_s"], result["window_end_s"]) == (10, 20)
    assert [field["field_id"] for field in fields] == field_ids
    for field, (band, low, high) in zip(fields, BANDS, strict=True):
        assert field["frequency_range_hz"] == [low, high]
        assert field["frequency_hz"] is None
        assert field["unit"] == "V^2/Hz"
        assert field["label"] == f"{band.title()} 平均 PSD ({low:g}–{high:g} Hz)"
        assert [point["value"] for point in field["points"]] == pytest.approx([2e-12, 4e-12, 6e-12], abs=1e-25)
        assert field["provenance"]["bandwidth_hz"] == high - low
        assert field["provenance"]["projection_identity"] == "psd-band-mean-density-v1"
        assert field["provenance"]["source_sha256"] == "source-hash"
    assert result["provenance"]["power_type"] == "psd_band_mean_density"
    assert obsolete.status_code == 422
    assert payload == before


def test_dynamic_all_band_http_response_keeps_available_fields_when_gamma_is_out_of_range():
    from fastapi import FastAPI
    from fastapi.testclient import TestClient
    from app.api.topomap import dynamic_router
    from test_topomap import _bind_dynamic_source_context

    payload = _payload([1.0, 15.5, 30.0, 45.0, 49.0], [
        [2e-12] * 5, [4e-12] * 5, [6e-12] * 5,
    ])
    _bind_dynamic_source_context(payload, payload["channel_order"])
    service = TopomapService(None, None, SimpleNamespace(get=lambda *_args, **_kwargs: payload))
    app = FastAPI()
    app.state.topomap_service = service
    app.include_router(dynamic_router)
    field_ids = [f"psd.band-mean-density.{band}" for band, _, _ in BANDS]

    with TestClient(app) as client:
        response = client.get("/api/analysis-windows/window-1/topomap",
                              params=[("field_ids", field_id) for field_id in field_ids])

    assert response.status_code == 200
    result = response.json()
    assert [field["field_id"] for field in result["fields"]] == field_ids
    assert [field["status"] for field in result["fields"]] == ["ready"] * 4 + ["unavailable"]
    gamma = result["fields"][-1]
    assert gamma["frequency_range_hz"] == [30.0, 50.0]
    assert gamma["failure"]["code"] == "TOPOMAP_BAND_RANGE_UNAVAILABLE"
    assert gamma["points"] == []


@pytest.mark.parametrize("bad_kind", ["non_finite", "quality_unavailable"])
def test_invalid_channel_is_excluded_without_synthetic_zero(bad_kind):
    from test_topomap import _bind_dynamic_source_context

    payload = _payload([1, 50], [[2e-12, 2e-12], [4e-12, 4e-12], [6e-12, 6e-12]])
    _bind_dynamic_source_context(payload, payload["channel_order"])
    if bad_kind == "non_finite":
        payload["result"]["arrays"]["psd"][0][0] = None
    else:
        payload["result"]["evidence"]["extensions"]["channel_quality"]["by_channel"]["Fz"] = {
            "status": "unavailable", "reasons": ["amplitude_threshold"]}
    service = TopomapService(None, None, SimpleNamespace(get=lambda *_args, **_kwargs: payload))
    result = service.build_dynamic_window("window-1", field_ids=["psd.band-mean-density.alpha"])
    field, = result.fields
    assert field.points[0].status == "excluded"
    if bad_kind == "non_finite":
        assert field.points[0].value is None
    else:
        assert field.points[0].value == pytest.approx(2e-12, abs=1e-25)
        assert field.points[0].reason == "amplitude_threshold"
    assert field.points[1].value == pytest.approx(4e-12, abs=1e-25)
    assert field.status == "unavailable"
