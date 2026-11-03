from types import SimpleNamespace
import hashlib
import json

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


def _bind_dynamic_source_context(payload, channels):
    positions = {
        channel: {"x": x, "y": y, "status": "available", "enabled": True, "source": "snapshot"}
        for channel, (x, y) in {"Fz": (0.0, -0.8), "Cz": (0.0, 0.0), "Pz": (0.0, 0.8)}.items()
    }
    source = {
        "formulas": [], "map_positions": positions, "montage_id": "montage-1",
        "montage_revision": "2", "montage_fingerprint": "fingerprint-1",
        "snapshot_sha256": "fingerprint-1",
    }
    configuration = {"context": {"signal_mapping": {
        "channels": channels, "signal_source": source,
        "analysis_montage": {"id": "montage-1", "revision": 2, "fingerprint": "fingerprint-1"},
    }}}
    encoded = json.dumps(configuration, sort_keys=True, separators=(",", ":"))
    payload["scientific_configuration_json"] = encoded
    payload["identity"]["configuration_sha256"] = hashlib.sha256(encoded.encode("utf-8")).hexdigest()
    return payload


def test_rbp_topomap_uses_snapshot_positions_and_returns_bounded_grid():
    result = _service([0.2, 0.5, 0.8], ["Fz", "Cz", "Pz"]).build_rbp_window("run", cursor_s=5.0)
    assert result.status == "ready"
    assert [band.band_id for band in result.bands] == ["delta", "theta", "alpha", "beta", "gamma"]
    assert result.window_start_s == 0.0 and result.window_end_s == 5.0
    assert result.bands[0].grid is not None and result.bands[0].grid.width == result.bands[0].grid.height == 65
    assert result.bands[0].grid.head_radius == 1.0
    assert all(item.status == "included" for item in result.bands[0].points)
    assert all(value is None or np.isfinite(value) for row in result.bands[0].grid.values for value in row)
    assert result.provenance["montage_fingerprint"] == "fingerprint-1"
    assert result.power_type == "relative"
    assert result.bands[0].unit == "ratio"


def test_absolute_power_type_selects_structured_values_and_unit_for_same_window():
    names = ("delta", "theta", "alpha", "beta", "gamma")
    result = _service(
        [0.2, 0.3, 0.5], ["Fz", "Cz", "Pz"],
        arrays={f"{band}_absolute_power": [1.0, 2.0, 3.0] for band in names},
        array_units={f"{band}_absolute_power": {"unit": "uV^2"} for band in names},
    ).build_rbp_window("run", cursor_s=5.0, power_type="absolute")

    assert result.status == "ready"
    assert result.power_type == "absolute"
    assert result.window_start_s == 0.0 and result.window_end_s == 5.0
    assert result.bands[2].unit == "uV^2"
    assert [point.value for point in result.bands[2].points] == [1.0, 2.0, 3.0]


def test_dynamic_window_topomap_selects_absolute_power_without_changing_window():
    names = ("delta", "theta", "alpha", "beta", "gamma")
    channels = ["Fz", "Cz", "Pz"]
    payload = {
        "identity": {"algorithm_id": "rbp", "recording_id": "recording-1",
                     "scientific_version": "official-rbp-v4", "implementation_identity": "build"},
        "state": "ready", "plan": {"start_s": 10.0, "end_s": 15.0},
        "result": {
            "arrays": {f"{band}_absolute_power": [1.0, 2.0, 3.0] for band in names},
            "array_units": {f"{band}_absolute_power": "uV^2" for band in names},
            "evidence": {"extensions": {"channel_order": {"channels": channels}}},
        },
    }
    _bind_dynamic_source_context(payload, channels)
    service = _service([0.2, 0.3, 0.5], channels)
    recordings = service.run_queue.recordings
    recordings.require_recording = lambda *_args: pytest.fail("dynamic topomap must use its window source snapshot")
    recordings.select_analysis_montage = lambda *_args: pytest.fail("dynamic topomap must not resolve montage again")
    recordings.analysis_signal_provenance = lambda *_args: pytest.fail("dynamic topomap must not resolve provenance again")
    service.dynamic_window_service = SimpleNamespace(get=lambda *_args, **_kwargs: payload)

    result = service.build_dynamic_window("window-1", power_type="absolute")

    assert result.status == "ready"
    assert result.power_type == "absolute"
    assert result.window_start_s == 10.0 and result.window_end_s == 15.0
    assert all(band.unit == "uV^2" for band in result.bands)
    assert result.provenance["montage_fingerprint"] == "fingerprint-1"


