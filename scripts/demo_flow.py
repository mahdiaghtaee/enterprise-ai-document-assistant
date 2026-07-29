"""Run the local end-to-end demo flow.

Start the stack first:

    docker compose up --build

Then run:

    python scripts/demo_flow.py

Optional environment variables:

    BASE_URL=http://localhost:5000
    SAMPLE_FILE=samples/sample-policy.txt
    QUERY="vendor contract approval process"
    QUESTION="Who needs to approve vendor contracts?"
    TOP_K=3
    PROCESSING_TIMEOUT_SECONDS=60
    JWT_TOKEN=<externally issued token>
    DEMO_USER_ID=demo-user
    DEMO_TENANT_ID=demo-tenant
    DEMO_ROLE=User
"""

from __future__ import annotations

import json
import mimetypes
import os
import sys
import time
from pathlib import Path
from typing import Any
from urllib import request
from urllib.error import HTTPError, URLError

from create_dev_token import create_token

BASE_URL = os.getenv("BASE_URL", "http://localhost:5000").rstrip("/")
SAMPLE_FILE = Path(os.getenv("SAMPLE_FILE", "samples/sample-policy.txt"))
QUERY = os.getenv("QUERY", "vendor contract approval process")
QUESTION = os.getenv("QUESTION", "Who needs to approve vendor contracts?")
TOP_K = int(os.getenv("TOP_K", "3"))
PROCESSING_TIMEOUT_SECONDS = int(os.getenv("PROCESSING_TIMEOUT_SECONDS", "60"))
JWT_TOKEN = os.getenv("JWT_TOKEN") or create_token(
    os.getenv("DEMO_USER_ID", "demo-user"),
    os.getenv("DEMO_ROLE", "User"),
    os.getenv("DEMO_TENANT_ID", "demo-tenant"),
    PROCESSING_TIMEOUT_SECONDS + 300,
)


def print_section(title: str) -> None:
    print(f"\n== {title} ==")


def authorization_headers() -> dict[str, str]:
    return {"Authorization": f"Bearer {JWT_TOKEN}"}


def get_json(path: str) -> Any:
    url = f"{BASE_URL}{path}"
    req = request.Request(url, headers=authorization_headers())
    with request.urlopen(req, timeout=30) as response:
        return json.loads(response.read().decode("utf-8"))


def post_json(path: str, payload: dict[str, Any]) -> Any:
    url = f"{BASE_URL}{path}"
    data = json.dumps(payload).encode("utf-8")
    headers = {"Content-Type": "application/json", **authorization_headers()}
    req = request.Request(url, data=data, headers=headers, method="POST")

    with request.urlopen(req, timeout=30) as response:
        return json.loads(response.read().decode("utf-8"))


def upload_file(path: str, file_path: Path) -> Any:
    boundary = "----EnterpriseDocumentAssistantDemoBoundary"
    content_type = mimetypes.guess_type(file_path.name)[0] or "text/plain"
    file_bytes = file_path.read_bytes()

    body = b"".join(
        [
            f"--{boundary}\r\n".encode(),
            (
                f'Content-Disposition: form-data; name="file"; filename="{file_path.name}"\r\n'
            ).encode(),
            f"Content-Type: {content_type}\r\n\r\n".encode(),
            file_bytes,
            f"\r\n--{boundary}--\r\n".encode(),
        ]
    )

    headers = {
        "Content-Type": f"multipart/form-data; boundary={boundary}",
        **authorization_headers(),
    }
    req = request.Request(
        f"{BASE_URL}{path}",
        data=body,
        headers=headers,
        method="POST",
    )

    with request.urlopen(req, timeout=60) as response:
        return json.loads(response.read().decode("utf-8"))


def wait_for_processing(status_path: str) -> Any:
    deadline = time.monotonic() + PROCESSING_TIMEOUT_SECONDS

    while time.monotonic() < deadline:
        status = get_json(status_path)
        if status.get("isTerminal"):
            if status.get("status") != "Completed":
                raise RuntimeError(
                    "Document processing failed: "
                    f"{status.get('lastErrorCode')}: {status.get('lastErrorSummary')}"
                )

            return status

        time.sleep(1)

    raise TimeoutError(
        f"Document processing did not complete within {PROCESSING_TIMEOUT_SECONDS} seconds."
    )


def print_json(value: Any) -> None:
    print(json.dumps(value, indent=2, ensure_ascii=False))


def main() -> int:
    print("Enterprise AI Document Assistant demo")
    print(f"Base URL: {BASE_URL}")
    print(f"Sample file: {SAMPLE_FILE}")

    if not SAMPLE_FILE.exists():
        print(f"Sample file not found: {SAMPLE_FILE}", file=sys.stderr)
        return 1

    try:
        print_section("Health check")
        print_json(get_json("/health"))

        print_section("Authenticated principal")
        print_json(get_json("/api/auth/me"))

        print_section("Upload document")
        upload = upload_file("/api/documents/upload", SAMPLE_FILE)
        print_json(upload)

        status_path = upload.get("processingStatusUrl")
        if not status_path:
            raise RuntimeError("Upload response did not contain processingStatusUrl.")

        print_section("Wait for background processing")
        print_json(wait_for_processing(status_path))

        print_section("Semantic search")
        print_json(
            post_json(
                "/api/documents/search",
                {
                    "query": QUERY,
                    "topK": TOP_K,
                },
            )
        )

        print_section("Ask grounded question")
        print_json(
            post_json(
                "/api/documents/ask",
                {
                    "question": QUESTION,
                    "topK": TOP_K,
                },
            )
        )

    except HTTPError as exc:
        print(f"HTTP error {exc.code}: {exc.read().decode('utf-8')}", file=sys.stderr)
        return 1
    except URLError as exc:
        print(f"Could not connect to the API at {BASE_URL}: {exc}", file=sys.stderr)
        print("Start the stack with: docker compose up --build", file=sys.stderr)
        return 1
    except (RuntimeError, TimeoutError) as exc:
        print(str(exc), file=sys.stderr)
        return 1

    print("\nDemo finished.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
