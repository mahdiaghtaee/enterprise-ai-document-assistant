from uuid import UUID

from app.main import app
from app.observability import CORRELATION_HEADER
from fastapi.testclient import TestClient

client = TestClient(app)


def test_health_endpoint_returns_service_status_and_correlation() -> None:
    response = client.get("/health")

    assert response.status_code == 200
    payload = response.json()
    assert payload["service"] == "ai-service"
    assert payload["status"] == "ok"
    assert payload["checked_at"]
    assert payload["correlation_id"]
    assert response.headers[CORRELATION_HEADER] == payload["correlation_id"]


def test_valid_correlation_id_is_echoed() -> None:
    response = client.get("/health", headers={CORRELATION_HEADER: "test-correlation-123"})

    assert response.status_code == 200
    assert response.headers[CORRELATION_HEADER] == "test-correlation-123"
    assert response.json()["correlation_id"] == "test-correlation-123"


def test_invalid_correlation_id_is_replaced() -> None:
    response = client.get("/health", headers={CORRELATION_HEADER: "invalid value with spaces"})

    assert response.status_code == 200
    assert response.headers[CORRELATION_HEADER] != "invalid value with spaces"
    assert len(response.headers[CORRELATION_HEADER]) == 32


def test_index_endpoint_returns_queued_document() -> None:
    response = client.post(
        "/index",
        headers={CORRELATION_HEADER: "index-flow"},
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
    assert payload["correlation_id"] == "index-flow"
    assert response.headers[CORRELATION_HEADER] == "index-flow"
    assert str(UUID(payload["document_id"])) == payload["document_id"]


def test_index_endpoint_validates_required_file_name() -> None:
    response = client.post("/index", json={"content_type": "text/plain"})

    assert response.status_code == 422
    assert response.headers[CORRELATION_HEADER]
