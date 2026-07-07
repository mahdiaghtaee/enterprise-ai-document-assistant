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
"""

from __future__ import annotations

import json
import mimetypes
import os
import sys
from pathlib import Path
from typing import Any
from urllib import request
from urllib.error import HTTPError, URLError

BASE_URL = os.getenv("BASE_URL", "http://localhost:5000").rstrip("/")
SAMPLE_FILE = Path(os.getenv("SAMPLE_FILE", "samples/sample-policy.txt"))
QUERY = os.getenv("QUERY", "vendor contract approval process")
QUESTION = os.getenv("QUESTION", "Who needs to approve vendor contracts?")
TOP_K = int(os.getenv("TOP_K", "3"))


def print_section(title: str) -> None:
    print(f"\n== {title} ==")


def get_json(path: str) -> Any:
    url = f"{BASE_URL}{path}"
    with request.urlopen(url, timeout=30) as response:
        return json.loads(response.read().decode("utf-8"))


def post_json(path: str, payload: dict[str, Any]) -> Any:
    url = f"{BASE_URL}{path}"
    data = json.dumps(payload).encode("utf-8")
    req = request.Request(
        url,
        data=data,
        headers={"Content-Type": "application/json"},
        method="POST",
    )

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
                "Content-Disposition: form-data; "
                f'name="file"; filename="{file_path.name}"\r\n'
            ).encode(),
            f"Content-Type: {content_type}\r\n\r\n".encode(),
            file_bytes,
            f"\r\n--{boundary}--\r\n".encode(),
        ]
    )

    req = request.Request(
        f"{BASE_URL}{path}",
        data=body,
        headers={"Content-Type": f"multipart/form-data; boundary={boundary}"},
        method="POST",
    )

    with request.urlopen(req, timeout=60) as response:
        return json.loads(response.read().decode("utf-8"))


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

        print_section("Upload document")
        print_json(upload_file("/api/documents/upload", SAMPLE_FILE))

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

    print("\nDemo finished.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
