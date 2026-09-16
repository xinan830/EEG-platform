from fastapi.testclient import TestClient

from app.main import app


def test_unified_algorithm_catalog_exposes_readable_official_entries() -> None:
    response = TestClient(app).get("/api/algorithms")
    assert response.status_code == 200
    items = response.json()["algorithms"]
    by_id = {item["id"]: item for item in items if item["source"] == "official"}
    assert by_id["iapf"]["display_name_zh"] == "个体 Alpha 峰频率"
    assert by_id["theta_beta"]["display_name_zh"] == "Theta/Beta 比值"
    assert by_id["theta_beta"]["parameters"][0]["key"] == "channel"
    assert by_id["theta_beta"]["is_runnable"] is True
