from pathlib import Path

from fastapi.testclient import TestClient

from app.main import app
from app.services.projects import ProjectService
from app.services.recordings import RecordingService
from app.models.research_project import ProjectCreateRequest
from app.persistence import get_schema_version, migrate_database


def _client(tmp_path: Path) -> tuple[TestClient, str]:
    recordings = RecordingService(tmp_path / "recordings", tmp_path / "platform.sqlite3")
    recording = recordings.create_recording("source.edf", ".edf", b"project-source")
    app.state.recording_service = recordings
    app.state.project_service = ProjectService(tmp_path / "platform.sqlite3")
    return TestClient(app), recording.id


def test_project_hierarchy_uses_internal_subject_code_and_session_recording(tmp_path: Path):
    client, recording_id = _client(tmp_path)
    project = client.post("/api/projects", json={"name": "Attention study", "description": "local only"})
    assert project.status_code == 201
    project_id = project.json()["project_id"]

    subject = client.post(f"/api/projects/{project_id}/subjects", json={"local_code": "S-001", "sex": "female"})
    condition = client.post(f"/api/projects/{project_id}/conditions", json={"code": "rest", "label": "eyes open"})
    session = client.post(f"/api/projects/{project_id}/sessions", json={
        "subject_id": subject.json()["subject_id"], "condition_id": condition.json()["condition_id"],
        "recording_id": recording_id, "label": "baseline", "age_at_recording_years": 25,
    })

    assert session.status_code == 201
    assert session.json()["age_at_recording_years"] == 25
    assert client.get(f"/api/projects/{project_id}/sessions").json()[0]["subject_sex"] == "female"
    assert client.get(f"/api/projects/{project_id}/recordings").json()["recording_ids"] == [recording_id]
    assert client.get(f"/api/projects/{project_id}/sessions").json()[0]["recording_id"] == recording_id


def test_project_accepts_optional_name_rejects_unrelated_pii_and_duplicate_code(tmp_path: Path):
    client, _ = _client(tmp_path)
    project_id = client.post("/api/projects", json={"name": "Study"}).json()["project_id"]
    assert client.post(f"/api/projects/{project_id}/subjects", json={"local_code": "S-1", "address": "private"}).status_code == 422
    assert client.post(f"/api/projects/{project_id}/subjects", json={"local_code": "S-1", "name": "自填姓名"}).status_code == 201
    duplicate = client.post(f"/api/projects/{project_id}/subjects", json={"local_code": "S-1"})
    assert duplicate.status_code == 409
    assert duplicate.json()["code"] == "SUBJECT_CODE_CONFLICT"


def test_session_cannot_cross_project_boundaries(tmp_path: Path):
    client, recording_id = _client(tmp_path)
    first = client.post("/api/projects", json={"name": "First"}).json()["project_id"]
    second = client.post("/api/projects", json={"name": "Second"}).json()["project_id"]
    subject_id = client.post(f"/api/projects/{first}/subjects", json={"local_code": "S-1"}).json()["subject_id"]

    response = client.post(f"/api/projects/{second}/sessions", json={"subject_id": subject_id, "recording_id": recording_id})

    assert response.status_code == 404
    assert response.json()["code"] == "PROJECT_REFERENCE_NOT_FOUND"


def test_desktop_project_resolution_is_idempotent(tmp_path: Path):
    client, _ = _client(tmp_path)
    desktop_id = "desktop-project-stable-id"
    payload = {"name": "Desktop project", "description": "local project"}

    first = client.put(f"/api/projects/desktop/{desktop_id}", json=payload)
    second = client.put(f"/api/projects/desktop/{desktop_id}", json=payload)

    assert first.status_code == second.status_code == 200
    assert first.json()["project_id"] == second.json()["project_id"] == desktop_id
    assert [item["project_id"] for item in client.get("/api/projects").json()] == [desktop_id]


def test_project_migration_is_repeatable(tmp_path: Path):
    database = tmp_path / "migration.sqlite3"
    service = ProjectService(database)
    existing = service.create(ProjectCreateRequest(name="Existing"))

    assert migrate_database(database) == migrate_database(database)
    assert get_schema_version(database) >= 6
    assert service.get(existing.project_id) == existing
