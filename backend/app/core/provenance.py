"""Deterministic identities and runtime provenance for scientific results."""

from __future__ import annotations

import hashlib
import json
import os
import platform
import sys
from importlib import metadata
from typing import Any


def canonical_json(value: Any) -> str:
    """Serialize JSON deterministically while rejecting NaN and infinities."""
    return json.dumps(
        value,
        ensure_ascii=True,
        allow_nan=False,
        sort_keys=True,
        separators=(",", ":"),
    )


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def sha256_json(value: Any) -> str:
    return sha256_bytes(canonical_json(value).encode("utf-8"))


def package_version(package: str) -> str:
    try:
        return metadata.version(package)
    except metadata.PackageNotFoundError:
        return "unavailable"


def implementation_version() -> str:
    explicit = os.environ.get("BRAIN_PLATFORM_BUILD_VERSION", "").strip()
    if explicit:
        return explicit
    version = package_version("brain-platform-backend")
    return f"brain-platform-backend@{version}" if version != "unavailable" else "development-unversioned"


def execution_environment() -> dict[str, str]:
    return {
        "python": platform.python_version(),
        "platform": platform.platform(),
        "implementation": platform.python_implementation(),
        "numpy": package_version("numpy"),
        "scipy": package_version("scipy"),
        "mne": package_version("mne"),
        "backend": package_version("brain-platform-backend"),
        "executable": sys.executable,
    }


def build_cache_key(
    *,
    source_sha256: str,
    definition_sha256: str,
    config_sha256: str,
    implementation_build: str,
    actual_range: dict[str, float],
) -> str:
    return sha256_json({
        "cache_contract": "analysis-cache-v1",
        "source_sha256": source_sha256,
        "definition_sha256": definition_sha256,
        "config_sha256": config_sha256,
        "implementation_build": implementation_build,
        "actual_range": actual_range,
    })