def test_dynamic_window_topomap_rejects_montage_identity_mismatch():
    channels = ["Fz", "Cz", "Pz"]
    payload = _bind_dynamic_source_context({
        "identity": {"algorithm_id": "rbp", "recording_id": "recording-1",
                     "scientific_version": "official-rbp-v4", "implementation_identity": "build"},
        "state": "ready", "plan": {"start_s": 10.0, "end_s": 15.0},
        "result": {
            "arrays": {band: [0.2, 0.3, 0.5] for band in ("delta", "theta", "alpha", "beta", "gamma")},
            "array_units": {band: "ratio" for band in ("delta", "theta", "alpha", "beta", "gamma")},
            "evidence": {"extensions": {"channel_order": {"channels": channels}}},
        },
    }, channels)
    configuration = json.loads(payload["scientific_configuration_json"])
    configuration["context"]["signal_mapping"]["analysis_montage"]["fingerprint"] = "stale"
    encoded = json.dumps(configuration, sort_keys=True, separators=(",", ":"))
    payload["scientific_configuration_json"] = encoded
    payload["identity"]["configuration_sha256"] = hashlib.sha256(encoded.encode("utf-8")).hexdigest()
    service = _service([0.2, 0.3, 0.5], channels)
    service.dynamic_window_service = SimpleNamespace(get=lambda *_args, **_kwargs: payload)

    with pytest.raises(TopomapUnavailable) as exc_info:
        service.build_dynamic_window("window-1")

    assert exc_info.value.code == "TOPOMAP_SOURCE_CONTEXT_INVALID"


def test_dynamic_window_topomap_logs_request_correlated_service_stages(caplog):
    import logging

    names = ("delta", "theta", "alpha", "beta", "gamma")
    channels = ["Fz", "Cz", "Pz"]
    payload = {
        "identity": {"algorithm_id": "rbp", "recording_id": "recording-1",
                     "scientific_version": "official-rbp-v4", "implementation_identity": "build"},
        "state": "ready", "plan": {"start_s": 10.0, "end_s": 15.0},
        "result": {
            "arrays": {f"{band}_absolute_power": [1.0, 2.0, 3.0] for band in names},
            "array_units": {f"{band}_absolute_power": "uV^2" for band in names},
            "evidence": {"extensions": {"channel_order": {"channels": channels}}},
        },
    }
    _bind_dynamic_source_context(payload, channels)
    service = _service([0.2, 0.3, 0.5], channels)
    service.dynamic_window_service = SimpleNamespace(get=lambda *_args, **_kwargs: payload)

    with caplog.at_level(logging.INFO, logger="brain_platform.topomap"):
        service.build_dynamic_window("window-1", power_type="absolute", request_id="request-1")

    assert "topomap_stage_perf request_id=request-1 window_id=window-1 stage=signal_provenance" in caplog.text
    assert "topomap_stage_perf request_id=request-1 window_id=window-1 stage=dynamic_window_total" in caplog.text
    assert "payload_ms=" in caplog.text and "input_ms=" in caplog.text
    assert "context_ms=" in caplog.text and "build_ms=" in caplog.text and "total_ms=" in caplog.text


def test_old_result_absolute_request_returns_explicit_unavailable_map():
    result = _service([0.2, 0.3, 0.5], ["Fz", "Cz", "Pz"]).build_rbp_window(
        "run", cursor_s=5.0, power_type="absolute")

    assert result.status == "unavailable"
    assert result.power_type == "absolute"
    assert result.failure["code"] == "ABSOLUTE_POWER_NOT_AVAILABLE"
    assert all(band.status == "unavailable" and band.unit == "uV^2" for band in result.bands)


def test_topomap_excludes_bipolar_outputs_without_inventing_endpoint():
    service = _service([0.2, 0.5, 0.8], ["Fz-F4", "Cz", "Pz"], formulas=[
        {"name": "Fz-F4", "positive": "Fz", "negative_kind": "specifiedpair"},
    ])
    result = service.build_rbp_window("run", cursor_s=5.0)
    assert result.status == "unavailable"
    assert result.bands[0].points[0].reason == "bipolar_or_derived_output_has_no_spatial_rule"


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
    )
    assert result.channel_order == ["Fz", "Cz", "Pz"]
    assert result.values_by_band["alpha"] == [0.1, 0.2, 0.3]
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
        power_type="absolute",
    )

    assert result.power_type == "absolute"
    assert result.units_by_band == {band: "uV^2" for band in bands}
    assert result.values_by_band["alpha"] == [2.0, 3.0, 4.0]


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
        power_type="absolute", band_id="alpha",
    )

    assert list(result.values_by_band) == ["alpha"]
    assert result.values_by_band["alpha"] == [2.0, 3.0, 4.0]
    assert result.units_by_band == {"alpha": "uV^2"}


