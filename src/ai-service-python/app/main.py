from datetime import datetime, timezone
from typing import Optional
from uuid import uuid4

from app.observability import (
    CORRELATION_HEADER,
    configure_observability,
    configure_structured_logging,
    current_trace_id,
    get_correlation_id,
    reset_correlation_id,
    resolve_correlation_id,
    set_correlation_id,
)
from fastapi import FastAPI, Request
from opentelemetry import trace
from pydantic import BaseModel

configure_structured_logging()
app = FastAPI(title="Enterprise Document AI Service")
index_requests = configure_observability(app)


class IndexDocumentRequest(BaseModel):
    file_name: str
    content_type: Optional[str] = None
    text: Optional[str] = None


@app.middleware("http")
async def correlate_request(request: Request, call_next):
    correlation_id = resolve_correlation_id(request.headers.get(CORRELATION_HEADER))
    token = set_correlation_id(correlation_id)
    trace.get_current_span().set_attribute("correlation.id", correlation_id)

    try:
        response = await call_next(request)
        response.headers[CORRELATION_HEADER] = correlation_id
        return response
    finally:
        reset_correlation_id(token)


@app.get("/health")
def health_check():
    return {
        "service": "ai-service",
        "status": "ok",
        "checked_at": datetime.now(timezone.utc).isoformat(),
        "correlation_id": get_correlation_id(),
        "trace_id": current_trace_id(),
    }


@app.post("/index")
def index_document(request: IndexDocumentRequest):
    index_requests.add(1, {"outcome": "queued"})
    return {
        "document_id": str(uuid4()),
        "file_name": request.file_name,
        "status": "queued_for_indexing",
        "correlation_id": get_correlation_id(),
        "trace_id": current_trace_id(),
    }
