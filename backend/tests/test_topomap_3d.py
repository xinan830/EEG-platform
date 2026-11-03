from types import SimpleNamespace
from hashlib import sha256
from io import BytesIO
import json

import numpy as np
import pytest

from app.services.topomap import RbpTopomapAdapter
from app.services.topomap_3d import Topomap3DService, _surface_basis
from app.services.topomap_3d_assets import load_topomap_3d_assets
import app.services.topomap_3d_assets as asset_loader


def _local_xy(name):
    assets = load_topomap_3d_assets()
    point = np.asarray(assets.channel_locations[name]["coordinate_gnm"], dtype=float)
    local = (point - assets.head_origin) @ np.column_stack((
        assets.right_axis, assets.forward_axis, assets.up_axis,
    ))
    return local[:2] / local[2]


def _service(provenance=None):
    return Topomap3DService(SimpleNamespace(
        _signal_provenance=lambda _run: provenance or {"formulas": []},
    ))


def test_packaged_assets_match_mne_and_publish_distinct_coordinate_contracts():
    from importlib.resources import files

    assets = load_topomap_3d_assets()
    model = _service().get_model()

    assert len(assets.channel_locations) == 94
    assert assets.mapping_sha256 == "a973409df056b801113461c0e40d4381751217921505c3b64201cfb682b0f5d8"
    assert assets.mesh_geometry_sha256 == assets.mapping["core_mapping"]["mesh_sha256"]
    assert assets.full_montage["mne_coordinate_frame"] == "head"
    assert assets.full_montage["mne_units"] == "m"
    assert assets.full_montage["gnm_units"] == "GNM model units"
    assert assets.channel_locations["T3"]["coordinate_gnm"] == assets.channel_locations["T7"]["coordinate_gnm"]
    assert model.mesh_geometry_sha256 == assets.mesh_geometry_sha256
    assert len(model.electrode_locations) == 94
    assert {"A1", "A2", "M1", "M2"}.issubset(
        site.channel for site in model.electrode_locations
    )
    assert all(not site.is_scalp_field_site for site in model.electrode_locations
        if site.channel in {"A1", "A2", "M1", "M2"})
    assert not {"REF", "GND"}.intersection(
        site.channel for site in model.electrode_locations
    )
    assert len(model.vertices_gnm) == len(assets.vertices)
    package = files("app.assets.topomap_3d")
    assert "Apache License" in package.joinpath("LICENSE").read_text(encoding="utf-8")
    assert "standard-template visualization" in package.joinpath("NOTICE").read_text(encoding="utf-8")


def test_registered_scalp_sites_remain_close_to_the_gnm_surface():
    assets = load_topomap_3d_assets()
    labels = list(assets.channel_locations)
    sites = np.asarray([assets.channel_locations[label]["coordinate_gnm"] for label in labels])
    nearest_distances = np.linalg.norm(sites[:, None, :] - assets.vertices[None, :, :], axis=2).min(axis=1)
    model_diagonal = np.linalg.norm(np.ptp(assets.vertices, axis=0))

    assert model_diagonal > 0
    assert nearest_distances.max() / model_diagonal < 0.01


def test_3d_location_resolver_separates_mapped_positions_from_scalp_field_eligibility():
    result = _service().resolve_locations(["Pz", "T3", "A1", "M1", "REF", "GND", "unsupported", ""])

    assert [item.channel for item in result.locations] == ["Pz", "T3", "A1", "M1", "REF", "GND", "unsupported", ""]
    assert result.locations[0].status == "available"
    assert result.locations[0].is_scalp_field_site
    assert result.locations[1].canonical_channel == "T7"
    assert result.locations[1].is_scalp_field_site
    assert result.locations[1].target_coordinate_gnm == tuple(
        load_topomap_3d_assets().channel_locations["T7"]["coordinate_gnm"]
    )
    for location in result.locations[2:4]:
        assert location.status == "available"
        assert location.target_coordinate_gnm is not None
        assert not location.is_scalp_field_site
    assert result.locations[4].reason == "hardware_landmark_has_no_standard_electrode_site"
    assert result.locations[5].reason == "hardware_landmark_has_no_standard_electrode_site"
    assert result.locations[6].reason == "electrode_location_unavailable"
    assert result.locations[7].reason == "blank_channel_label"


