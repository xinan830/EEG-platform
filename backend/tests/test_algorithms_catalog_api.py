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
        "default_window_s": 5.0,
        "refresh_step_s": 1.0,
        "refresh_step_options_s": [1.0, 2.0, 5.0],
        "allow_warmup": True,
    }
    assert by_id["psd"]["dynamic_capabilities"]["combined_preview_background_full"]["implementation"] == "implemented"
    assert by_id["psd"]["dynamic_capabilities"]["combined_preview_background_full"]["validation"] == "validated"
    assert by_id["faa"]["dynamic_capabilities"]["full_result_to_preview"]["implementation"] == "unsupported"


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
    assert TestClient(app).get(f"/api/algorithm-definitions/{definition.definition_id}").status_code == 404

def test_public_official_catalog_matches_builtin_module_declarations() -> None:
    from app.algorithms.builtin_modules import build_builtin_modules
    from app.bootstrap import build_builtin_registry

    response = TestClient(app).get("/api/algorithms")
    assert response.status_code == 200
    official = [item for item in response.json()["algorithms"] if item["source"] == "official"]
    registry = build_builtin_registry()
    modules = {module.manifest.algorithm_id: module for module in build_builtin_modules()}
    assert {item["id"] for item in official} == set(modules)

    for item in official:
        module = registry.get_current(item["id"])
        assert module.manifest.algorithm_id == modules[item["id"]].manifest.algorithm_id
        manifest = module.manifest
        assert item["version"] == manifest.scientific_version
        assert item["display_name_zh"] == manifest.display_name_zh
        assert item["abbreviation"] == manifest.abbreviation
        assert item["description"] == manifest.purpose_zh
        assert item["modes"] == list(manifest.supported_modes)
        assert item["output_schema"] == manifest.output_schema
        assert item["dynamic_policy"] == manifest.dynamic_policy.model_dump(mode="json")
        assert item["availability"] == manifest.availability
        assert item["is_runnable"] == manifest.is_runnable
        assert item["implementation_identity"] == manifest.implementation_identity
        assert item["definition_version"] == manifest.definition_persistence.version
        expected_capabilities = (
            module.manifest.dynamic_capabilities.model_dump(mode="json")
            if module.manifest.dynamic_capabilities is not None else None
        )
        assert item["dynamic_capabilities"] == expected_capabilities
        expected_parameters = [parameter.model_dump(mode="json") for parameter in module.parameter_schema().parameters]
        assert item["parameters"] == expected_parameters
        assert item["implementation_identity"] == module.manifest.implementation_identity
