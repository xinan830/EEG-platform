from concurrent.futures import ThreadPoolExecutor

from fastapi import FastAPI
from fastapi.testclient import TestClient
import pytest
import mne
import matplotlib

from app.api.electrode_locations import router
from app.services.electrode_locations import _standard_positions, resolve_electrode_labels


def client() -> TestClient:
    app = FastAPI()
    app.include_router(router)
    return TestClient(app)


def test_standard_template_returns_mne_coordinates_and_provenance():
    response = client().post("/api/electrode-locations/resolve", json={"labels": [" Fp1 ", "cz"]})
    assert response.status_code == 200
    payload = response.json()
    assert payload["montage"] == "colin27_1020"
    fp1, cz = payload["locations"]
    assert fp1["canonical_site"] == "Fp1"
    assert fp1["coordinate_frame"] == "mri" and fp1["coordinate_unit"] == "m"
    assert fp1["coordinates"] == pytest.approx([-0.0294367, 0.0839171, -0.00699])
    montage = mne.channels.make_standard_montage("colin27_1020")
    unique_sites = {}
    for name, xyz in montage.get_positions()["ch_pos"].items():
        unique_sites.setdefault(tuple(float(value) for value in xyz), name)
    info = mne.create_info(list(unique_sites.values()), sfreq=256, ch_types="eeg")
    info.set_montage(montage)
    assert len(fp1["map_coordinates"]) == 2
    assert fp1["map_coordinates"][1] < 0
    assert fp1["source_id"] == "mne.colin27_1020"
    assert cz["status"] == "available" and cz["canonical_site"] == "Cz"


def test_unsupported_and_non_electrode_labels_are_explicitly_unavailable():
    locations = resolve_electrode_labels(["", "REF", "GND", "not-a-site"])
    assert all(item["status"] == "unavailable" for item in locations)
    assert all(item["coordinates"] is None for item in locations)
    assert all(item["map_coordinates"] is None for item in locations)


def test_mne_topomap_places_occipital_sites_at_the_posterior_head_edge():
    labels = mne.channels.make_standard_montage("colin27_1020").ch_names
    locations = {item["canonical_site"]: item for item in resolve_electrode_labels(labels)}
    assert locations["Fp1"]["map_coordinates"][1] < locations["O1"]["map_coordinates"][1]
    assert locations["Fz"]["map_coordinates"][1] < locations["Cz"]["map_coordinates"][1]
    assert locations["Cz"]["map_coordinates"][1] < locations["Pz"]["map_coordinates"][1]
    for label in ("O1", "O2", "Oz"):
        assert locations[label]["map_coordinates"][1] > 0.7
    for item in locations.values():
        x, y = item["map_coordinates"]
        assert x * x + y * y <= 1


def test_exact_matching_does_not_alias_legacy_temporal_sites():
    locations = resolve_electrode_labels(["T3", "T7", "t5", "P7"])
    assert [item["canonical_site"] for item in locations] == ["T3", "T7", "T5", "P7"]


def test_concurrent_first_resolution_uses_noninteractive_matplotlib_backend():
    assert matplotlib.get_backend().lower() == "agg"
    _standard_positions.cache_clear()

    with ThreadPoolExecutor(max_workers=8) as executor:
        results = list(executor.map(resolve_electrode_labels, [["O1", "O2"]] * 8))

    assert all([item["canonical_site"] for item in result] == ["O1", "O2"] for result in results)


def test_location_request_rejects_unknown_fields_and_oversized_batches():
    api = client()
    assert api.post("/api/electrode-locations/resolve", json={"labels": ["Cz"], "montage": "other"}).status_code == 422
    assert api.post("/api/electrode-locations/resolve", json={"labels": ["Cz"] * 129}).status_code == 422