def test_3d_location_http_contract_returns_typed_mapping_provenance():
    from fastapi import FastAPI
    from fastapi.testclient import TestClient

    from app.api.topomap_3d import model_router

    app = FastAPI()
    app.state.topomap_3d_service = _service()
    app.include_router(model_router)

    with TestClient(app) as client:
        response = client.post("/api/topomap/3d/locations", json={"channels": ["Fp1", "A1"]})

    assert response.status_code == 200
    body = response.json()
    assert body["source_coordinate_frame"] == "head"
    assert body["source_coordinate_unit"] == "m"
    assert body["target_coordinate_unit"] == "GNM model units"
    assert body["locations"][0]["status"] == "available"
    assert body["locations"][1]["status"] == "available"
    assert body["locations"][1]["target_coordinate_gnm"] is not None
    assert body["locations"][1]["is_scalp_field_site"] is False


@pytest.mark.parametrize("mutation", ["source_frame", "source_unit", "target_unit", "mesh_hash"])
def test_asset_loader_rejects_incompatible_coordinate_and_mesh_contracts(monkeypatch, mutation):
    package = asset_loader.files("app.assets.topomap_3d")
    mapping_bytes = package.joinpath(asset_loader.MAPPING_NAME).read_bytes()
    mesh_bytes = package.joinpath(asset_loader.MESH_NAME).read_bytes()
    document = json.loads(mapping_bytes.decode("utf-8-sig"))
    if mutation == "source_frame":
        document["full_montage"]["mne_coordinate_frame"] = "mri"
    elif mutation == "source_unit":
        document["full_montage"]["mne_units"] = "mm"
    elif mutation == "target_unit":
        document["full_montage"]["gnm_units"] = "mm"
    else:
        document["core_mapping"]["mesh_sha256"] = "0" * 64
    mapping_bytes = json.dumps(document).encode()

    class Resource:
        def __init__(self, content):
            self.content = content

        def read_bytes(self):
            return self.content

    class Package:
        @staticmethod
        def joinpath(name):
            return Resource(mapping_bytes if name.endswith(".json") else mesh_bytes)

    monkeypatch.setattr(asset_loader, "files", lambda _name: Package())
    monkeypatch.setattr(asset_loader, "MAPPING_SHA256", sha256(mapping_bytes).hexdigest())
    asset_loader.load_topomap_3d_assets.cache_clear()
    try:
        with pytest.raises(asset_loader.MappingAssetsInvalid):
            asset_loader.load_topomap_3d_assets()
    finally:
        asset_loader.load_topomap_3d_assets.cache_clear()


def test_surface_interpolation_preserves_linear_field_inside_electrode_hull():
    names = ["Fp1", "Fp2", "F3", "F4", "C3", "C4", "P3", "P4", "O1", "O2"]
    projected = np.asarray([_local_xy(name) for name in names])
    values = 0.4 + 0.08 * projected[:, 0] - 0.03 * projected[:, 1]
    result = _service()._build_band("alpha", "ratio", values.tolist(), names, {})
    basis = _surface_basis(tuple(names))
    assets = load_topomap_3d_assets()

    assert result.status == "ready"
    assert [electrode.value for electrode in result.electrodes] == values.tolist()
    assert result.value_min == pytest.approx(values.min())
    assert result.value_max == pytest.approx(values.max())
    for vertex_index, value in enumerate(result.vertex_values):
        if value is None:
            continue
        x, y = assets.projected_vertices[vertex_index]
        assert value == pytest.approx(0.4 + 0.08 * x - 0.03 * y, abs=1e-10)
        assert values.min() - 1e-12 <= value <= values.max() + 1e-12
    assert len(basis[0]) > 1


