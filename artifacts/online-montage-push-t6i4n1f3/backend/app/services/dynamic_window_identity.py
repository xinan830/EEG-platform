"""Resolve published official provenance once, before any window rows exist."""

from app.algorithms.catalog import ensure_official_definitions, official_definition_identity
from app.algorithm_runtime.errors import DynamicWindowIdentityConflictError
from app.core.provenance import implementation_version
from app.algorithm_runtime.offline_execution import official_definition_sha256
from app.services.definitions import DefinitionService


class DynamicWindowIdentityResolver:
    def __init__(self, database_path):
        self.definitions = DefinitionService(database_path)
        ensure_official_definitions(self.definitions)
        self.implementation_build = implementation_version()

    def resolve(self, module, request) -> dict[str, str]:
        definition_id, version, digest = official_definition_identity(
            self.definitions, module.manifest.algorithm_id,
        )
        if (request.definition_id is not None and request.definition_id != definition_id or
                request.definition_version is not None and request.definition_version != version):
            raise DynamicWindowIdentityConflictError(
                "请求的 Definition 与安装的已发布官方算法定义不一致。",
                detail={"definition_id": definition_id, "definition_version": version},
            )
        return {"definition_id": definition_id, "definition_version": version,
                "definition_sha256": official_definition_sha256(
                    definition_id=definition_id, definition_version=version,
                    definition_digest=digest, algorithm_id=module.manifest.algorithm_id,
                    scientific_version=module.manifest.scientific_version,
                    implementation_identity=module.manifest.implementation_identity,
                ), "implementation_build": self.implementation_build}
