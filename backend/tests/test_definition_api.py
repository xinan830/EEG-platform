import sqlite3
from pathlib import Path

from fastapi.testclient import TestClient

from app.main import app
from app.models.algorithm_definition import DefinitionCreateRequest
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


def test_definition_api_deletes_unpublished_private_definition_only(tmp_path: Path):
    app.state.definition_service = DefinitionService(tmp_path / "definitions.sqlite3")
    client = TestClient(app)
    created = client.post("/api/algorithm-definitions", json={"name": "My Ratio", "description": "private"})
    definition_id = created.json()["definition_id"]

    deleted = client.delete(f"/api/algorithm-definitions/{definition_id}")

    assert deleted.status_code == 204
    assert client.get(f"/api/algorithm-definitions/{definition_id}").status_code == 404


def test_definition_api_rejects_platform_owner_and_protects_installed_official_definition(tmp_path: Path):
    service = DefinitionService(tmp_path / "definitions.sqlite3")
    app.state.definition_service = service
    client = TestClient(app)

    spoofed = client.post("/api/algorithm-definitions", json={"name": "Spoof", "owner": "platform-official"})
    assert spoofed.status_code == 422
    assert spoofed.json()["code"] == "DEFINITION_OWNER_FORBIDDEN"

    installed = service.create(DefinitionCreateRequest(name="Installed", owner="platform-official"))
    protected = client.delete(f"/api/algorithm-definitions/{installed.definition_id}")
    assert protected.status_code == 409
    assert protected.json()["code"] == "DEFINITION_DELETE_FORBIDDEN"

    cloned = client.post(f"/api/algorithm-definitions/{installed.definition_id}/clone", json={})
    assert cloned.status_code == 201
    assert cloned.json()["owner"] == "local-user"


def test_definition_api_preserves_definitions_referenced_by_runs_or_batches(tmp_path: Path):
    database_path = tmp_path / "definitions.sqlite3"
    service = DefinitionService(database_path)
    app.state.definition_service = service
    client = TestClient(app)
    run_definition = service.create(DefinitionCreateRequest(name="Run referenced"))
    batch_definition = service.create(DefinitionCreateRequest(name="Batch referenced"))

    with sqlite3.connect(database_path) as connection:
        connection.execute(
            """INSERT INTO analysis_runs (
                run_id, recording_id, analysis_type, status, definition_id,
                scientific_version, implementation_version, config_json, config_sha256,
                cache_key, requested_range_json, channel_mapping_json, reference_json,
                filter_json, window_json, quality_rules_json, environment_json,
                created_at, updated_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
            ("run-1", "recording-1", "definition", "completed", run_definition.definition_id,
             "research-primitives-v1", "test-build", "{}", "config", "cache", "{}", "{}", "{}",
             "{}", "{}", "{}", "{}", "now", "now"),
        )
        connection.execute(
            """INSERT INTO batch_runs (
                batch_run_id, project_id, analysis_type, definition_id, config_json,
                config_sha256, idempotency_key, status, created_at, updated_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
            ("batch-1", "project-1", "definition", batch_definition.definition_id, "{}", "config",
             "batch-key", "completed", "now", "now"),
        )

    for definition in (run_definition, batch_definition):
        response = client.delete(f"/api/algorithm-definitions/{definition.definition_id}")
        assert response.status_code == 409
        assert response.json()["code"] == "DEFINITION_IN_USE"
        assert client.get(f"/api/algorithm-definitions/{definition.definition_id}").status_code == 200
