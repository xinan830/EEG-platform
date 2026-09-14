from pathlib import Path

from fastapi.testclient import TestClient

from app.main import app
from app.services.definitions import DefinitionService


def _draft(semver: str = "1.0.0") -> dict[str, object]:
    return {"semver": semver, "graph": {"nodes": [{"id": "out", "type": "output", "inputs": {"source": "$input.value"}}], "outputs": ["out"]},
            "parameter_schema": {"type": "object", "additionalProperties": False}}


def test_definition_api_persists_versions_publish_clone_compare_and_errors(tmp_path: Path):
    app.state.definition_service = DefinitionService(tmp_path / "definitions.sqlite3")
    client = TestClient(app)
    created = client.post("/api/algorithm-definitions", json={"name": "Ratio", "description": "test"})
    assert created.status_code == 201
    definition_id = created.json()["definition_id"]

    version = client.post(f"/api/algorithm-definitions/{definition_id}/versions", json=_draft())
    assert version.status_code == 201
    published = client.post(f"/api/algorithm-definitions/{definition_id}/versions/1.0.0/publish")
    assert published.json()["state"] == "published"
    listed = client.get(f"/api/algorithm-definitions/{definition_id}/versions")
    assert listed.json()[0]["digest_sha256"] == version.json()["digest_sha256"]
    clone = client.post(f"/api/algorithm-definitions/{definition_id}/clone", json={})
    assert clone.status_code == 201 and clone.json()["definition_id"] != definition_id
    invalid = client.post("/api/algorithm-definitions/validate", json={"draft": {**_draft(), "parameter_schema": {"type": "object", "patternProperties": {}}}})
    assert invalid.status_code == 422
    assert invalid.json()["code"] == "PARAMETER_INVALID"


def test_definition_capabilities_expose_closed_backend_authoring_vocabulary(tmp_path: Path):
    app.state.definition_service = DefinitionService(tmp_path / "definitions.sqlite3")
    response = TestClient(app).get("/api/algorithm-definitions/capabilities")

    assert response.status_code == 200
    assert "welch_psd" in response.json()["nodes"]
    assert "V^2/Hz" in response.json()["units"]
    assert response.json()["official_execution"]["iapf"] == "official_composite_shadow_only"
