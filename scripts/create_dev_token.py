"""Create a local-development JWT for the document API.

This helper uses only the Python standard library and must not be used as an
identity provider. Override every JWT_* value outside the repository defaults.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import hmac
import json
import os
import time
from typing import Any

DEFAULT_ISSUER = "enterprise-document-assistant-local"
DEFAULT_AUDIENCE = "enterprise-document-assistant-local"
DEFAULT_SIGNING_KEY = "development-only-signing-key-change-before-production-2026"


def encode_segment(value: dict[str, Any]) -> str:
    payload = json.dumps(value, separators=(",", ":"), sort_keys=True).encode("utf-8")
    return base64.urlsafe_b64encode(payload).rstrip(b"=").decode("ascii")


def create_token(user_id: str, role: str, lifetime_seconds: int) -> str:
    now = int(time.time())
    issuer = os.getenv("JWT_ISSUER", DEFAULT_ISSUER)
    audience = os.getenv("JWT_AUDIENCE", DEFAULT_AUDIENCE)
    signing_key = os.getenv("JWT_SIGNING_KEY", DEFAULT_SIGNING_KEY)

    header = encode_segment({"alg": "HS256", "typ": "JWT"})
    payload = encode_segment(
        {
            "sub": user_id,
            "name": user_id,
            "role": role,
            "iss": issuer,
            "aud": audience,
            "iat": now,
            "nbf": now,
            "exp": now + lifetime_seconds,
        }
    )
    unsigned_token = f"{header}.{payload}"
    signature = hmac.new(
        signing_key.encode("utf-8"),
        unsigned_token.encode("ascii"),
        hashlib.sha256,
    ).digest()
    encoded_signature = base64.urlsafe_b64encode(signature).rstrip(b"=").decode("ascii")
    return f"{unsigned_token}.{encoded_signature}"


def main() -> int:
    parser = argparse.ArgumentParser(description="Create a local document API JWT.")
    parser.add_argument("--user", default="demo-user", help="JWT subject claim")
    parser.add_argument("--role", choices=("User", "Admin"), default="User")
    parser.add_argument("--lifetime-seconds", type=int, default=3600)
    args = parser.parse_args()

    if not args.user.strip():
        parser.error("--user must not be blank")
    if args.lifetime_seconds <= 0:
        parser.error("--lifetime-seconds must be greater than zero")

    print(create_token(args.user.strip(), args.role, args.lifetime_seconds))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
