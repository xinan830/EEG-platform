import sqlite3
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from app.main import app
from app.models.algorithm_definition import DefinitionCreateRequest, DefinitionVersionDraft
from app.services.definitions import DefinitionService


def _historical_definition(database_path: Path) -> tuple[DefinitionService, str]:
    service = DefinitionService(database_path)
    definition = service.create(DefinitionCreateRequest(name="Historical ratio", owner="local-user"))
    service.create_version(definition.definition_id, DefinitionVersionDraft(
        semver="1.0.0",
        graph={"nodes": [{"id": "out", "type": "output", "inputs": {"source": "$input.value"}}], "outputs": ["out"]},
        parameter_schema={"type": "object", "additionalProperties": False},
    ))
    service.publish(definition.definition_id, "1.0.0")
    return service, definition.definition_id


def test_historical_definitions_and_versions_remain_readable(tmp_path: Path, monkeypatch):
    service, definition_id = _historical_definition(tmp_path / "definitions.sqlite3")
    monkeypatch.setattr(app.state, "definition_service", service)
    client = TestClient(app)

    assert any(item["definition_id"] == definition_id for item in client.get("/api/algorithm-definitions").json())
    assert client.get(f"/api/algorithm-definitions/{definition_id}").json()["name"] == "Historical ratio"
    versions = client.get(f"/api/algorithm-definitions/{definition_id}/versions").json()
    assert versions[0]["state"] == "published"
    comparison = client.get(f"/api/algorithm-definitions/{definition_id}/versions/1.0.0/compare/1.0.0")
    assert comparison.status_code == 200
    assert comparison.json()["same_digest"] is True


@pytest.mark.parametrize(("method", "path"), [
    ("get", "/api/algorithm-definitions/capabilities"),
    ("post", "/api/algorithm-definitions"),
    ("post", "/api/algorithm-definitions/validate"),
    ("post", "/api/algorithm-definitions/preview"),
    ("post", "/api/algorithm-definitions/preview-run"),
    ("post", "/api/algorithm-definitions/{id}/versions"),
    ("post", "/api/algorithm-definitions/{id}/versions/1.0.0/publish"),
    ("post", "/api/algorithm-definitions/{id}/clone"),
    ("delete", "/api/algorithm-definitions/{id}"),
])
def test_authoring_routes_are_retired_without_writes(tmp_path: Path, monkeypatch, method: str, path: str):
    database_path = tmp_path / "definitions.sqlite3"
    service, definition_id = _historical_definition(database_path)
    monkeypatch.setattr(app.state, "definition_service", service)
    with sqlite3.connect(database_path) as connection:
        before = tuple(connection.execute(f"SELECT COUNT(*) FROM {table}").fetchone()[0] for table in ("algorithm_definitions", "algorithm_definition_versions", "analysis_runs"))

    url = path.replace("{id}", definition_id)
    client = TestClient(app)
    response = client.post(url, json={}) if method == "post" else getattr(client, method)(url)

    assert response.status_code == 410
    assert response.json()["code"] == "USER_ALGORITHM_AUTHORING_RETIRED"
    with sqlite3.connect(database_path) as connection:
        after = tuple(connection.execute(f"SELECT COUNT(*) FROM {table}").fetchone()[0] for table in ("algorithm_definitions", "algorithm_definition_versions", "analysis_runs"))
    assert after == before
