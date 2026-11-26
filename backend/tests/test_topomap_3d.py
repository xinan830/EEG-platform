from hashlib import sha256
from io import BytesIO
import json

import numpy as np
import pytest

from app.services.topomap_3d import Topomap3DService
from app.services.topomap_3d_assets import load_topomap_3d_assets
import app.services.topomap_3d_assets as asset_loader


def _service():
    return Topomap3DService()


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

    from app.api.topomap_3d import router

    app = FastAPI()
    app.state.topomap_3d_service = _service()
    app.include_router(router)

    with TestClient(app) as client:
        response = client.post("/api/topomap/3d/locations", json={"channels": ["Fp1", "A1"]})
        paths = set(client.get("/openapi.json").json()["paths"])

    assert response.status_code == 200
    assert paths == {
        "/api/topomap/3d/model",
        "/api/topomap/3d/locations",
        "/api/topomap/3d/cortex",
        "/api/topomap/3d/montages",
        "/api/topomap/3d/montages/{montage_name}",
    }
    body = response.json()
    assert body["source_coordinate_frame"] == "head"
    assert body["source_coordinate_unit"] == "m"
    assert body["target_coordinate_unit"] == "GNM model units"
    assert body["locations"][0]["status"] == "available"
    assert body["locations"][1]["status"] == "available"
    assert body["locations"][1]["target_coordinate_gnm"] is not None
    assert body["locations"][1]["is_scalp_field_site"] is False


def test_3d_cortex_http_contract_returns_aligned_pial_and_atlases():
    from fastapi import FastAPI
    from fastapi.testclient import TestClient

    from app.api.topomap_3d import router

    app = FastAPI()
    app.state.topomap_3d_service = _service()
    app.include_router(router)

    with TestClient(app) as client:
        response = client.get("/api/topomap/3d/cortex")

    assert response.status_code == 200
    body = response.json()
    assert body["vertex_count"] == 20484
    assert body["triangle_count"] == 40960
    assert body["target_coordinate_frame"] == "gnm_native"
    assert set(body["atlases"]) == {"lobes", "dk", "yeo7"}
    assert body["vertices_f32_b64"]
    assert body["triangles_i32_b64"]
    assert body["sulc_f32_b64"]


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


def test_core_montage_catalog_lists_15_eeg_montages_with_colin27_default():
    catalog = _service().list_montages()
    names = [item.name for item in catalog.montages]

    assert catalog.default_montage == "colin27_1020"
    assert len(catalog.montages) == 15
    assert catalog.montages[0].name == "colin27_1020"
    assert catalog.montages[0].is_default is True
    assert catalog.montages[0].channel_count == 94
    assert {"fsaverage_1020", "fsaverage_1010", "colin27_1005", "biosemi128", "GSN-HydroCel-128", "easycap-M1", "brainproducts-RNP-BA-128"}.issubset(names)
    assert all(not name.startswith("artinis-") for name in names)


def test_all_core_montages_project_finite_2d_and_surface_3d_gnm_sites():
    service = _service()
    assets = load_topomap_3d_assets()
    model_diagonal = np.linalg.norm(np.ptp(assets.vertices, axis=0))

    for item in service.list_montages().montages:
        preview = service.preview_montage(item.name)
        assert preview.channel_count == len(preview.sites) == item.channel_count
        if item.is_default:
            assert preview.projection_source == "gnm_mapped_colin27"
            for site in preview.sites:
                assert site.coordinate_gnm == tuple(assets.channel_locations[site.channel]["coordinate_gnm"])
        else:
            assert preview.projection_source == "dynamic_head_mesh_projection"

        coords_2d = np.asarray([[site.map_x, site.map_y] for site in preview.sites], dtype=float)
        assert np.all(np.isfinite(coords_2d))
        assert np.max(np.linalg.norm(coords_2d, axis=1)) <= 0.9201

        coords_3d = np.asarray([site.coordinate_gnm for site in preview.sites], dtype=float)
        assert np.all(np.isfinite(coords_3d))
        nearest = np.linalg.norm(coords_3d[:, None, :] - assets.vertices[None, :, :], axis=2).min(axis=1)
        assert nearest.max() / model_diagonal < 0.025

    fs20 = {site.channel: np.asarray(site.coordinate_gnm) - assets.head_origin for site in service.preview_montage("fsaverage_1020").sites}
    assert fs20["T7"] @ assets.right_axis < 0 < fs20["T8"] @ assets.right_axis
    assert fs20["Fp1"] @ assets.forward_axis > fs20["O1"] @ assets.forward_axis
    assert fs20["Cz"] @ assets.up_axis > fs20["Fp1"] @ assets.up_axis

    bio128_preview = service.preview_montage("biosemi128")
    bio128_by_ch = {site.channel: site for site in bio128_preview.sites}
    assert bio128_by_ch["A1"].is_scalp_field_site is True
    assert bio128_by_ch["A2"].is_scalp_field_site is True
    bio128_u = {ch: float((np.asarray(site.coordinate_gnm) - assets.head_origin) @ assets.up_axis) for ch, site in bio128_by_ch.items()}
    assert bio128_u["A1"] > bio128_u["A19"]
    assert min(bio128_u.values()) > -0.04


def test_montage_preview_http_endpoints_and_missing_montage_404():
    from fastapi import FastAPI
    from fastapi.testclient import TestClient

    from app.api.topomap_3d import router

    app = FastAPI()
    app.state.topomap_3d_service = _service()
    app.include_router(router)

    with TestClient(app) as client:
        catalog_resp = client.get("/api/topomap/3d/montages")
        bio_resp = client.get("/api/topomap/3d/montages/biosemi128")
        missing_resp = client.get("/api/topomap/3d/montages/unknown-cap")

    assert catalog_resp.status_code == 200
    assert catalog_resp.json()["default_montage"] == "colin27_1020"
    assert bio_resp.status_code == 200
    assert bio_resp.json()["channel_count"] == 128
    assert len(bio_resp.json()["sites"]) == 128
    assert missing_resp.status_code == 404
    assert missing_resp.json()["code"] == "MONTAGE_NOT_FOUND"
