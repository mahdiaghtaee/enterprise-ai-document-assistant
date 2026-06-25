from datetime import datetime, timezone
from typing import Optional
from uuid import uuid4

from fastapi import FastAPI
from pydantic import BaseModel

app = FastAPI(title="Enterprise Document AI Service")


class IndexDocumentRequest(BaseModel):
    file_name: str
    content_type: Optional[str] = None
    text: Optional[str] = None


@app.get("/health")
def health_check():
    return {
        "service": "ai-service",
        "status": "ok",
        "checked_at": datetime.now(timezone.utc).isoformat(),
    }


@app.post("/index")
def index_document(request: IndexDocumentRequest):
    # This is a placeholder for the first version.
    # In the next iteration this endpoint will parse, chunk and embed documents.
    return {
        "document_id": str(uuid4()),
        "file_name": request.file_name,
        "status": "queued_for_indexing",
    }
