"""Bounded immutable payload storage, independent of algorithm scheduling."""

from __future__ import annotations

import hashlib
import json
import logging
import os
import threading
import sys
from collections import OrderedDict
from copy import deepcopy
from time import perf_counter
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
    MAX_CACHE_BYTES = 32 * 1024 * 1024
    MAX_CACHE_ENTRIES = 32
    # shortcut: one backend process owns this directory; use shared accounting before multi-worker hosting.
    _usage_lock = threading.RLock()
    _directory_usage: dict[Path, int] = {}

    def __init__(self, directory: Path, repository: DynamicWindowRepository):
        self.directory = directory.resolve()
        self.repository = repository
        self.logger = logging.getLogger("brain_platform.dynamic_window_artifacts")
        self._read_lock = threading.RLock()
        self._decode_locks = tuple(threading.Lock() for _ in range(16))
        self._read_cache = OrderedDict()
        self._cache_bytes = 0
        self._cache_closed = False
        self.directory.mkdir(parents=True, exist_ok=True)
        with self._usage_lock:
            if self.directory not in self._directory_usage:
                self._directory_usage[self.directory] = self._scan_directory_bytes()

    def _scan_directory_bytes(self) -> int:
        with os.scandir(self.directory) as entries:
            return sum(entry.stat().st_size for entry in entries if entry.is_file())

    def _track_file(self, path: Path) -> None:
        size = path.stat().st_size
        with self._usage_lock:
            self._directory_usage[self.directory] += size

    def _unlink_file(self, path: Path) -> None:
        try:
            size = path.stat().st_size
        except FileNotFoundError:
            return
        try:
            path.unlink()
        except FileNotFoundError:
            return
        with self._usage_lock:
            self._directory_usage[self.directory] -= size

    def cleanup(self) -> None:
        # Do not touch another process's in-flight publication. Recovery will
        # clean orphans after all outstanding leases have expired/finished.
        if self.repository.has_running_attempts():
            return
        for path in (*self.directory.glob("*.tmp.npz"), *self.directory.glob("*.tmp.json")):
            self._unlink_file(path)
        referenced = self.repository.published_artifact_ids()
        stems = {path.stem for path in self.directory.glob("*.npz")} | {path.stem for path in self.directory.glob("*.json")}
        for artifact_id in stems - referenced:
            self.remove_unreferenced(artifact_id)

    def write(self, window_id: str, result: DynamicWindowResult, *, projection_fields: tuple[str, ...] = ()) -> str:
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
            if not set(projection_fields) <= (result.axes.keys() | result.arrays.keys()):
                raise ValueError("projection fields must belong to the saved window")
            metadata["projection_fields"] = list(projection_fields)
            with metadata_temp.open("w", encoding="utf-8") as stream:
                json.dump(metadata, stream, ensure_ascii=False, allow_nan=False)
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temporary, target)
            self._track_file(target)
            os.replace(metadata_temp, metadata_path)
            self._track_file(metadata_path)
            # Verify the actual bytes that the repository will later reference.
            self.read(artifact_id, manifest_sha256=self.manifest_digest(artifact_id),
                      include_projection_source=True)
            return artifact_id
        except Exception:
            self.remove_unreferenced(artifact_id)
            raise
        finally:
            for path in (temporary, metadata_temp):
                path.unlink(missing_ok=True)

    def check_admission_budget(self, directory_budget: int, max_active: int) -> None:
        with self._usage_lock:
            used = self._directory_usage[self.directory]
        reserved = max_active * (self.MAX_PAYLOAD_BYTES + self.MAX_METADATA_BYTES)
        if used + reserved > directory_budget:
            raise DynamicWindowCapacityExceededError("动态结果存储空间已达到预算；已有结果不会被删除。",
                detail={"scope": "artifact_directory", "used_bytes": used,
                        "reserved_bytes": reserved, "limit_bytes": directory_budget})

    def manifest_digest(self, artifact_id: str) -> str:
        return hashlib.sha256((self.directory / f"{artifact_id}.json").read_bytes()).hexdigest()

    def clear_cache(self) -> None:
        with self._read_lock:
            self._read_cache.clear()
            self._cache_bytes = 0

    def close(self) -> None:
        with self._read_lock:
            self._cache_closed = True
            self.clear_cache()

    @staticmethod
    def _file_signature(path: Path) -> tuple:
        value = path.stat()
        return value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns, value.st_ctime_ns

    def _source_signature(self, path: Path, payload_path: Path) -> tuple:
        metadata, payload = self._file_signature(path), self._file_signature(payload_path)
        if metadata[2] > self.MAX_METADATA_BYTES or payload[2] > self.MAX_PAYLOAD_BYTES:
            raise ValueError("dynamic window artifact exceeds the single-window budget")
        # Keep content verification on warm reads, including timestamp-preserving corruption.
        return (metadata, payload, hashlib.sha256(path.read_bytes()).digest(),
                hashlib.sha256(payload_path.read_bytes()).digest())

    def read(self, artifact_id: str, *, manifest_sha256: str | None = None,
             include_projection_source: bool = False) -> dict:
        path = self.directory / f"{artifact_id}.json"
        payload_path = path.with_suffix(".npz")
        # Reuse decoding, never bypass artifact-content or repository-digest verification.
        with self._decode_locks[hash(artifact_id) % len(self._decode_locks)]:
            try:
                signature = self._source_signature(path, payload_path)
                key = artifact_id, manifest_sha256, include_projection_source
                with self._read_lock:
                    cached = self._read_cache.get(key)
                    if cached is not None and cached[0] == signature:
                        self._read_cache.move_to_end(key)
                    else:
                        cached = None
                        for old_key in list(self._read_cache):
                            if old_key[0] == artifact_id and (self._read_cache[old_key][0] != signature or
                                                             old_key[1] != manifest_sha256):
                                self._cache_bytes -= self._read_cache.pop(old_key)[2]
                if cached is not None:
                    result = cached[1]
                else:
                    result = self._decode(artifact_id, manifest_sha256=manifest_sha256,
                                          include_projection_source=include_projection_source)
                    if signature != self._source_signature(path, payload_path):
                        raise ValueError("dynamic window artifact changed during read")
                    size = _memory_bytes(result)
                    with self._read_lock:
                        if not self._cache_closed and size <= self.MAX_CACHE_BYTES:
                            while self._read_cache and (self._cache_bytes + size > self.MAX_CACHE_BYTES or
                                                       len(self._read_cache) >= self.MAX_CACHE_ENTRIES):
                                self._cache_bytes -= self._read_cache.popitem(last=False)[1][2]
                            self._read_cache[key] = signature, result, size
                            self._cache_bytes += size
                value = deepcopy(result)
                if not include_projection_source:
                    for field in ("axes", "arrays"):
                        value[field] = {name: array.tolist() for name, array in value[field].items()}
                return value
            except OSError as exc:
                self.clear_cache()
                raise ValueError(f"dynamic window artifact is invalid: {exc}") from exc

    def _decode(self, artifact_id: str, *, manifest_sha256: str | None,
                include_projection_source: bool) -> dict:
        started = perf_counter()
        path = self.directory / f"{artifact_id}.json"
        payload_path = path.with_suffix(".npz")
        try:
            if path.stat().st_size > self.MAX_METADATA_BYTES or payload_path.stat().st_size > self.MAX_PAYLOAD_BYTES:
                raise ValueError("dynamic window artifact exceeds the single-window budget")
            metadata_started = perf_counter()
            content = path.read_bytes()
            metadata = json.loads(content)
            projection_fields = metadata.pop("projection_fields", [])
            if (not isinstance(projection_fields, list) or
                any(not isinstance(name, str) for name in projection_fields) or
                not set(projection_fields) <= (metadata.get("axis_shapes", {}).keys() | metadata.get("array_shapes", {}).keys())):
                raise ValueError("dynamic window projection schema is invalid")
            excluded = set() if include_projection_source else set(projection_fields)
            metadata_read_ms = (perf_counter() - metadata_started) * 1000
            checksum_started = perf_counter()
            if manifest_sha256 and hashlib.sha256(content).hexdigest() != manifest_sha256:
                raise ValueError("dynamic window metadata checksum mismatch")
            if metadata.get("sha256") != hashlib.sha256(payload_path.read_bytes()).hexdigest():
                raise ValueError("dynamic window result artifact checksum mismatch")
            checksum_ms = (perf_counter() - checksum_started) * 1000
            decode_started = perf_counter()
            axes, arrays = {}, {}
            with zipfile.ZipFile(payload_path) as archive:
                members = archive.infolist()
                if len(members) > 64 or sum(item.file_size for item in members) > self.MAX_PAYLOAD_BYTES:
                    raise ValueError("dynamic window expanded payload exceeds the single-window budget")
            with np.load(payload_path, allow_pickle=False) as payload:
                expected = ({f"axis_{name}" for name in metadata.get("axis_shapes", {})} |
                            {f"array_{name}" for name in metadata.get("array_shapes", {})})
                if set(payload.files) != expected:
                    raise ValueError("dynamic window fields do not match the saved schema")
                excluded_keys = {f"axis_{name}" for name in excluded} | {f"array_{name}" for name in excluded}
                for key in payload.files:
                    if key in excluded_keys:
                        continue
                    value = np.asarray(payload[key])
                    if key.startswith("axis_"):
                        name = key.removeprefix("axis_")
                        if metadata.get("axis_shapes", {}).get(name) != list(value.shape):
                            raise ValueError("dynamic window axis shape does not match its schema")
                        axes[name] = value
                    elif key.startswith("array_"):
                        name = key.removeprefix("array_")
                        if metadata.get("array_shapes", {}).get(name) != list(value.shape):
                            raise ValueError("dynamic window array shape does not match its schema")
                        arrays[name] = value
                    else:
                        raise ValueError("dynamic window artifact contains an unknown field")
            decode_ms = (perf_counter() - decode_started) * 1000
            metadata["axes"], metadata["arrays"] = axes, arrays
            for field in ("axis_units", "array_units", "axis_shapes", "array_shapes"):
                metadata[field] = {name: value for name, value in metadata[field].items() if name not in excluded}
            validation_started = perf_counter()
            DynamicWindowResult.model_validate(metadata)
            validation_ms = (perf_counter() - validation_started) * 1000
            self.logger.info(
                "dynamic_artifact_read_perf artifact_id=%s metadata_read_ms=%.1f checksum_ms=%.1f "
                "decode_ms=%.1f validation_ms=%.1f total_ms=%.1f",
                artifact_id, metadata_read_ms, checksum_ms, decode_ms, validation_ms,
                (perf_counter() - started) * 1000,
            )
            return metadata
        except (OSError, TypeError, KeyError, ValueError, zipfile.BadZipFile) as exc:
            raise ValueError(f"dynamic window artifact is invalid: {exc}") from exc

    def remove_unreferenced(self, artifact_id: str) -> None:
        if artifact_id in self.repository.published_artifact_ids():
            return
        with self._read_lock:
            for key in list(self._read_cache):
                if key[0] == artifact_id:
                    self._cache_bytes -= self._read_cache.pop(key)[2]
        for suffix in (".npz", ".json"):
            try:
                self._unlink_file(self.directory / f"{artifact_id}{suffix}")
            except OSError:
                self.logger.warning("dynamic_artifact_cleanup_failed artifact_id=%s suffix=%s", artifact_id, suffix)


def _memory_bytes(value) -> int:
    size = sys.getsizeof(value)
    if isinstance(value, np.ndarray):
        return size + (0 if value.flags.owndata else value.nbytes)
    if isinstance(value, dict):
        return size + sum(_memory_bytes(key) + _memory_bytes(item) for key, item in value.items())
    if isinstance(value, (list, tuple)):
        return size + sum(_memory_bytes(item) for item in value)
    return size
