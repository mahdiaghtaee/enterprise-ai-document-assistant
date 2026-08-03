"""Run the local end-to-end demo flow.

Start the stack first:

    docker compose up --build

Then run:

    python scripts/demo_flow.py

The default flow provisions a managed demo tenant, creates a one-time invitation,
accepts it as the demo user, uploads a document, and waits for the independent
worker to index it. Set JWT_TOKEN to use an already provisioned external token.

Optional environment variables:

    BASE_URL=http://localhost:5000
    SAMPLE_FILE=samples/sample-policy.txt
    QUERY="vendor contract approval process"
    QUESTION="Who needs to approve vendor contracts?"
    TOP_K=3
    PROCESSING_TIMEOUT_SECONDS=60
    JWT_TOKEN=<already provisioned external token>
    DEMO_BOOTSTRAP_MANAGED_TENANT=true
    DEMO_USER_ID=demo-user
    DEMO_ADMIN_USER_ID=demo-admin
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
DEMO_USER_ID = os.getenv("DEMO_USER_ID", "demo-user")
DEMO_ADMIN_USER_ID = os.getenv("DEMO_ADMIN_USER_ID", "demo-admin")
DEMO_TENANT_ID = os.getenv("DEMO_TENANT_ID", "demo-tenant")
DEMO_ROLE = os.getenv("DEMO_ROLE", "User")
EXTERNAL_JWT_TOKEN = os.getenv("JWT_TOKEN")
BOOTSTRAP_MANAGED_TENANT = os.getenv(
    "DEMO_BOOTSTRAP_MANAGED_TENANT",
    "false" if EXTERNAL_JWT_TOKEN else "true",
).lower() in {"1", "true", "yes"}
JWT_TOKEN = EXTERNAL_JWT_TOKEN or create_token(
    DEMO_USER_ID,
    DEMO_ROLE,
    DEMO_TENANT_ID,
    PROCESSING_TIMEOUT_SECONDS + 600,
)
ADMIN_TOKEN = create_token(
    DEMO_ADMIN_USER_ID,
    "Admin",
    DEMO_TENANT_ID,
    PROCESSING_TIMEOUT_SECONDS + 600,
)
PLATFORM_TOKEN = create_token(
    "demo-platform-admin",
    "PlatformAdmin",
    "platform",
    PROCESSING_TIMEOUT_SECONDS + 600,
)


def print_section(title: str) -> None:
    print(f"\n== {title} ==")


def authorization_headers(token: str = JWT_TOKEN) -> dict[str, str]:
    return {"Authorization": f"Bearer {token}"}


def request_json(
    method: str,
    path: str,
    payload: dict[str, Any] | None = None,
    token: str = JWT_TOKEN,
) -> Any:
    url = f"{BASE_URL}{path}"
    data = None if payload is None else json.dumps(payload).encode("utf-8")
    headers = authorization_headers(token)
    if payload is not None:
        headers["Content-Type"] = "application/json"
    req = request.Request(url, data=data, headers=headers, method=method)
    with request.urlopen(req, timeout=30) as response:
        body = response.read().decode("utf-8")
        return json.loads(body) if body else None


def get_json(path: str, token: str = JWT_TOKEN) -> Any:
    return request_json("GET", path, token=token)


def post_json(
    path: str,
    payload: dict[str, Any],
    token: str = JWT_TOKEN,
) -> Any:
    return request_json("POST", path, payload=payload, token=token)


def bootstrap_managed_tenant() -> None:
    try:
        get_json("/api/auth/me")
        return
    except HTTPError as exc:
        if exc.code != 403:
            raise

    try:
        post_json(
            "/api/platform/tenants",
            {
                "tenantId": DEMO_TENANT_ID,
                "displayName": "Local demo tenant",
                "initialAdminUserId": DEMO_ADMIN_USER_ID,
            },
            token=PLATFORM_TOKEN,
        )
    except HTTPError as exc:
        body = exc.read().decode("utf-8")
        if exc.code != 409 or "tenant_already_exists" not in body:
            raise RuntimeError(f"Tenant provisioning failed: HTTP {exc.code}: {body}") from exc

    invitations = get_json("/api/tenant/invitations", token=ADMIN_TOKEN)
    for invitation in invitations:
        if (
            invitation.get("inviteeUserId") == DEMO_USER_ID
            and invitation.get("status") == "Pending"
        ):
            post_json(
                f"/api/tenant/invitations/{invitation['id']}/revoke",
                {},
                token=ADMIN_TOKEN,
            )

    invitation = post_json(
        "/api/tenant/invitations",
        {
            "inviteeUserId": DEMO_USER_ID,
            "role": DEMO_ROLE,
            "lifetimeHours": 24,
        },
        token=ADMIN_TOKEN,
    )
    post_json(
        "/api/tenant/invitations/accept",
        {"token": invitation["token"]},
    )


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

        if BOOTSTRAP_MANAGED_TENANT:
            print_section("Provision managed tenant and membership")
            bootstrap_managed_tenant()
            print("Managed tenant membership is active.")

        print_section("Authenticated principal")
        print_json(get_json("/api/auth/me"))

        print_section("Upload document")
        upload = upload_file("/api/documents/upload", SAMPLE_FILE)
        print_json(upload)

        status_path = upload.get("processingStatusUrl")
        if not status_path:
            raise RuntimeError("Upload response did not contain processingStatusUrl.")

        print_section("Wait for independent background worker")
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
