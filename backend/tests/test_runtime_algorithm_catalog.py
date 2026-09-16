from fastapi.testclient import TestClient

from app.main import app


def test_runtime_catalog_exposes_current_official_algorithms_without_definition_installation():
    response = TestClient(app).get("/api/algorithms")

    assert response.status_code == 200
    by_id = {item["id"]: item for item in response.json()["algorithms"] if item["source"] == "official"}
    assert set(by_id) == {"brainbeat", "faa", "iapf", "rbp", "theta_beta"}
    assert by_id["iapf"]["is_runnable"] is True
    assert by_id["theta_beta"]["is_runnable"] is True
    assert by_id["rbp"]["is_runnable"] is True
    assert by_id["faa"]["is_runnable"] is True
    assert by_id["brainbeat"]["is_runnable"] is False
    assert by_id["brainbeat"]["availability"] == "shadow_validation"
    assert all(item["availability"] == "available" for key, item in by_id.items() if key != "brainbeat")
