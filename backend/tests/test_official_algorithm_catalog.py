from fastapi.testclient import TestClient

from app.eeg_core.official_algorithms.registry import ensure_official_definitions
from app.main import app


def test_official_algorithm_catalog_exposes_the_five_shadow_only_algorithms():
    ensure_official_definitions(app.state.definition_service)
    response = TestClient(app).get("/api/official-algorithms")

    assert response.status_code == 200
    catalog = response.json()["algorithms"]
    assert [item["algorithm_id"] for item in catalog] == [
        "rbp", "theta_beta", "faa", "brainbeat", "iapf",
    ]
    assert [item["display_name_zh"] for item in catalog] == [
        "相对频段功率", "Theta/Beta 比值", "额叶 Alpha 不对称性", "脑节律指标", "个体 Alpha 峰频",
    ]
    assert all(item["availability"] == "shadow_validation" for item in catalog)
    assert all(item["is_runnable"] is False for item in catalog)
    assert all(item["definition_id"] and item["definition_version"] == "1.0.0" for item in catalog)
    assert all("graph" not in item and "quality_rules" not in item for item in catalog)


def test_definition_capabilities_derive_official_execution_from_catalog_registry():
    response = TestClient(app).get("/api/algorithm-definitions/capabilities")

    assert response.status_code == 200
    assert response.json()["official_execution"] == {
        "rbp": "generic_research_primitives",
        "theta_beta": "official_composite_shadow_only",
        "faa": "official_composite_shadow_only",
        "brainbeat": "official_composite_shadow_only",
        "iapf": "official_composite_shadow_only",
    }
