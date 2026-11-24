from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path
from uuid import uuid4

from app.algorithm_runtime.executor import AlgorithmRuntime
from app.algorithm_runtime.registry import AlgorithmRegistry
from app.core.provenance import sha256_json
from app.models.algorithm_preset import AlgorithmPreset, AlgorithmPresetCreateRequest, AlgorithmPresetUpdateRequest
from app.persistence.repositories.algorithm_preset import AlgorithmPresetRepository


class AlgorithmPresetService:
    def __init__(self, registry: AlgorithmRegistry, database_path: Path):
        self.registry = registry
        self.repository = AlgorithmPresetRepository(database_path)

    def _validate(self, algorithm_id: str, scientific_version: str, config: dict) -> None:
        module = self.registry.get(algorithm_id, scientific_version)
        typed = AlgorithmRuntime.validate_config(module=module, config=config)
        AlgorithmRuntime.validate_parameter_schema(module=module, config=typed)

    def create(self, request: AlgorithmPresetCreateRequest) -> AlgorithmPreset:
        self._validate(request.algorithm_id, request.scientific_version, request.config)
        now = datetime.now(timezone.utc).isoformat()
        preset = AlgorithmPreset(
            preset_id=uuid4().hex, algorithm_id=request.algorithm_id,
            scientific_version=request.scientific_version, name=request.name,
            config=request.config, config_sha256=sha256_json(request.config),
            created_at=now, updated_at=now,
        )
        self.repository.create(preset)
        return preset

    def list(self, algorithm_id: str | None = None) -> list[AlgorithmPreset]:
        return self.repository.list(algorithm_id)

    def get(self, preset_id: str) -> AlgorithmPreset:
        preset = self.repository.get(preset_id)
        if preset is None:
            raise KeyError("preset not found")
        return preset

    def update(self, preset_id: str, request: AlgorithmPresetUpdateRequest) -> AlgorithmPreset:
        current = self.get(preset_id)
        self._validate(current.algorithm_id, current.scientific_version, request.config)
        updated = current.model_copy(update={
            "name": request.name, "config": request.config,
            "config_sha256": sha256_json(request.config),
            "updated_at": datetime.now(timezone.utc).isoformat(),
        })
        self.repository.update(updated)
        return updated

    def delete(self, preset_id: str) -> None:
        if not self.repository.delete(preset_id):
            raise KeyError("preset not found")
