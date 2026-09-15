"""SQLite persistence for immutable algorithm definition versions."""

from __future__ import annotations

import json
import sqlite3
from pathlib import Path
from uuid import uuid4

from app.core.config import DATABASE_PATH
from app.core.provenance import sha256_json
from app.models.algorithm_definition import AlgorithmDefinition, AlgorithmDefinitionVersion, DefinitionCreateRequest, DefinitionVersionDraft
from app.persistence import migrate_database
from app.services.run_repository import utc_now


def _json(value: object) -> str:
    return json.dumps(value, ensure_ascii=False, allow_nan=False, sort_keys=True)


class DefinitionRepository:
    def __init__(self, database_path: Path = DATABASE_PATH):
        self.database_path = Path(database_path)
        migrate_database(self.database_path)

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self.database_path, timeout=5.0)
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA foreign_keys = ON")
        return connection

    def create(self, request: DefinitionCreateRequest) -> AlgorithmDefinition:
        now = utc_now()
        definition = AlgorithmDefinition(definition_id=uuid4().hex, name=request.name, owner=request.owner,
                                         status="draft", description=request.description, created_at=now, updated_at=now)
        with self._connect() as connection:
            connection.execute("INSERT INTO algorithm_definitions VALUES (?, ?, ?, ?, ?, ?, ?)", tuple(definition.model_dump().values()))
        return definition

    def get(self, definition_id: str) -> AlgorithmDefinition | None:
        with self._connect() as connection:
            row = connection.execute("SELECT * FROM algorithm_definitions WHERE definition_id = ?", (definition_id,)).fetchone()
        return AlgorithmDefinition(**dict(row)) if row else None

    def list(self) -> list[AlgorithmDefinition]:
        with self._connect() as connection:
            rows = connection.execute("SELECT * FROM algorithm_definitions ORDER BY updated_at DESC").fetchall()
        return [AlgorithmDefinition(**dict(row)) for row in rows]

    def delete(self, definition_id: str) -> bool:
        """Delete a private definition while retaining completed result records.

        Analysis runs and batch rows intentionally have no foreign key to the
        definition table: their persisted output/provenance remains readable
        after a local user removes an algorithm. Queued work cannot execute a
        deleted definition, so it is cancelled in the same transaction.
        """
        with self._connect() as connection:
            definition = connection.execute("SELECT owner FROM algorithm_definitions WHERE definition_id = ?", (definition_id,)).fetchone()
            if definition is None:
                return False
            if definition["owner"] == "platform-official":
                raise PermissionError("official definitions cannot be deleted")
            now = utc_now()
            connection.execute(
                """UPDATE analysis_runs
                    SET status = 'cancelled', cancel_requested = 1, updated_at = ?, completed_at = COALESCE(completed_at, ?)
                    WHERE definition_id = ? AND status = 'queued'""",
                (now, now, definition_id),
            )
            connection.execute(
                """UPDATE analysis_runs
                    SET cancel_requested = 1, updated_at = ?
                    WHERE definition_id = ? AND status = 'running'""",
                (now, definition_id),
            )
            connection.execute("DELETE FROM algorithm_definition_versions WHERE definition_id = ?", (definition_id,))
            connection.execute("DELETE FROM algorithm_definitions WHERE definition_id = ?", (definition_id,))
        return True

    def get_version(self, definition_id: str, semver: str) -> AlgorithmDefinitionVersion | None:
        with self._connect() as connection:
            row = connection.execute("SELECT * FROM algorithm_definition_versions WHERE definition_id = ? AND semver = ?", (definition_id, semver)).fetchone()
        return self._version(row) if row else None

    def list_versions(self, definition_id: str) -> list[AlgorithmDefinitionVersion]:
        with self._connect() as connection:
            rows = connection.execute("SELECT * FROM algorithm_definition_versions WHERE definition_id = ? ORDER BY created_at DESC", (definition_id,)).fetchall()
        return [self._version(row) for row in rows]

    def create_version(self, definition_id: str, draft: DefinitionVersionDraft) -> AlgorithmDefinitionVersion:
        if self.get(definition_id) is None:
            raise KeyError("definition not found")
        payload = draft.model_dump(mode="json")
        digest = sha256_json(payload)
        now = utc_now()
        version = AlgorithmDefinitionVersion(version_id=uuid4().hex, definition_id=definition_id, state="draft",
                                             digest_sha256=digest, created_at=now, **payload)
        with self._connect() as connection:
            try:
                connection.execute(
                    """INSERT INTO algorithm_definition_versions VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
                    (version.version_id, definition_id, version.semver, version.state, _json(version.graph),
                     _json(version.parameter_schema), _json(version.inputs), _json(version.outputs), _json(version.units),
                     _json(version.quality_rules), _json(version.references), digest, now, None),
                )
            except sqlite3.IntegrityError as exc:
                raise ValueError("semver or canonical definition digest already exists") from exc
        return version

    def publish(self, definition_id: str, semver: str) -> AlgorithmDefinitionVersion:
        with self._connect() as connection:
            row = connection.execute("SELECT * FROM algorithm_definition_versions WHERE definition_id = ? AND semver = ?", (definition_id, semver)).fetchone()
            if row is None:
                raise KeyError("definition version not found")
            if row["state"] == "published":
                return self._version(row)
            now = utc_now()
            connection.execute("UPDATE algorithm_definition_versions SET state = 'published', published_at = ? WHERE version_id = ?", (now, row["version_id"]))
            connection.execute("UPDATE algorithm_definitions SET status = 'testing', updated_at = ? WHERE definition_id = ?", (now, definition_id))
            row = connection.execute("SELECT * FROM algorithm_definition_versions WHERE version_id = ?", (row["version_id"],)).fetchone()
        return self._version(row)

    @staticmethod
    def _version(row: sqlite3.Row) -> AlgorithmDefinitionVersion:
        return AlgorithmDefinitionVersion(version_id=row["version_id"], definition_id=row["definition_id"], semver=row["semver"],
            state=row["state"], graph=json.loads(row["graph_json"]), parameter_schema=json.loads(row["parameter_schema_json"]),
            inputs=json.loads(row["inputs_json"]), outputs=json.loads(row["outputs_json"]), units=json.loads(row["units_json"]),
            quality_rules=json.loads(row["quality_rules_json"]), references=json.loads(row["references_json"]),
            digest_sha256=row["digest_sha256"], created_at=row["created_at"], published_at=row["published_at"])
