from fastapi.testclient import TestClient

from app.main import app
from app.models.algorithm_definition import DefinitionCreateRequest, DefinitionVersionDraft
from app.services.definitions import DefinitionService


def test_unified_algorithm_catalog_exposes_readable_official_entries() -> None:
    response = TestClient(app).get("/api/algorithms")
    assert response.status_code == 200
    items = response.json()["algorithms"]
    by_id = {item["id"]: item for item in items if item["source"] == "official"}
    assert by_id["iapf"]["display_name_zh"] == "个体 Alpha 峰频率"
    assert by_id["theta_beta"]["display_name_zh"] == "Theta/Beta 比值"
    assert by_id["theta_beta"]["parameters"][0]["key"] == "channel"
    assert by_id["theta_beta"]["is_runnable"] is True
    assert by_id["iapf"]["dynamic_policy"] == {
        "minimum_window_s": 4.0,
        "window_options_s": [5.0, 10.0, 20.0, 30.0],
        "default_window_s": 10.0,
        "refresh_step_s": 1.0,
        "allow_warmup": True,
    }


def test_unified_catalog_gives_user_algorithms_the_same_runtime_parameter_contract(tmp_path, monkeypatch) -> None:
    service = DefinitionService(tmp_path / "catalog.sqlite3")
    monkeypatch.setattr(app.state, "definition_service", service)
    definition = service.create(DefinitionCreateRequest(name="我的比值", description="", owner="local-user"))
    service.create_version(definition.definition_id, DefinitionVersionDraft(
        semver="1.0.0", graph={"nodes": [{"id": "out", "type": "output", "inputs": {"source": "$input.value"}}], "edges": [], "outputs": ["out"]}, parameter_schema={"type": "object", "additionalProperties": False},
        inputs={}, outputs={"output": {"unit": "dimensionless"}},
    ))
    service.publish(definition.definition_id, "1.0.0")

    response = TestClient(app).get("/api/algorithms")

    assert response.status_code == 200
    item = next(item for item in response.json()["algorithms"] if item["id"] == definition.definition_id)
    assert item["source"] == "user"
    assert item["parameters"][0]["key"] == "channel"
    assert item["output"] == {"unit": "dimensionless"}
