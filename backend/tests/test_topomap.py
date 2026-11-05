import hashlib
import json
from types import SimpleNamespace

import numpy as np
import pytest

from app.services.topomap import RbpTopomapAdapter, TopomapService, TopomapUnavailable


def _service(values, channels, formulas=None, status="available", arrays=None, array_units=None):
    run = SimpleNamespace(
        config={"algorithm_id": "rbp"}, recording_id="recording-1",
        result_summary={"structured": {"channel_order": channels}},
        scientific_version="official-rbp-v4", implementation_version="test-build",
    )
    preview = {"recording_id": "recording-1", "status": status, "cursor_s": 5.0,
               "scientific_version": run.scientific_version, "implementation_version": run.implementation_version,
               "window": {"start_sample": 0, "end_sample": 500,
                           "provenance": {"window_start_s": 0.0, "window_end_s": 5.0}},
               "arrays": arrays or {band: values for band in ("delta", "theta", "alpha", "beta", "gamma")},
               "array_metadata": array_units or {band: {"unit": "ratio"}
                                                  for band in ("delta", "theta", "alpha", "beta", "gamma")}}
    queue = SimpleNamespace(
        get=lambda _run_id: run,
        recordings=SimpleNamespace(
            require_recording=lambda _id: SimpleNamespace(id="recording-1", sfreq=100.0),
            select_analysis_montage=lambda recording, _montage: recording,
            analysis_signal_provenance=lambda _recording: {
                "formulas": formulas or [],
                "montage_id": "montage-1", "montage_revision": 2,
                "montage_fingerprint": "fingerprint-1", "snapshot_sha256": "snapshot-1",
                "map_positions": {
                    channel: {"x": x, "y": y, "status": "available", "enabled": True, "source": "template"}
                    for channel, (x, y) in {"Fz": (0.0, -0.8), "Cz": (0.0, 0.0), "Pz": (0.0, 0.8)}.items()
                },
            },
        ),
    )
    result_service = SimpleNamespace(window_preview=lambda *_args, **_kwargs: preview)
    return TopomapService(result_service, queue)


def _set_dynamic_configuration(payload, configuration):
    encoded = json.dumps(configuration, sort_keys=True, separators=(",", ":"))
    payload["scientific_configuration_json"] = encoded
    payload["identity"]["configuration_sha256"] = hashlib.sha256(encoded.encode("utf-8")).hexdigest()
    return payload


def _bind_dynamic_source_context(payload, channels, *, selected_montage=True):
    positions = {
        channel: {"x": x, "y": y, "status": "available", "enabled": True, "source": "snapshot"}
        for channel, (x, y) in {"Fz": (0.0, -0.8), "Cz": (0.0, 0.0), "Pz": (0.0, 0.8)}.items()
    }
    source = {
        "signal_mode": "acquisition_montage", "formulas": [], "map_positions": positions,
    }
    signal_mapping = {"channels": channels, "signal_source": source}
    if selected_montage:
        source.update({
            "analysis_reference": "analysis_configuration_snapshot", "montage_id": "montage-1",
            "montage_revision": "2", "montage_fingerprint": "source-profile-fingerprint",
            "snapshot_sha256": "fingerprint-1",
        })
        signal_mapping["analysis_montage"] = {
            "id": "montage-1", "revision": 2, "fingerprint": "fingerprint-1",
        }
    else:
        source.update({
            "analysis_reference": "acquisition_montage_snapshot", "montage_id": "saved-montage",
            "montage_revision": None, "montage_fingerprint": None,
            "snapshot_sha256": "snapshot-content-hash",
        })
    configuration = {"context": {"signal_mapping": signal_mapping}}
    return _set_dynamic_configuration(payload, configuration)


def _dynamic_window_payload(channels, *, absolute=False, selected_montage=True):
    bands = ("delta", "theta", "alpha", "beta", "gamma")
    values = {f"{band}_absolute_power" if absolute else band: [1.0, 2.0, 3.0] if absolute else [0.2, 0.3, 0.5]
              for band in bands}
    payload = {
        "identity": {"algorithm_id": "rbp", "recording_id": "recording-1",
                     "scientific_version": "official-rbp-v4", "implementation_identity": "build"},
        "state": "ready", "plan": {"start_s": 10.0, "end_s": 15.0},
        "result": {
            "arrays": values,
            "array_units": {name: "uV^2" if absolute else "ratio" for name in values},
            "evidence": {"extensions": {"channel_order": {"channels": channels}}},
        },
    }
    return _bind_dynamic_source_context(payload, channels, selected_montage=selected_montage)