def test_rbp_adapter_rejects_unknown_band_id():
    with pytest.raises(TopomapUnavailable) as exc_info:
        RbpTopomapAdapter().build_input(
            recording_id="recording-1", payload={}, window_start_s=5.0, window_end_s=10.0,
            band_id="ultra",
        )

    assert exc_info.value.code == "TOPOMAP_BAND_ID_INVALID"


def test_run_topomap_builds_only_the_requested_band():
    bands = ("delta", "theta", "alpha", "beta", "gamma")
    arrays = {f"{band}_absolute_power": [1.0, 2.0, 3.0] for band in bands}
    units = {name: {"unit": "uV^2"} for name in arrays}
    result = _service([0.2, 0.5, 0.8], ["Fz", "Cz", "Pz"], arrays=arrays,
                      array_units=units).build_rbp_window(
        "run", cursor_s=5.0, power_type="absolute", band_id="alpha",
    )

    assert result.status == "ready"
    assert [band.band_id for band in result.bands] == ["alpha"]
    assert result.bands[0].unit == "uV^2"
    assert result.provenance["bands"] == ["alpha"]


def test_rbp_adapter_does_not_fallback_to_trace_for_old_absolute_results():
    bands = ("delta", "theta", "alpha", "beta", "gamma")
    result = RbpTopomapAdapter().build_input(
        recording_id="recording-1",
        payload={"result": {"arrays": {band: [0.2, 0.3, 0.5] for band in bands},
                             "array_units": {band: "ratio" for band in bands},
                             "evidence": {"extensions": {"channel_order": {"channels": ["Fz", "Cz", "Pz"]}},
                                          "calculation_trace": {"band_powers_uv2": {"Fz": {"alpha": 123}}}}}},
        window_start_s=5.0, window_end_s=10.0, power_type="absolute",
    )
    assert result.unavailable is not None
    assert result.unavailable["code"] == "ABSOLUTE_POWER_NOT_AVAILABLE"


def test_topomap_route_passes_power_type_and_band_filter_and_defaults_for_existing_caller(caplog):
    import logging

    from fastapi import FastAPI
    from fastapi.testclient import TestClient

    from app.api.topomap import dynamic_router, router

    class Service:
        power_type = ""
        band_id = None

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
            self.power_type = kwargs["power_type"]
            self.band_id = kwargs.get("band_id")
            return self._result_service().build_rbp_window(
                "run", cursor_s=5.0, power_type=self.power_type, band_id=self.band_id,
                request_id=kwargs.get("request_id"))

        def build_dynamic_window(self, _window_id, **kwargs):
            self.power_type = kwargs["power_type"]
            self.band_id = kwargs.get("band_id")
            return self._result_service().build_rbp_window(
                "run", cursor_s=5.0, power_type=self.power_type, band_id=self.band_id,
                request_id=kwargs.get("request_id"))

    service = Service()
    app = FastAPI()
    app.state.topomap_service = service
    app.include_router(router)
    app.include_router(dynamic_router)

    with TestClient(app) as client, caplog.at_level(logging.INFO):
        explicit = client.get("/api/runs/run/topomap?cursor_s=5&power_type=absolute&band_id=alpha",
                             headers={"X-Request-ID": "topomap-request-1"})
        assert explicit.status_code == 200
        assert explicit.json()["power_type"] == "absolute"
        assert service.band_id == "alpha"
        assert service.power_type == "absolute"
        assert "topomap_api_perf request_id=topomap-request-1" in caplog.text
        assert "topomap_interpolation_perf request_id=topomap-request-1" in caplog.text
        assert "band_id=alpha" in caplog.text
        assert "band_count=1 grid_cells=4225" in caplog.text

        omitted = client.get("/api/runs/run/topomap?cursor_s=5")
        assert omitted.status_code == 200
        assert omitted.json()["power_type"] == "relative"
        assert len(omitted.json()["bands"]) == 5
        assert service.power_type == "relative"

        dynamic = client.get("/api/analysis-windows/window/topomap?power_type=absolute")
        assert dynamic.status_code == 200
        assert dynamic.json()["power_type"] == "absolute"
        assert service.power_type == "absolute"
        assert service.band_id is None

        invalid = client.get("/api/analysis-windows/window/topomap?band_id=ultra")
        assert invalid.status_code == 422


def test_interpolation_preserves_exact_grid_samples_and_circular_boundary():
    grid = TopomapService(None, None)._interpolate([
        ("left", -1.0, 0.0, 2.0), ("center", 0.0, 0.0, 3.0), ("right", 1.0, 0.0, 4.0),
    ])

    assert grid.values[32][32] == 3.0
    assert grid.values[0][0] is None
