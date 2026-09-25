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
    theta_start = next(item for item in by_id["theta_beta"]["parameters"] if item["key"] == "start_s")
    assert theta_start["minimum"] == 0
    assert theta_start["step"] == 0.001
    assert by_id["theta_beta"]["is_runnable"] is True
    assert by_id["peak_frequency"]["output_schema"]["fields"][0]["unit"] == "Hz"
    assert by_id["peak_frequency"]["output_schema"]["fields"][0]["name"] == "peak_frequency_hz"
    assert by_id["band_ratio"]["output_schema"]["fields"][0]["unit"] == "ratio"
    assert next(item for item in by_id["band_ratio"]["parameters"] if item["key"] == "denominator_high_hz")["unit"] == "Hz"
    assert by_id["iapf"]["dynamic_policy"] == {
        "minimum_window_s": 4.0,
        "window_options_s": [5.0, 10.0, 20.0, 30.0],
        "default_window_s": 10.0,
        "refresh_step_s": 1.0,
        "allow_warmup": True,
    }


def test_active_catalog_excludes_historical_user_algorithms(tmp_path, monkeypatch) -> None:
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
    assert all(item["source"] == "official" for item in response.json()["algorithms"])
    assert not any(item["id"] == definition.definition_id for item in response.json()["algorithms"])
    assert TestClient(app).get(f"/api/algorithm-definitions/{definition.definition_id}").status_code == 200
