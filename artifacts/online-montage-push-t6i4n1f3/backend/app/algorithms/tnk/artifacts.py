"""Bounded metadata for handing TNK arrays to the existing ArtifactStore."""

from __future__ import annotations

import hashlib
import json
from typing import Any

import numpy as np
from pydantic import BaseModel, Field


class TnkArtifactDescriptor(BaseModel):
    artifact_kind: str = Field(min_length=1)
    array_names: list[str] = Field(min_length=1)
    shapes: dict[str, list[int]]
    units: dict[str, str]
    arrays_sha256: str = Field(min_length=64, max_length=64)
    environment: dict[str, str]
    source_identity: dict[str, Any]


def describe_arrays(arrays: dict[str, np.ndarray], *, artifact_kind: str,
                    units: dict[str, str], environment: dict[str, str],
                    source_identity: dict[str, Any]) -> TnkArtifactDescriptor:
    """Create replayable, bounded metadata before ``ArtifactStore.write_npz``.

    The digest is over sorted names, little-endian float64 values, and shapes;
    the store then adds its immutable NPZ SHA-256. This descriptor is suitable
    for Run evidence and never embeds the numerical arrays in JSON.
    """
    if not arrays or set(arrays) != set(units):
        raise ValueError("TNK artifact arrays and units must have identical names")
    digest = hashlib.sha256()
    shapes = {}
    for name in sorted(arrays):
        values = np.asarray(arrays[name])
        if not np.issubdtype(values.dtype, np.number) or np.any(np.isinf(values)):
            raise ValueError(f"TNK artifact array {name!r} is not finite numeric data")
        normalized = np.asarray(values, dtype="<f8")
        digest.update(name.encode("utf-8"))
        digest.update(json.dumps(list(normalized.shape), separators=(",", ":")).encode("ascii"))
        digest.update(normalized.tobytes(order="C"))
        shapes[name] = list(normalized.shape)
    return TnkArtifactDescriptor(
        artifact_kind=artifact_kind, array_names=sorted(arrays), shapes=shapes,
        units=dict(units), arrays_sha256=digest.hexdigest(),
        environment=dict(environment), source_identity=dict(source_identity),
    )