def test_rbp_topomap_uses_snapshot_positions_and_returns_bounded_grid():
    field_ids = [f"rbp.relative.{band}" for band in ("delta", "theta", "alpha", "beta", "gamma")]
    result = _service([0.2, 0.5, 0.8], ["Fz", "Cz", "Pz"]).build_rbp_window("run", cursor_s=5.0, field_ids=field_ids)
    assert result.status == "ready"
    assert [field.field_id for field in result.fields] == field_ids
    assert result.window_start_s == 0.0 and result.window_end_s == 5.0
    assert result.fields[0].grid is not None and result.fields[0].grid.width == result.fields[0].grid.height == 65
    assert result.fields[0].grid.head_radius == 1.0
    assert all(item.status == "included" for item in result.fields[0].points)
    assert all(value is None or np.isfinite(value) for row in result.fields[0].grid.values for value in row)
    assert result.provenance["montage_fingerprint"] == "fingerprint-1"
    assert result.fields[0].unit == "ratio"


def test_topomap_quality_excludes_only_unavailable_channels():
    service = TopomapService(None, None)
    positions = {
        channel: {"x": x, "y": y, "status": "available", "enabled": True}
        for channel, (x, y) in {"Fz": (0.0, -0.8), "Cz": (0.0, 0.0), "Pz": (0.0, 0.8)}.items()
    }

    points, usable = service._build_points(
        ["Fz", "Cz", "Pz"], [1.0, 2.0, 3.0], {}, positions,
        {"Fz": {"status": "warning", "reasons": ["amplitude_warning"]},
         "Cz": {"status": "unavailable", "reasons": ["gap"]},
         "Pz": {"status": "clean", "reasons": []}},
    )

    assert [point.status for point in points] == ["included", "excluded", "included"]
    assert points[0].quality_status == "warning"
    assert points[1].reason == "gap"
    assert len(usable) == 2


def test_absolute_power_type_selects_structured_values_and_unit_for_same_window():
    names = ("delta", "theta", "alpha", "beta", "gamma")
    result = _service(
        [0.2, 0.3, 0.5], ["Fz", "Cz", "Pz"],
        arrays={f"{band}_absolute_power": [1.0, 2.0, 3.0] for band in names},
        array_units={f"{band}_absolute_power": {"unit": "uV^2"} for band in names},
    ).build_rbp_window("run", cursor_s=5.0,
                       field_ids=[f"rbp.absolute.{band}" for band in names])

    assert result.status == "ready"
    assert result.window_start_s == 0.0 and result.window_end_s == 5.0
    assert result.fields[2].unit == "uV^2"
    assert [point.value for point in result.fields[2].points] == [1.0, 2.0, 3.0]


def test_dynamic_window_topomap_selects_absolute_power_without_changing_window():
    channels = ["Fz", "Cz", "Pz"]
    payload = _dynamic_window_payload(channels, absolute=True)
    service = _service([0.2, 0.3, 0.5], channels)
    recordings = service.run_queue.recordings
    recordings.require_recording = lambda *_args: pytest.fail("dynamic topomap must use its window source snapshot")
    recordings.select_analysis_montage = lambda *_args: pytest.fail("dynamic topomap must not resolve montage again")
    recordings.analysis_signal_provenance = lambda *_args: pytest.fail("dynamic topomap must not resolve provenance again")
    service.dynamic_window_service = SimpleNamespace(get=lambda *_args, **_kwargs: payload)

    result = service.build_dynamic_window("window-1", field_ids=[f"rbp.absolute.{band}" for band in ("delta", "theta", "alpha", "beta", "gamma")])

    assert result.status == "ready"
    assert result.window_start_s == 10.0 and result.window_end_s == 15.0
    assert all(field.unit == "uV^2" for field in result.fields)
    assert result.provenance["montage_fingerprint"] == "source-profile-fingerprint"


