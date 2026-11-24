"""TNK bridge to the existing immutable ArtifactStore."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any

import numpy as np

from app.algorithms.tnk.artifacts import TnkArtifactDescriptor, describe_arrays
from app.models.run import RunArtifact


@dataclass(frozen=True)
class TnkStoredArtifact:
    artifact: RunArtifact
    descriptor: TnkArtifactDescriptor


def store_tnk_arrays(store: Any, *, run_id: str, arrays: dict[str, np.ndarray],
                      artifact_kind: str, units: dict[str, str],
                      environment: dict[str, str], source_identity: dict[str, Any],
                      window_ranges: list[dict[str, object]] | None = None) -> TnkStoredArtifact:
    """Write large TNK arrays through ArtifactStore and return bounded evidence.

    ``store`` is injected to keep the algorithm package free of persistence
    dependencies. ArtifactStore supplies NPZ atomicity, confinement, and file
    SHA-256; the descriptor supplies array-level identity for replay evidence.
    """
    descriptor = describe_arrays(
        arrays, artifact_kind=artifact_kind, units=units,
        environment=environment, source_identity=source_identity,
    )
    artifact = store.write_npz(
        run_id=run_id, kind=artifact_kind, arrays=arrays,
        unit="mixed" if len(set(units.values())) > 1 else next(iter(units.values())),
        window_ranges=window_ranges,
    )
    return TnkStoredArtifact(artifact=artifact, descriptor=descriptor)
