from fastapi import FastAPI
from fastapi.testclient import TestClient
import pytest

from app.api.electrode_locations import router
from app.services.electrode_locations import resolve_electrode_labels


def client() -> TestClient:
    app = FastAPI()
    app.include_router(router)
    return TestClient(app)


def test_standard_template_returns_mne_coordinates_and_provenance():
    response = client().post("/api/electrode-locations/resolve", json={"labels": [" Fp1 ", "cz"]})
    assert response.status_code == 200
    payload = response.json()
    assert payload["montage"] == "standard_1020"
    fp1, cz = payload["locations"]
    assert fp1["canonical_site"] == "Fp1"
    assert fp1["coordinate_frame"] == "mri" and fp1["coordinate_unit"] == "m"
    assert fp1["coordinates"] == pytest.approx([-0.0294367, 0.0839171, -0.00699])
    assert fp1["source_id"] == "mne.standard_1020"
    assert cz["status"] == "available" and cz["canonical_site"] == "Cz"


def test_unsupported_and_non_electrode_labels_are_explicitly_unavailable():
    locations = resolve_electrode_labels(["", "REF", "GND", "not-a-site"])
    assert all(item["status"] == "unavailable" for item in locations)
    assert all(item["coordinates"] is None for item in locations)


def test_exact_matching_does_not_alias_legacy_temporal_sites():
    locations = resolve_electrode_labels(["T3", "T7", "t5", "P7"])
    assert [item["canonical_site"] for item in locations] == ["T3", "T7", "T5", "P7"]


def test_location_request_rejects_unknown_fields_and_oversized_batches():
    api = client()
    assert api.post("/api/electrode-locations/resolve", json={"labels": ["Cz"], "montage": "other"}).status_code == 422
    assert api.post("/api/electrode-locations/resolve", json={"labels": ["Cz"] * 129}).status_code == 422