def test_dynamic_window_topomap_accepts_default_acquisition_snapshot_without_duplicate_montage():
    channels = ["Fz", "Cz", "Pz"]
    payload = _dynamic_window_payload(channels, selected_montage=False)
    service = _service([0.2, 0.3, 0.5], channels)
    service.run_queue.recordings.require_recording = lambda *_args: pytest.fail("dynamic topomap must use its window source snapshot")
    service.run_queue.recordings.analysis_signal_provenance = lambda *_args: pytest.fail("dynamic topomap must use its window source snapshot")
    service.dynamic_window_service = SimpleNamespace(get=lambda *_args, **_kwargs: payload)

    result = service.build_dynamic_window("window-1", field_ids=["rbp.relative.alpha"])

    assert result.status == "ready"
    assert result.provenance["montage_id"] == "saved-montage"
    assert result.provenance["montage_revision"] is None


def test_dynamic_window_topomap_rejects_acquisition_source_without_snapshot_hash():
    channels = ["Fz", "Cz", "Pz"]
    payload = _dynamic_window_payload(channels, selected_montage=False)
    configuration = json.loads(payload["scientific_configuration_json"])
    del configuration["context"]["signal_mapping"]["signal_source"]["snapshot_sha256"]
    _set_dynamic_configuration(payload, configuration)
    service = _service([0.2, 0.3, 0.5], channels)
    service.dynamic_window_service = SimpleNamespace(get=lambda *_args, **_kwargs: payload)

    with pytest.raises(TopomapUnavailable) as exc_info:
        service.build_dynamic_window("window-1", field_ids=["rbp.relative.alpha"])

    assert exc_info.value.code == "TOPOMAP_SOURCE_CONTEXT_INVALID"


def test_dynamic_window_topomap_rejects_montage_identity_mismatch():
    channels = ["Fz", "Cz", "Pz"]
    payload = _dynamic_window_payload(channels)
    configuration = json.loads(payload["scientific_configuration_json"])
    configuration["context"]["signal_mapping"]["analysis_montage"]["fingerprint"] = "stale"
    _set_dynamic_configuration(payload, configuration)
    service = _service([0.2, 0.3, 0.5], channels)
    service.dynamic_window_service = SimpleNamespace(get=lambda *_args, **_kwargs: payload)

    with pytest.raises(TopomapUnavailable) as exc_info:
        service.build_dynamic_window("window-1", field_ids=["rbp.relative.alpha"])

    assert exc_info.value.code == "TOPOMAP_SOURCE_CONTEXT_INVALID"


def test_dynamic_window_topomap_logs_request_correlated_service_stages(caplog):
    import logging

    channels = ["Fz", "Cz", "Pz"]
    payload = _dynamic_window_payload(channels, absolute=True)
    service = _service([0.2, 0.3, 0.5], channels)
    service.dynamic_window_service = SimpleNamespace(get=lambda *_args, **_kwargs: payload)

    with caplog.at_level(logging.INFO, logger="brain_platform.topomap"):
        service.build_dynamic_window("window-1", field_ids=["rbp.absolute.alpha"], request_id="request-1")

    assert "topomap_stage_perf request_id=request-1 window_id=window-1 stage=signal_provenance" in caplog.text
    assert "topomap_stage_perf request_id=request-1 window_id=window-1 stage=dynamic_window_total" in caplog.text
    assert "payload_ms=" in caplog.text and "input_ms=" in caplog.text
    assert "context_ms=" in caplog.text and "build_ms=" in caplog.text and "total_ms=" in caplog.text


def test_old_result_absolute_request_returns_explicit_unavailable_map():
    result = _service([0.2, 0.3, 0.5], ["Fz", "Cz", "Pz"]).build_rbp_window(
        "run", cursor_s=5.0, field_ids=["rbp.absolute.alpha"])

    assert result.status == "unavailable"
    assert result.failure["code"] == "ABSOLUTE_POWER_NOT_AVAILABLE"
    assert all(field.status == "unavailable" and field.unit == "uV^2" for field in result.fields)


def test_topomap_excludes_bipolar_outputs_without_inventing_endpoint():
    service = _service([0.2, 0.5, 0.8], ["Fz-F4", "Cz", "Pz"], formulas=[
        {"name": "Fz-F4", "positive": "Fz", "negative_kind": "specifiedpair"},
    ])
    result = service.build_rbp_window("run", cursor_s=5.0, field_ids=["rbp.relative.delta"])
    assert result.status == "unavailable"
    assert result.fields[0].points[0].reason == "bipolar_or_derived_output_has_no_spatial_rule"


