"""Bounded immutable payload storage, independent of algorithm scheduling."""

from __future__ import annotations

import hashlib
import json
import logging
import os
import zipfile
from pathlib import Path
from uuid import uuid4

import numpy as np

from app.algorithm_runtime.on_demand import DynamicWindowResult
from app.persistence.repositories.dynamic_window import DynamicWindowRepository
from app.algorithm_runtime.errors import DynamicWindowCapacityExceededError


class DynamicWindowArtifactStore:
    MAX_PAYLOAD_BYTES = 16 * 1024 * 1024
    MAX_METADATA_BYTES = 2 * 1024 * 1024

    def __init__(self, directory: Path, repository: DynamicWindowRepository):
        self.directory = directory
        self.repository = repository
        self.logger = logging.getLogger(__name__)
        directory.mkdir(parents=True, exist_ok=True)

    def cleanup(self) -> None:
        # Do not touch another process's in-flight publication. Recovery will
        # clean orphans after all outstanding leases have expired/finished.
        if self.repository.has_running_attempts():
            return
        for path in (*self.directory.glob("*.tmp.npz"), *self.directory.glob("*.tmp.json")):
            path.unlink(missing_ok=True)
        referenced = self.repository.published_artifact_ids()
        stems = {path.stem for path in self.directory.glob("*.npz")} | {path.stem for path in self.directory.glob("*.json")}
        for artifact_id in stems - referenced:
            self.remove_unreferenced(artifact_id)

    def write(self, window_id: str, result: DynamicWindowResult) -> str:
        artifact_id = f"{window_id}-{uuid4().hex}"
        target = self.directory / f"{artifact_id}.npz"
        temporary = target.with_suffix(".tmp.npz")
        metadata_path = target.with_suffix(".json")
        metadata_temp = metadata_path.with_suffix(".tmp.json")
        try:
            arrays = {f"axis_{key}": np.asarray(value) for key, value in result.axes.items()}
            arrays.update({f"array_{key}": np.asarray(value) for key, value in result.arrays.items()})
            if sum(value.nbytes for value in arrays.values()) > self.MAX_PAYLOAD_BYTES:
                raise ValueError("dynamic window numeric payload exceeds the single-window budget")
            with temporary.open("wb") as stream:
                np.savez_compressed(stream, **arrays)
                stream.flush()
                os.fsync(stream.fileno())
            metadata = result.model_dump(exclude={"axes", "arrays"}, mode="json")
            metadata["sha256"] = hashlib.sha256(temporary.read_bytes()).hexdigest()
            metadata["axis_shapes"] = {key: list(np.asarray(value).shape) for key, value in result.axes.items()}
            metadata["array_shapes"] = {key: list(np.asarray(value).shape) for key, value in result.arrays.items()}
            with metadata_temp.open("w", encoding="utf-8") as stream:
                json.dump(metadata, stream, ensure_ascii=False, allow_nan=False)
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temporary, target)
            os.replace(metadata_temp, metadata_path)
            # Verify the actual bytes that the repository will later reference.
            self.read(artifact_id)
            return artifact_id
        except Exception:
            self.remove_unreferenced(artifact_id)
            raise
        finally:
            for path in (temporary, metadata_temp):
                path.unlink(missing_ok=True)

    def check_admission_budget(self, directory_budget: int, max_active: int) -> None:
        used = 0
        for path in self.directory.iterdir():
            try:
                if path.is_file():
                    used += path.stat().st_size
            except FileNotFoundError:
                continue  # Another worker cleaned an unpublished file.
        reserved = max_active * (self.MAX_PAYLOAD_BYTES + self.MAX_METADATA_BYTES)
        if used + reserved > directory_budget:
            raise DynamicWindowCapacityExceededError("动态结果存储空间已达到预算；已有结果不会被删除。",
                detail={"scope": "artifact_directory", "used_bytes": used,
                        "reserved_bytes": reserved, "limit_bytes": directory_budget})

    def manifest_digest(self, artifact_id: str) -> str:
        return hashlib.sha256((self.directory / f"{artifact_id}.json").read_bytes()).hexdigest()

    def read(self, artifact_id: str, *, manifest_sha256: str | None = None) -> dict:
        path = self.directory / f"{artifact_id}.json"
        payload_path = path.with_suffix(".npz")
        try:
            if path.stat().st_size > self.MAX_METADATA_BYTES or payload_path.stat().st_size > self.MAX_PAYLOAD_BYTES:
                raise ValueError("dynamic window artifact exceeds the single-window budget")
            content = path.read_bytes()
            if manifest_sha256 and hashlib.sha256(content).hexdigest() != manifest_sha256:
                raise ValueError("dynamic window metadata checksum mismatch")
            metadata = json.loads(content)
            if metadata.get("sha256") != hashlib.sha256(payload_path.read_bytes()).hexdigest():
                raise ValueError("dynamic window result artifact checksum mismatch")
            axes, arrays = {}, {}
            with zipfile.ZipFile(payload_path) as archive:
                members = archive.infolist()
                if len(members) > 64 or sum(item.file_size for item in members) > self.MAX_PAYLOAD_BYTES:
                    raise ValueError("dynamic window expanded payload exceeds the single-window budget")
            with np.load(payload_path, allow_pickle=False) as payload:
                for key in payload.files:
                    value = np.asarray(payload[key])
                    if key.startswith("axis_"):
                        name = key.removeprefix("axis_")
                        if metadata.get("axis_shapes", {}).get(name) != list(value.shape):
                            raise ValueError("dynamic window axis shape does not match its schema")
                        axes[name] = value.tolist()
                    elif key.startswith("array_"):
                        name = key.removeprefix("array_")
                        if metadata.get("array_shapes", {}).get(name) != list(value.shape):
                            raise ValueError("dynamic window array shape does not match its schema")
                        arrays[name] = value.tolist()
                    else:
                        raise ValueError("dynamic window artifact contains an unknown field")
            metadata["axes"], metadata["arrays"] = axes, arrays
            DynamicWindowResult.model_validate(metadata)
            return metadata
        except (OSError, TypeError, KeyError, ValueError, zipfile.BadZipFile) as exc:
            raise ValueError(f"dynamic window artifact is invalid: {exc}") from exc

    def remove_unreferenced(self, artifact_id: str) -> None:
        if artifact_id in self.repository.published_artifact_ids():
            return
        for suffix in (".npz", ".json"):
            try:
                (self.directory / f"{artifact_id}{suffix}").unlink(missing_ok=True)
            except OSError:
                self.logger.warning("dynamic_artifact_cleanup_failed artifact_id=%s suffix=%s", artifact_id, suffix)
