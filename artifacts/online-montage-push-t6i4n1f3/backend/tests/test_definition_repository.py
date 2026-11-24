from pathlib import Path

import pytest

from app.models.algorithm_definition import DefinitionCreateRequest, DefinitionVersionDraft
from app.persistence.repositories.definition import DefinitionRepository


def test_definition_versions_are_digest_deduplicated_and_publish_is_idempotent(tmp_path: Path):
    repository = DefinitionRepository(tmp_path / "definitions.sqlite3")
    definition = repository.create(DefinitionCreateRequest(name="Alpha ratio", description="test"))
    draft = DefinitionVersionDraft(
        semver="1.0.0",
        graph={"nodes": [{"id": "out", "type": "output", "inputs": {"source": "$input.value"}}], "outputs": ["out"]},
        inputs={"value": {"unit": "ratio"}}, outputs={"out": {"unit": "ratio"}},
    )
    created = repository.create_version(definition.definition_id, draft)
    assert created.state == "draft"
    with pytest.raises(ValueError, match="already exists"):
        repository.create_version(definition.definition_id, draft)

    published = repository.publish(definition.definition_id, "1.0.0")
    assert published.state == "published"
    assert repository.publish(definition.definition_id, "1.0.0") == published