def test_rbp_adapter_reads_channel_order_from_window_evidence_without_recomputing():
    result = RbpTopomapAdapter().build_input(
        recording_id="recording-1",
        payload={
            "identity": {"scientific_version": "rbp-v2", "implementation_identity": "build"},
            "result": {
                "arrays": {band: [0.1, 0.2, 0.3] for band in ("delta", "theta", "alpha", "beta", "gamma")},
                "array_units": {"alpha": "ratio"},
                "evidence": {"extensions": {"channel_order": {"channels": ["Fz", "Cz", "Pz"]}}},
            },
        },
        window_start_s=5.0, window_end_s=10.0,
        field_ids=["rbp.relative.alpha"],
    )
    assert result.channel_order == ["Fz", "Cz", "Pz"]
    assert result.values_by_band["rbp.relative.alpha"] == [0.1, 0.2, 0.3]
    assert result.window_start_s == 5.0


def test_rbp_adapter_selects_declared_absolute_power_and_unit():
    bands = ("delta", "theta", "alpha", "beta", "gamma")
    payload = {
        "identity": {"scientific_version": "official-rbp-v4", "implementation_identity": "build"},
        "result": {
            "arrays": {**{band: [0.2, 0.2, 0.2] for band in bands},
                       **{f"{band}_absolute_power": [2.0, 3.0, 4.0] for band in bands}},
            "array_units": {**{band: "ratio" for band in bands},
                            **{f"{band}_absolute_power": "uV^2" for band in bands}},
            "evidence": {"extensions": {"channel_order": {"channels": ["Fz", "Cz", "Pz"]}}},
        },
    }

    result = RbpTopomapAdapter().build_input(
        recording_id="recording-1", payload=payload, window_start_s=5.0, window_end_s=10.0,
        field_ids=[f"rbp.absolute.{band}" for band in bands],
    )

    assert result.power_type == "absolute"
    assert result.units_by_band == {f"rbp.absolute.{band}": "uV^2" for band in bands}
    assert result.values_by_band["rbp.absolute.alpha"] == [2.0, 3.0, 4.0]


def test_rbp_adapter_restricts_spatial_input_to_selected_band():
    bands = ("delta", "theta", "alpha", "beta", "gamma")
    payload = {
        "identity": {"scientific_version": "official-rbp-v4", "implementation_identity": "build"},
        "result": {
            "arrays": {**{band: [0.2, 0.2, 0.2] for band in bands},
                       **{f"{band}_absolute_power": [2.0, 3.0, 4.0] for band in bands}},
            "array_units": {**{band: "ratio" for band in bands},
                            **{f"{band}_absolute_power": "uV^2" for band in bands}},
            "evidence": {"extensions": {"channel_order": {"channels": ["Fz", "Cz", "Pz"]}}},
        },
    }

    result = RbpTopomapAdapter().build_input(
        recording_id="recording-1", payload=payload, window_start_s=5.0, window_end_s=10.0,
        field_ids=["rbp.absolute.alpha"],
    )

    assert list(result.values_by_band) == ["rbp.absolute.alpha"]
    assert result.values_by_band["rbp.absolute.alpha"] == [2.0, 3.0, 4.0]
    assert result.units_by_band == {"rbp.absolute.alpha": "uV^2"}


def test_rbp_adapter_rejects_unknown_band_id():
    with pytest.raises(TopomapUnavailable) as exc_info:
        RbpTopomapAdapter().build_input(
            recording_id="recording-1", payload={}, window_start_s=5.0, window_end_s=10.0,
            field_ids=["rbp.relative.ultra"],
        )

    assert exc_info.value.code == "TOPOMAP_FIELD_ID_INVALID"


def test_run_topomap_builds_only_the_requested_scalar_field():
    bands = ("delta", "theta", "alpha", "beta", "gamma")
    arrays = {f"{band}_absolute_power": [1.0, 2.0, 3.0] for band in bands}
    units = {name: {"unit": "uV^2"} for name in arrays}
    result = _service([0.2, 0.5, 0.8], ["Fz", "Cz", "Pz"], arrays=arrays,
                      array_units=units).build_rbp_window(
        "run", cursor_s=5.0, field_ids=["rbp.absolute.alpha"],
    )

    assert result.status == "ready"
    assert [field.field_id for field in result.fields] == ["rbp.absolute.alpha"]
    assert result.fields[0].unit == "uV^2"
    assert result.provenance["field_ids"] == ["rbp.absolute.alpha"]


