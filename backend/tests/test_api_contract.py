from fastapi.testclient import TestClient

from app.main import app


def test_http_errors_have_stable_shape_and_request_id():
    response = TestClient(app).get(
        "/api/recordings/does-not-exist",
        headers={"X-Request-ID": "contract-test"},
    )

    assert response.status_code == 404
    assert response.headers["X-Request-ID"] == "contract-test"
    assert response.json()["code"] == "RESOURCE_NOT_FOUND"
    assert response.json()["message"] == response.json()["detail"]
    assert response.json()["request_id"] == "contract-test"
