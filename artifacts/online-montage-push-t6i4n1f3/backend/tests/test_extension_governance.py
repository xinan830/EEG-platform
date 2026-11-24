from fastapi.testclient import TestClient

from app.main import app


def _manifest(**overrides) -> dict[str, object]:
    result: dict[str, object] = {
        "module_id": "alpha-trend-template",
        "name": "Alpha trend template",
        "source": "research_template",
        "platform_compatibility": "brain-platform-api>=0.1.0,<0.2.0",
        "validation_state": "engineering_verified",
        "execution_mode": "closed_definition_graph",
        "permissions": ["read_derived_artifacts"],
        "input_types": ["PSDSeries"],
        "output_types": ["TimeSeries"],
    }
    result.update(overrides)
    return result


def test_extension_manifest_supports_local_sources_without_granting_permissions():
    client = TestClient(app)
    for source in ("local_official", "research_template", "user_private"):
        response = client.post("/api/extensions/validate", json=_manifest(source=source))
        assert response.status_code == 200
        assert response.json()["manifest"]["source"] == source
        assert response.json()["permission_semantics"] == "declarative_only_not_granted"
        assert response.json()["execution_boundaries"]["arbitrary_python"] is False


def test_extension_manifest_rejects_unknown_types_privileged_modes_and_unknown_fields():
    client = TestClient(app)
    bad_type = client.post("/api/extensions/validate", json=_manifest(input_types=["PythonPlugin"]))
    assert bad_type.status_code == 422
    assert bad_type.json()["code"] == "EXTENSION_MANIFEST_INVALID"

    metadata_permissions = client.post("/api/extensions/validate", json=_manifest(execution_mode="metadata_only"))
    assert metadata_permissions.status_code == 422
    assert metadata_permissions.json()["code"] == "EXTENSION_MANIFEST_INVALID"

    unknown_field = client.post("/api/extensions/validate", json={**_manifest(), "python_source": "import os"})
    assert unknown_field.status_code == 422
    assert unknown_field.json()["code"] == "INVALID_REQUEST"


def test_extension_capabilities_describe_safe_local_boundary():
    response = TestClient(app).get("/api/extensions/capabilities")
    assert response.status_code == 200
    body = response.json()
    assert body["sources"] == ["local_official", "research_template", "user_private"]
    assert "PSDSeries" in body["scientific_types"]
    assert body["boundaries"]["installed_plugins"] is False
