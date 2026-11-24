"""Confined, atomic storage for immutable numerical run artifacts."""

from __future__ import annotations

import hashlib
import json
import os
import tempfile
import zipfile
from pathlib import Path
from uuid import uuid4

import numpy as np

from app.core.config import ARTIFACTS_DIR
from app.models.run import RunArtifact
from app.persistence.clock import utc_now
from app.persistence.repositories.run import RunRepository


class ArtifactIntegrityError(RuntimeError):
    pass


class WindowIndexUnavailable(RuntimeError):
    pass


class ArtifactStore:
    MAX_WINDOW_CHUNK_BYTES = 64 * 1024 * 1024
    MAX_AXIS_BYTES = 16 * 1024 * 1024
    MAX_INDEX_BYTES = 4 * 1024 * 1024

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
        window_ranges: list[dict[str, object]] | None = None,
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
            if window_ranges:
                self._write_window_index(artifact, arrays, window_ranges)
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

    def verify(self, artifact: RunArtifact) -> None:
        """Verify the immutable artifact without loading numerical arrays."""
        path = (self.root / artifact.relative_path).resolve()
        self._require_confined(path)
        if not path.is_file() or self._sha256_file(path) != artifact.sha256:
            raise ArtifactIntegrityError("artifact SHA-256 verification failed")

    def read_window(self, artifact: RunArtifact, window_index: int) -> dict[str, np.ndarray]:
        """Read one derived window without loading the canonical NPZ matrix."""
        index = self._read_window_index(artifact)
        try:
            windows = index["windows"]
            if not isinstance(windows, list):
                raise WindowIndexUnavailable("window index rows are malformed")
            if type(window_index) is not int or not 0 <= window_index < len(windows):
                raise IndexError("result window index is outside the artifact")
            entry = windows[window_index]
            if entry["window_index"] != window_index:
                raise WindowIndexUnavailable("result window row identity does not match")
            return self._read_derived_npz(entry["path"], entry["sha256"], self.MAX_WINDOW_CHUNK_BYTES)
        except (KeyError, TypeError) as exc:
            raise WindowIndexUnavailable("window index is malformed") from exc

    def read_window_axes(self, artifact: RunArtifact) -> dict[str, np.ndarray]:
        """Verify the same sidecar identity even when axes are read independently."""
        index = self._read_window_index(artifact)
        try:
            return self._read_derived_npz(index["static_path"], index["static_sha256"], self.MAX_AXIS_BYTES)
        except (KeyError, TypeError) as exc:
            raise WindowIndexUnavailable("window axis index is malformed") from exc

    def _read_window_index(self, artifact: RunArtifact) -> dict[str, object]:
        index_path = self._window_index_path(artifact)
        try:
            if index_path.stat().st_size > self.MAX_INDEX_BYTES:
                raise WindowIndexUnavailable("window index exceeds the resource limit")
            index = json.loads(index_path.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, json.JSONDecodeError) as exc:
            raise WindowIndexUnavailable("window index is missing, unreadable or incomplete") from exc
        if not isinstance(index, dict):
            raise WindowIndexUnavailable("window index is malformed")
        if index.get("format") != "result-window-index-v1" or index.get("artifact_sha256") != artifact.sha256:
            raise WindowIndexUnavailable("window index is stale or incompatible")
        return index

    def _read_derived_npz(self, relative_path: str, sha256: str, max_bytes: int) -> dict[str, np.ndarray]:
        path = (self.root / relative_path).resolve()
        self._require_confined(path)
        try:
            if path.stat().st_size > max_bytes:
                raise WindowIndexUnavailable("derived result exceeds the resource limit")
            if self._sha256_file(path) != sha256:
                raise ArtifactIntegrityError("derived result integrity verification failed")
            with np.load(path, allow_pickle=False) as loaded:
                return {name: np.asarray(loaded[name]) for name in loaded.files}
        except OSError as exc:
            raise WindowIndexUnavailable("derived result is missing or unreadable") from exc
        except (ValueError, EOFError, zipfile.BadZipFile) as exc:
            raise ArtifactIntegrityError("derived result is not a readable NPZ") from exc

    def prepare_window_index(
        self,
        artifact: RunArtifact,
        window_ranges: list[dict[str, object]],
    ) -> None:
        """Build a disposable index for a legacy artifact in a bounded job.

        The preparation may read the canonical artifact once. It is never called
        by the cursor request path, and the immutable artifact is not replaced.
        """
        arrays = self.read_npz(artifact)
        self._write_window_index(artifact, arrays, window_ranges)

    def _write_window_index(
        self,
        artifact: RunArtifact,
        arrays: dict[str, np.ndarray],
        window_ranges: list[dict[str, object]],
    ) -> None:
        window_count = len(window_ranges)
        matrix_names = [name for name, value in arrays.items()
                        if not name.startswith("axis_")
                        if np.asarray(value).ndim >= 1 and np.asarray(value).shape[0] == window_count]
        if not matrix_names:
            return
        static_names = [name for name in arrays if name not in matrix_names]
        artifact_dir = (self.root / artifact.relative_path).resolve().parent
        sidecar_dir = artifact_dir / f"{artifact.artifact_id}.windows"
        sidecar_dir.mkdir(parents=True, exist_ok=True)
        static_path = sidecar_dir / "axes.npz"
        self._atomic_npz(static_path, {name: np.asarray(arrays[name]) for name in static_names})
        entries: list[dict[str, object]] = []
        for index, window in enumerate(window_ranges):
            chunk_path = sidecar_dir / f"{index:08d}.npz"
            self._atomic_npz(chunk_path, {name: np.asarray(arrays[name])[index] for name in matrix_names})
            entries.append({
                "window_index": index,
                "start_sample": window.get("start_sample"),
                "end_sample": window.get("end_sample"),
                "path": chunk_path.relative_to(self.root).as_posix(),
                "sha256": self._sha256_file(chunk_path),
            })
        index = {
            "format": "result-window-index-v1",
            "artifact_sha256": artifact.sha256,
            "static_path": static_path.relative_to(self.root).as_posix(),
            "static_sha256": self._sha256_file(static_path),
            "matrix_names": matrix_names,
            "windows": entries,
        }
        index_path = self._window_index_path(artifact)
        temp_path = index_path.with_suffix(".tmp")
        temp_path.write_text(json.dumps(index, ensure_ascii=True, sort_keys=True), encoding="utf-8")
        os.replace(temp_path, index_path)

    @staticmethod
    def _atomic_npz(path: Path, arrays: dict[str, np.ndarray]) -> None:
        temp_path: Path | None = None
        try:
            with tempfile.NamedTemporaryFile(dir=path.parent, suffix=".tmp", delete=False) as handle:
                temp_path = Path(handle.name)
                np.savez_compressed(handle, **arrays)
                handle.flush()
                os.fsync(handle.fileno())
            os.replace(temp_path, path)
        finally:
            if temp_path is not None:
                temp_path.unlink(missing_ok=True)

    def _window_index_path(self, artifact: RunArtifact) -> Path:
        path = (self.root / artifact.relative_path).resolve()
        self._require_confined(path)
        return path.with_suffix(path.suffix + ".windows.json")

    def discard_cancelled_write(self, artifact: RunArtifact) -> None:
        """Remove only a new, unpublished artifact from a cancelled execution."""
        run = self.repository.get(artifact.run_id)
        if run is None or run.status.value != "cancelled":
            raise ValueError("only cancelled execution writes can be discarded")
        path = (self.root / artifact.relative_path).resolve()
        self._require_confined(path)
        path.unlink(missing_ok=True)
        sidecar_dir = path.parent / f"{artifact.artifact_id}.windows"
        if sidecar_dir.is_dir():
            for child in sidecar_dir.iterdir():
                child.unlink(missing_ok=True)
            sidecar_dir.rmdir()
        self._window_index_path(artifact).unlink(missing_ok=True)
        self.repository.remove_artifact(artifact.artifact_id, artifact.run_id)

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