@pytest.mark.parametrize("band_id,low_hz,high_hz", [
    ("delta", 1.0, 4.0), ("theta", 4.0, 8.0), ("alpha", 8.0, 13.0),
    ("beta", 13.0, 30.0), ("gamma", 30.0, 50.0),
])
def test_psd_adapter_returns_band_mean_density_in_SI_units_and_original_channel_order(band_id, low_hz, high_hz):
    from app.services.topomap import PsdTopomapAdapter

    frequencies = np.arange(1.0, 51.0)
    result = PsdTopomapAdapter().build_input(
        recording_id="recording-1",
        payload={
            "channel_order": ["Cz", "Fz"],
            "axes": {"frequency_hz": frequencies.tolist()},
            "axis_units": {"frequency_hz": "Hz"},
            "arrays": {"psd": [frequencies.tolist(), (2 * frequencies).tolist()]},
            "array_units": {"psd": "V^2/Hz"},
            "quality": "clean",
            "evidence": {"extensions": {"channel_quality": {"by_channel": {
                "Cz": {"status": "clean", "reasons": []},
                "Fz": {"status": "clean", "reasons": []},
            }}}},
        },
        window_start_s=5.0,
        window_end_s=10.0,
        field_ids=[f"psd.band-mean-density.{band_id}"],
    )

    assert result.channel_order == ["Cz", "Fz"]
    expected_density = 0.5 * (low_hz + high_hz)
    assert result.values_by_band == {f"psd.band-mean-density.{band_id}": pytest.approx([expected_density, 2 * expected_density])}
    assert result.units_by_band == {f"psd.band-mean-density.{band_id}": "V^2/Hz"}
    assert result.metadata_by_field[f"psd.band-mean-density.{band_id}"] == {
        "frequency_range_hz": (low_hz, high_hz), "label": f"{band_id.title()} 平均 PSD ({low_hz:g}–{high_hz:g} Hz)",
    }
    assert result.provenance["bandwidth_hz"] == high_hz - low_hz
    assert result.provenance["projection_identity"] == "psd-band-mean-density-v1"


def test_psd_adapter_rejects_a_band_not_covered_by_the_frequency_axis():
    from app.services.topomap import PsdTopomapAdapter

    with pytest.raises(TopomapUnavailable) as exc_info:
        PsdTopomapAdapter().build_input(
            recording_id="recording-1",
            payload={
                "channel_order": ["Cz"],
                "axes": {"frequency_hz": [1.0, 15.5, 30.0]},
                "axis_units": {"frequency_hz": "Hz"},
                "arrays": {"psd": [[1.0, 1.0, 1.0]]},
                "array_units": {"psd": "V^2/Hz"},
                "evidence": {"extensions": {"channel_quality": {"by_channel": {
                    "Cz": {"status": "clean", "reasons": []},
                }}}},
            },
            window_start_s=5.0, window_end_s=10.0,
            field_ids=["psd.band-mean-density.gamma"],
        )

    assert exc_info.value.code == "TOPOMAP_BAND_RANGE_UNAVAILABLE"


def test_psd_adapter_preserves_exact_frequency_bin_fields_for_api_consumers():
    from app.services.topomap import PsdTopomapAdapter

    result = PsdTopomapAdapter().build_input(
        recording_id="recording-1",
        payload={
            "channel_order": ["Cz", "Fz"],
            "axes": {"frequency_hz": [1.0, 2.0]},
            "axis_units": {"frequency_hz": "Hz"},
            "arrays": {"psd": [[10.0, 11.0], [20.0, 21.0]]},
            "array_units": {"psd": "V^2/Hz"},
            "quality": "clean",
            "evidence": {"extensions": {"channel_quality": {"by_channel": {
                "Cz": {"status": "clean", "reasons": []},
                "Fz": {"status": "clean", "reasons": []},
            }}}},
        },
        window_start_s=5.0, window_end_s=10.0,
        field_ids=["psd.frequency-bin.1"],
    )

    assert result.values_by_band == {"psd.frequency-bin.1": [11.0, 21.0]}
    assert result.units_by_band == {"psd.frequency-bin.1": "V^2/Hz"}
    assert result.power_type == "psd"
    assert result.metadata_by_field == {
        "psd.frequency-bin.1": {"frequency_hz": 2.0, "label": "2 Hz"},
    }