def test_aliases_share_one_site_and_reference_or_mastoid_channels_are_excluded():
    names = ["T3", "T7", "A1", "M1", "REF", "GND", "Fp1", "F3", "C3", "P3"]
    result = _service()._build_band("alpha", "ratio", [0.1] * len(names), names, {})

    assert result.status == "ready"
    assert "T7" in result.included_channels
    assert "T3" not in result.included_channels
    exclusions = {item.channel: item.reason for item in result.excluded_channels}
    assert exclusions["T3"] == "duplicate_spatial_coordinate"
    for channel in ("A1", "M1", "REF", "GND"):
        assert exclusions[channel] == "reference_or_mastoid_has_no_scalp_field"


def test_dynamic_topomap_consumes_existing_result_arrays_without_running_algorithm():
    names = ["Fp1", "Fp2", "F3", "F4", "C3", "C4", "P3", "P4", "O1", "O2"]
    bands = ("delta", "theta", "alpha", "beta", "gamma")
    values = [0.1] * len(names)
    payload = {
        "identity": {"algorithm_id": "rbp", "recording_id": "recording-1",
                     "scientific_version": "rbp-v1", "implementation_identity": "build-1"},
        "state": "ready", "plan": {"start_s": 10.0, "end_s": 15.0},
        "result": {"arrays": {band: values for band in bands},
                   "array_units": {band: "ratio" for band in bands},
                   "evidence": {"extensions": {"channel_order": {"channels": names}}}},
    }
    dynamic_service = SimpleNamespace(get=lambda *_args, **_kwargs: payload)
    topomap_service = SimpleNamespace(
        dynamic_window_service=dynamic_service,
        adapters={"rbp": RbpTopomapAdapter()},
        run_queue=SimpleNamespace(recordings=SimpleNamespace(
            require_recording=lambda _recording_id: SimpleNamespace(id="recording-1"),
        )),
        _signal_provenance=lambda _run: {"formulas": []},
    )

    result = Topomap3DService(topomap_service).build_dynamic_window("window-1", band_id="alpha")

    assert result.status == "ready"
    assert result.window_start_s == 10.0 and result.window_end_s == 15.0
    assert [band.band_id for band in result.bands] == ["alpha"]
    assert result.bands[0].electrodes[0].value == values[0]
    assert result.provenance["display_only"] is True


def test_full_run_topomap_reads_saved_window_result_without_reexecuting_rbp():
    names = ["Fp1", "Fp2", "F3", "F4", "C3", "C4", "P3", "P4", "O1", "O2"]
    values = [0.2] * len(names)
    run = SimpleNamespace(
        config={"algorithm_id": "rbp"}, recording_id="recording-1",
        result_summary={"structured": {"channel_order": names}},
    )
    preview = {
        "status": "available", "recording_id": "recording-1", "cursor_s": 15.0,
        "window": {"start_sample": 1000, "end_sample": 1500},
        "arrays": {band: values for band in ("delta", "theta", "alpha", "beta", "gamma")},
        "array_metadata": {band: {"unit": "ratio"} for band in ("delta", "theta", "alpha", "beta", "gamma")},
        "evidence": {"extensions": {"channel_order": {"channels": names}}},
        "scientific_version": "rbp-v1", "implementation_version": "build-1",
    }
    window_requests = []
    topomap_service = SimpleNamespace(
        result_service=SimpleNamespace(window_preview=lambda *args, **kwargs: (
            window_requests.append((args, kwargs)) or preview
        )),
        run_queue=SimpleNamespace(
            get=lambda _run_id: run,
            recordings=SimpleNamespace(require_recording=lambda _recording_id: SimpleNamespace(sfreq=100.0)),
        ),
        adapters={"rbp": RbpTopomapAdapter()},
        _signal_provenance=lambda _run: {"formulas": []},
    )

    result = Topomap3DService(topomap_service).build_run_window("run-1", cursor_s=15.0, band_id="alpha")

    assert result.status == "ready"
    assert result.window_start_s == 10.0 and result.window_end_s == 15.0
    assert [band.band_id for band in result.bands] == ["alpha"]
    assert window_requests == [(("run-1",), {"cursor_sample": None, "cursor_s": 15.0})]
