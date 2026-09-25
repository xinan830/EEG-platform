from __future__ import annotations

from fastapi.testclient import TestClient

from app.bootstrap import build_builtin_registry
from app.main import app
from app.services.algorithm_presets import AlgorithmPresetService


def _config() -> dict[str, object]:
    return {"channel": "Fz", "mode": "static", "start_s": 0, "end_s": 10}


def test_algorithm_preset_crud_validates_against_official_runtime(tmp_path, monkeypatch):
    monkeypatch.setattr(
        app.state,
        "algorithm_preset_service",
        AlgorithmPresetService(build_builtin_registry(), tmp_path / "presets.sqlite3"),
    )
    client = TestClient(app)

    created = client.post("/api/algorithm-presets", json={
        "algorithm_id": "iapf",
        "scientific_version": "official-iapf-v2",
        "name": "默认 Alpha",
        "config": _config(),
    })
    assert created.status_code == 201
    preset = created.json()
    assert preset["config_sha256"]

    listed = client.get("/api/algorithm-presets?algorithm_id=iapf")
    assert listed.status_code == 200
    assert listed.json()[0]["preset_id"] == preset["preset_id"]

    updated = client.put(f"/api/algorithm-presets/{preset['preset_id']}", json={
        "name": "扩大范围",
        "config": {**_config(), "end_s": 20},
    })
    assert updated.status_code == 200
    assert updated.json()["name"] == "扩大范围"

    deleted = client.delete(f"/api/algorithm-presets/{preset['preset_id']}")
    assert deleted.status_code == 204


def test_algorithm_preset_rejects_invalid_config_before_persisting(tmp_path, monkeypatch):
    monkeypatch.setattr(
        app.state,
        "algorithm_preset_service",
        AlgorithmPresetService(build_builtin_registry(), tmp_path / "presets.sqlite3"),
    )
    response = TestClient(app).post("/api/algorithm-presets", json={
        "algorithm_id": "iapf",
        "scientific_version": "official-iapf-v2",
        "name": "错误预设",
        "config": {**_config(), "start_s": -1},
    })
    assert response.status_code == 422
    assert TestClient(app).get("/api/algorithm-presets").json() == []