def test_rbp_adapter_does_not_fallback_to_trace_for_old_absolute_results():
    bands = ("delta", "theta", "alpha", "beta", "gamma")
    result = RbpTopomapAdapter().build_input(
        recording_id="recording-1",
        payload={"result": {"arrays": {band: [0.2, 0.3, 0.5] for band in bands},
                             "array_units": {band: "ratio" for band in bands},
                             "evidence": {"extensions": {"channel_order": {"channels": ["Fz", "Cz", "Pz"]}},
                                          "calculation_trace": {"band_powers_uv2": {"Fz": {"alpha": 123}}}}}},
        window_start_s=5.0, window_end_s=10.0, field_ids=["rbp.absolute.alpha"],
    )
    assert result.unavailable is not None
    assert result.unavailable["code"] == "ABSOLUTE_POWER_NOT_AVAILABLE"


def test_topomap_route_requires_explicit_field_ids_and_rejects_old_query_contract(caplog):
    import logging

    from fastapi import FastAPI
    from fastapi.testclient import TestClient

    from app.api.topomap import dynamic_router, router

    class Service:
        field_ids = None

        @staticmethod
        def _result_service():
            bands = ("delta", "theta", "alpha", "beta", "gamma")
            arrays = {**{band: [0.2, 0.3, 0.5] for band in bands},
                      **{f"{band}_absolute_power": [1.0, 2.0, 3.0] for band in bands}}
            units = {**{band: {"unit": "ratio"} for band in bands},
                     **{f"{band}_absolute_power": {"unit": "uV^2"} for band in bands}}
            return _service([0.2, 0.3, 0.5], ["Fz", "Cz", "Pz"],
                            arrays=arrays, array_units=units)

        def build_rbp_window(self, _run_id, **kwargs):
            self.field_ids = kwargs["field_ids"]
            return self._result_service().build_rbp_window(
                "run", cursor_s=5.0, field_ids=self.field_ids,
                request_id=kwargs.get("request_id"))

        def build_dynamic_window(self, _window_id, **kwargs):
            self.field_ids = kwargs["field_ids"]
            return self._result_service().build_rbp_window(
                "run", cursor_s=5.0, field_ids=self.field_ids,
                request_id=kwargs.get("request_id"))

    service = Service()
    app = FastAPI()
    app.state.topomap_service = service
    app.include_router(router)
    app.include_router(dynamic_router)

    with TestClient(app) as client, caplog.at_level(logging.INFO):
        explicit = client.get("/api/runs/run/topomap?cursor_s=5&field_ids=rbp.absolute.alpha",
                             headers={"X-Request-ID": "topomap-request-1"})
        assert explicit.status_code == 200
        assert explicit.json()["fields"][0]["field_id"] == "rbp.absolute.alpha"
        assert service.field_ids == ["rbp.absolute.alpha"]
        assert "topomap_api_perf request_id=topomap-request-1" in caplog.text
        assert "topomap_interpolation_perf request_id=topomap-request-1" in caplog.text
        assert "field_count=1 grid_cells=4225" in caplog.text

        omitted = client.get("/api/runs/run/topomap?cursor_s=5")
        assert omitted.status_code == 422

        dynamic = client.get("/api/analysis-windows/window/topomap?field_ids=rbp.absolute.alpha")
        assert dynamic.status_code == 200
        assert dynamic.json()["fields"][0]["field_id"] == "rbp.absolute.alpha"

        invalid = client.get("/api/analysis-windows/window/topomap?field_ids=rbp.relative.ultra")
        assert invalid.status_code == 422

        old_contract = client.get("/api/analysis-windows/window/topomap?power_type=absolute&band_id=alpha")
        assert old_contract.status_code == 422


def test_interpolation_preserves_exact_grid_samples_and_circular_boundary():
    grid = TopomapService(None, None)._interpolate([
        ("left", -1.0, 0.0, 2.0), ("center", 0.0, 0.0, 3.0), ("right", 1.0, 0.0, 4.0),
    ])

    assert grid.values[32][32] == 3.0
    assert grid.values[0][0] is None
