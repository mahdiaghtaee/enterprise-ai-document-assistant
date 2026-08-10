#!/usr/bin/env python3
"""Exercise safe TXT/PDF/DOCX upload boundaries against the local Compose stack."""

from __future__ import annotations

import io
import json
import subprocess
import sys
import time
import uuid
import zipfile
from pathlib import Path
from urllib import error, request
from xml.sax.saxutils import escape

API_BASE = "http://localhost:5000"
DOCX_CONTENT_TYPE = (
    "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
)


def create_token(user: str, tenant: str, role: str) -> str:
    completed = subprocess.run(
        [
            sys.executable,
            "scripts/create_dev_token.py",
            "--user",
            user,
            "--tenant",
            tenant,
            "--role",
            role,
        ],
        check=True,
        capture_output=True,
        text=True,
    )
    return completed.stdout.strip()


def json_request(
    method: str,
    path: str,
    token: str | None = None,
    payload: dict[str, object] | None = None,
) -> tuple[int, object]:
    data = None if payload is None else json.dumps(payload).encode("utf-8")
    headers = {"Content-Type": "application/json"} if data is not None else {}
    if token:
        headers["Authorization"] = f"Bearer {token}"

    req = request.Request(f"{API_BASE}{path}", data=data, headers=headers, method=method)
    try:
        with request.urlopen(req, timeout=20) as response:
            raw = response.read()
            return response.status, json.loads(raw) if raw else None
    except error.HTTPError as exc:
        raw = exc.read()
        return exc.code, json.loads(raw) if raw else None


def upload(token: str, file_name: str, content_type: str, content: bytes) -> tuple[int, object]:
    boundary = f"----document-format-{uuid.uuid4().hex}"
    prefix = (
        f"--{boundary}\r\n"
        f'Content-Disposition: form-data; name="file"; filename="{file_name}"\r\n'
        f"Content-Type: {content_type}\r\n\r\n"
    ).encode("ascii")
    suffix = f"\r\n--{boundary}--\r\n".encode("ascii")
    body = prefix + content + suffix
    req = request.Request(
        f"{API_BASE}/api/documents/upload",
        data=body,
        headers={
            "Authorization": f"Bearer {token}",
            "Content-Type": f"multipart/form-data; boundary={boundary}",
        },
        method="POST",
    )

    try:
        with request.urlopen(req, timeout=30) as response:
            raw = response.read()
            return response.status, json.loads(raw) if raw else None
    except error.HTTPError as exc:
        raw = exc.read()
        return exc.code, json.loads(raw) if raw else None


def create_pdf(text: str) -> bytes:
    escaped = text.replace("\\", "\\\\").replace("(", "\\(").replace(")", "\\)")
    stream = f"BT /F1 12 Tf 72 720 Td ({escaped}) Tj ET".encode("ascii")
    objects = [
        b"<< /Type /Catalog /Pages 2 0 R >>",
        b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        (
            b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] "
            b"/Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>"
        ),
        b"<< /Length " + str(len(stream)).encode("ascii") + b" >>\nstream\n" + stream + b"\nendstream",
        b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
    ]

    output = bytearray(b"%PDF-1.4\n%\xe2\xe3\xcf\xd3\n")
    offsets = [0]
    for index, obj in enumerate(objects, start=1):
        offsets.append(len(output))
        output.extend(f"{index} 0 obj\n".encode("ascii"))
        output.extend(obj)
        output.extend(b"\nendobj\n")

    xref_offset = len(output)
    output.extend(f"xref\n0 {len(objects) + 1}\n".encode("ascii"))
    output.extend(b"0000000000 65535 f \n")
    for offset in offsets[1:]:
        output.extend(f"{offset:010d} 00000 n \n".encode("ascii"))
    output.extend(
        (
            f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R >>\n"
            f"startxref\n{xref_offset}\n%%EOF\n"
        ).encode("ascii")
    )
    return bytes(output)


def create_docx(text: str) -> bytes:
    output = io.BytesIO()
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        archive.writestr(
            "[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
            "<Override PartName=\"/word/document.xml\" "
            "ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>"
            "</Types>",
        )
        archive.writestr(
            "word/document.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"
            f"<w:body><w:p><w:r><w:t>{escape(text)}</w:t></w:r></w:p></w:body>"
            "</w:document>",
        )
    return output.getvalue()


def wait_for_completion(token: str, processing_url: str) -> None:
    deadline = time.monotonic() + 90
    while time.monotonic() < deadline:
        status_code, payload = json_request("GET", processing_url, token)
        if status_code != 200 or not isinstance(payload, dict):
            raise AssertionError((status_code, payload))

        status = payload.get("status")
        if status == "Completed":
            return
        if status == "Failed":
            raise AssertionError(payload)
        time.sleep(1)

    raise AssertionError(f"Processing did not complete: {processing_url}")


def main() -> None:
    platform = create_token("format-platform", "platform", "PlatformAdmin")
    admin = create_token("format-admin", "format-tenant", "Admin")
    user = create_token("format-user", "format-tenant", "User")

    code, health = json_request("GET", "/health")
    assert code == 200 and isinstance(health, dict), health
    assert health["fileThreatScanningProvider"] == "Disabled", health

    code, tenant = json_request(
        "POST",
        "/api/platform/tenants",
        platform,
        {
            "tenantId": "format-tenant",
            "displayName": "Format smoke tenant",
            "initialAdminUserId": "format-admin",
        },
    )
    assert code == 200 or code == 201, tenant

    code, invitation = json_request(
        "POST",
        "/api/tenant/invitations",
        admin,
        {"inviteeUserId": "format-user", "role": "User", "lifetimeHours": 24},
    )
    assert code == 200 or code == 201, invitation
    assert isinstance(invitation, dict) and invitation.get("token"), invitation

    code, accepted = json_request(
        "POST",
        "/api/tenant/invitations/accept",
        user,
        {"token": invitation["token"]},
    )
    assert code == 200, accepted

    fixtures = [
        (
            "format-policy.pdf",
            "application/pdf",
            create_pdf("PDF format worker smoke unique evidence"),
        ),
        (
            "format-policy.docx",
            DOCX_CONTENT_TYPE,
            create_docx("DOCX format worker smoke unique evidence"),
        ),
    ]

    for file_name, content_type, content in fixtures:
        code, uploaded = upload(user, file_name, content_type, content)
        assert code == 202 and isinstance(uploaded, dict), (file_name, code, uploaded)
        assert uploaded["indexingStatus"] == "queued_for_background_processing", uploaded
        wait_for_completion(user, uploaded["processingStatusUrl"])

    code, rejected = upload(
        user,
        "spoofed.pdf",
        "application/pdf",
        b"This is not a PDF despite its extension and declared MIME type.",
    )
    assert code == 400 and isinstance(rejected, dict), rejected
    assert rejected["code"] == "invalid-file-signature", rejected

    code, search = json_request(
        "POST",
        "/api/documents/search",
        user,
        {"query": "format worker smoke unique evidence", "topK": 10},
    )
    assert code == 200 and isinstance(search, dict), search
    file_names = {item["fileName"] for item in search["results"]}
    assert "format-policy.pdf" in file_names, search
    assert "format-policy.docx" in file_names, search

    print("Safe PDF/DOCX Compose smoke test passed.")


if __name__ == "__main__":
    main()
