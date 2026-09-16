"""Single registry for current executable algorithms."""

from __future__ import annotations

from dataclasses import dataclass

from .contracts import AlgorithmModule
from .errors import DuplicateAlgorithmError, UnknownAlgorithmError


@dataclass(frozen=True)
class RegistryKey:
    algorithm_id: str
    scientific_version: str


class AlgorithmRegistry:
    def __init__(self) -> None:
        self._modules: dict[RegistryKey, AlgorithmModule] = {}

    def register(self, algorithm: AlgorithmModule) -> None:
        key = RegistryKey(algorithm.manifest.algorithm_id, algorithm.manifest.scientific_version)
        if key in self._modules:
            raise DuplicateAlgorithmError(
                f"algorithm {key.algorithm_id}@{key.scientific_version} is already registered",
                detail={"algorithm_id": key.algorithm_id, "scientific_version": key.scientific_version},
            )
        self._modules[key] = algorithm

    def get(self, algorithm_id: str, scientific_version: str | None = None) -> AlgorithmModule:
        matches = [
            module for key, module in self._modules.items()
            if key.algorithm_id == algorithm_id
            and (scientific_version is None or key.scientific_version == scientific_version)
        ]
        if not matches:
            raise UnknownAlgorithmError(
                f"algorithm {algorithm_id!r} is not registered",
                detail={"algorithm_id": algorithm_id, "scientific_version": scientific_version},
            )
        if scientific_version is None:
            return sorted(matches, key=lambda item: item.manifest.scientific_version)[-1]
        return matches[0]

    def list(self) -> list[AlgorithmModule]:
        return sorted(self._modules.values(), key=lambda item: (item.manifest.algorithm_id, item.manifest.scientific_version))
