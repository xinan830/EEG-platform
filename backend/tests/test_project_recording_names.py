"""Project display names do not alter imported recording identity or source bytes."""

from pathlib import Path

import pytest

from test_recording_import import _client, _edf_bytes


def _import(client, tmp_path: Path):
    project_id = client.put("/api/projects/desktop/rename", json={"name": "Study"}).json()["project_id"]
    source = _edf_bytes(tmp_path / "source.edf")
    response = client.post(
        f"/api/projects/{project_id}/recordings/import",
        data={"new_subject_local_code": "S-01"}, files={"file": ("source.edf", source)},
    )
    assert response.status_code == 201, response.json()
    return project_id, response.json(), source


def test_rename_imported_recording_persists_only_the_owning_session_label(tmp_path: Path):
    client, recordings, projects = _client(tmp_path)
    project_id, imported, source = _import(client, tmp_path)
    before = recordings.require_recording(imported["recording_id"])
    subject_id = imported["subject_id"]
    second_session = client.post(f"/api/projects/{project_id}/sessions", json={
        "subject_id": subject_id, "recording_id": before.id, "label": "other reference",
    }).json()

    response = client.patch(f"/api/projects/{project_id}/sessions/{imported['session_id']}",
                            json={"label": "  静息态复测  "})

    assert response.status_code == 200, response.json()
    assert response.json()["label"] == "静息态复测"
    sessions = {item.session_id: item for item in projects.list_sessions(project_id)}
    assert sessions[imported["session_id"]].label == "静息态复测"
    assert sessions[second_session["session_id"]].label == "other reference"
    assert client.get(f"/api/projects/{project_id}/sessions").json()[0]["label"] == "静息态复测"
    assert recordings.require_recording(before.id) == before
    assert (recordings.storage_dir / before.stored_name).read_bytes() == source


@pytest.mark.parametrize("payload", [{"label": ""}, {"label": "  \t"},
                                      {"label": "x" * 4001}, {"label": "valid", "recording_id": "other"}])
def test_invalid_recording_names_do_not_change_session(tmp_path: Path, payload):
    client, _, projects = _client(tmp_path)
    project_id, imported, _ = _import(client, tmp_path)
    before = projects.list_sessions(project_id)

    response = client.patch(f"/api/projects/{project_id}/sessions/{imported['session_id']}", json=payload)

    assert response.status_code == 422
    assert response.json()["code"]
    assert projects.list_sessions(project_id) == before


def test_rename_rejects_cross_project_and_missing_session(tmp_path: Path):
    client, _, projects = _client(tmp_path)
    project_id, imported, _ = _import(client, tmp_path)
    other_id = client.put("/api/projects/desktop/other", json={"name": "Other"}).json()["project_id"]
    before = projects.list_sessions(project_id)

    for target_project, target_session in [(other_id, imported["session_id"]), (project_id, "missing")]:
        response = client.patch(f"/api/projects/{target_project}/sessions/{target_session}",
                                json={"label": "do not save"})
        assert response.status_code == 404
        assert response.json()["code"] == "RECORDING_SESSION_NOT_FOUND"
    assert projects.list_sessions(project_id) == before
