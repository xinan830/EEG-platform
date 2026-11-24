from fastapi.testclient import TestClient

from app.main import app


def test_health_reports_backend_identity():
    response = TestClient(app).get("/api/health")

    assert response.status_code == 200
    assert response.json() == {
        "status": "ok",
        "service": "brain-platform-backend",
    }
