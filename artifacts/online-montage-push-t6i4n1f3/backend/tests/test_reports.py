from pathlib import Path

from fastapi.testclient import TestClient

from app.main import app


def _recording(client: TestClient) -> str:
    service = app.state.recording_service
    with service._connect() as connection:
        connection.execute(
            "INSERT OR REPLACE INTO recordings (id, original_name, stored_name, extension, created_at, duration_s, channels_json, mapping_json) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
            ("report-rec", "report.bdf", "report.bdf", ".bdf", "2026-01-01T00:00:00Z", 10.0, "[]", "{}"),
        )
    return "report-rec"


def test_report_snapshot_create_list_and_get_isolated():
    client = TestClient(app)
    recording_id = _recording(client)
    response = client.post(f"/api/recordings/{recording_id}/reports", json={"title": "阅图初版", "snapshot": {"timebase_s": 10, "events": []}})
    assert response.status_code == 201
    report = response.json()
    assert report["payload"]["timebase_s"] == 10
    assert client.get(f"/api/recordings/{recording_id}/reports").json()[0]["id"] == report["id"]
    assert client.get(f"/api/recordings/{recording_id}/reports/{report['id']}").status_code == 200
    assert client.get(f"/api/recordings/other/reports/{report['id']}").status_code == 404
