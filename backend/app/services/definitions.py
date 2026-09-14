"""Use cases for safe, versioned algorithm definitions."""

from pathlib import Path

from app.core.config import DATABASE_PATH
from app.eeg_core.definition_engine import DefinitionEngineError, validate_graph, validate_parameters
from app.models.algorithm_definition import AlgorithmDefinition, AlgorithmDefinitionVersion, DefinitionCreateRequest, DefinitionVersionDraft
from app.services.definition_repository import DefinitionRepository


class DefinitionService:
    def __init__(self, database_path: Path = DATABASE_PATH):
        self.repository = DefinitionRepository(database_path)

    def create(self, request: DefinitionCreateRequest) -> AlgorithmDefinition:
        return self.repository.create(request)

    def list(self) -> list[AlgorithmDefinition]:
        return self.repository.list()

    def get(self, definition_id: str) -> AlgorithmDefinition:
        value = self.repository.get(definition_id)
        if value is None:
            raise KeyError("definition not found")
        return value

    def validate(self, draft: DefinitionVersionDraft, parameters: dict[str, object] | None = None) -> dict[str, object]:
        order = validate_graph(draft.graph)
        validate_parameters(draft.parameter_schema, parameters or {})
        return {"valid": True, "node_order": [node.node_id for node in order]}

    def create_version(self, definition_id: str, draft: DefinitionVersionDraft) -> AlgorithmDefinitionVersion:
        self.validate(draft)
        return self.repository.create_version(definition_id, draft)

    def publish(self, definition_id: str, semver: str) -> AlgorithmDefinitionVersion:
        version = self.repository.get_version(definition_id, semver)
        if version is None:
            raise KeyError("definition version not found")
        self.validate(DefinitionVersionDraft(**version.model_dump(exclude={"version_id", "definition_id", "state", "digest_sha256", "created_at", "published_at"})))
        return self.repository.publish(definition_id, semver)

    def compare(self, definition_id: str, left: str, right: str) -> dict[str, object]:
        first, second = self.repository.get_version(definition_id, left), self.repository.get_version(definition_id, right)
        if first is None or second is None:
            raise KeyError("definition version not found")
        return {"left": first, "right": second, "same_digest": first.digest_sha256 == second.digest_sha256,
                "graph_changed": first.graph != second.graph, "parameter_schema_changed": first.parameter_schema != second.parameter_schema}
