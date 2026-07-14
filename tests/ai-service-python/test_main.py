from uuid import UUID

from app.main import app
from fastapi.testclient import TestClient

client = TestClient(app)


def test_health_endpoint_returns_service_status() -> None:
    response = client.get("/health")

    assert response.status_code == 200
    payload = response.json()
    assert payload["service"] == "ai-service"
    assert payload["status"] == "ok"
    assert payload["checked_at"]


def test_index_endpoint_returns_queued_document() -> None:
    response = client.post(
        "/index",
        json={
            "file_name": "policy.txt",
            "content_type": "text/plain",
            "text": "Example policy content.",
        },
    )

    assert response.status_code == 200
    payload = response.json()
    assert payload["file_name"] == "policy.txt"
    assert payload["status"] == "queued_for_indexing"
    assert str(UUID(payload["document_id"])) == payload["document_id"]


def test_index_endpoint_validates_required_file_name() -> None:
    response = client.post("/index", json={"content_type": "text/plain"})

    assert response.status_code == 422
