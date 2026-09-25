"""Confined, atomic storage for immutable numerical run artifacts."""

from __future__ import annotations

import hashlib
import os
import tempfile
from pathlib import Path
from uuid import uuid4

import numpy as np

from app.core.config import ARTIFACTS_DIR
from app.models.run import RunArtifact
from app.persistence.clock import utc_now
from app.persistence.repositories.run import RunRepository


class ArtifactIntegrityError(RuntimeError):
    pass


class ArtifactStore:
    def __init__(self, repository: RunRepository, root: Path = ARTIFACTS_DIR):
        self.repository = repository
        self.root = Path(root).resolve()
        self.root.mkdir(parents=True, exist_ok=True)

    def write_npz(
        self,
        run_id: str,
        kind: str,
        arrays: dict[str, np.ndarray],
        unit: str | None = None,
    ) -> RunArtifact:
        if not arrays:
            raise ValueError("artifact requires at least one array")
        artifact_id = uuid4().hex
        run_dir = (self.root / run_id).resolve()
        self._require_confined(run_dir)
        run_dir.mkdir(parents=True, exist_ok=True)
        final_path = run_dir / f"{artifact_id}.npz"
        temp_path: Path | None = None
        try:
            with tempfile.NamedTemporaryFile(dir=run_dir, suffix=".tmp", delete=False) as handle:
                temp_path = Path(handle.name)
                np.savez_compressed(handle, **{name: np.asarray(value) for name, value in arrays.items()})
                handle.flush()
                os.fsync(handle.fileno())
            os.replace(temp_path, final_path)
            digest = self._sha256_file(final_path)
            relative = final_path.relative_to(self.root).as_posix()
            artifact = RunArtifact(
                artifact_id=artifact_id,
                run_id=run_id,
                kind=kind,
                relative_path=relative,
                media_type="application/x-npz",
                byte_size=final_path.stat().st_size,
                sha256=digest,
                unit=unit,
                shape={name: list(np.asarray(value).shape) for name, value in arrays.items()},
                created_at=utc_now(),
            )
            self.repository.add_artifact(artifact)
            return artifact
        except Exception:
            if temp_path is not None:
                temp_path.unlink(missing_ok=True)
            final_path.unlink(missing_ok=True)
            raise

    def read_npz(self, artifact: RunArtifact) -> dict[str, np.ndarray]:
        path = (self.root / artifact.relative_path).resolve()
        self._require_confined(path)
        if not path.is_file() or self._sha256_file(path) != artifact.sha256:
            raise ArtifactIntegrityError("artifact SHA-256 verification failed")
        with np.load(path, allow_pickle=False) as loaded:
            return {name: np.asarray(loaded[name]) for name in loaded.files}

    def _require_confined(self, path: Path) -> None:
        if path != self.root and self.root not in path.parents:
            raise ValueError("artifact path escapes configured root")

    @staticmethod
    def _sha256_file(path: Path) -> str:
        digest = hashlib.sha256()
        with path.open("rb") as handle:
            for chunk in iter(lambda: handle.read(1024 * 1024), b""):
                digest.update(chunk)
        return digest.hexdigest()
