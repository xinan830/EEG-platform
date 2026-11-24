"""SQLite-backed local research projects without participant PII."""

from __future__ import annotations

import sqlite3
from pathlib import Path
from uuid import uuid4

from app.core.config import DATABASE_PATH
from app.models.research_project import (
    Condition, ConditionCreateRequest, Project, ProjectCreateRequest, Session,
    SessionCreateRequest, Subject, SubjectCreateRequest,
)
from app.persistence import connect_database, migrate_database
from app.persistence.clock import utc_now


class ProjectConflictError(ValueError):
    """Raised for a unique project-scoped research identifier."""


class ProjectService:
    def __init__(self, database_path: Path = DATABASE_PATH):
        self.database_path = Path(database_path)
        migrate_database(self.database_path)

    def _connect(self) -> sqlite3.Connection:
        return connect_database(self.database_path, foreign_keys=True)

    def create(self, request: ProjectCreateRequest) -> Project:
        now = utc_now()
        project = Project(project_id=uuid4().hex, name=request.name.strip(), description=request.description,
                          created_at=now, updated_at=now)
        with self._connect() as connection:
            connection.execute("INSERT INTO projects VALUES (?, ?, ?, ?, ?)", tuple(project.model_dump().values()))
        return project

    def get(self, project_id: str) -> Project | None:
        with self._connect() as connection:
            row = connection.execute("SELECT * FROM projects WHERE project_id = ?", (project_id,)).fetchone()
        return Project(**dict(row)) if row else None

    def list(self) -> list[Project]:
        with self._connect() as connection:
            rows = connection.execute("SELECT * FROM projects ORDER BY updated_at DESC").fetchall()
        return [Project(**dict(row)) for row in rows]

    def create_subject(self, project_id: str, request: SubjectCreateRequest) -> Subject:
        self._require_project(project_id)
        subject = Subject(subject_id=uuid4().hex, project_id=project_id, local_code=request.local_code.strip(), created_at=utc_now())
        try:
            with self._connect() as connection:
                connection.execute("INSERT INTO subjects VALUES (?, ?, ?, ?)", tuple(subject.model_dump().values()))
        except sqlite3.IntegrityError as exc:
            raise ProjectConflictError("subject local code already exists in this project") from exc
        return subject

    def list_subjects(self, project_id: str) -> list[Subject]:
        self._require_project(project_id)
        with self._connect() as connection:
            rows = connection.execute("SELECT * FROM subjects WHERE project_id = ? ORDER BY local_code", (project_id,)).fetchall()
        return [Subject(**dict(row)) for row in rows]

    def create_condition(self, project_id: str, request: ConditionCreateRequest) -> Condition:
        self._require_project(project_id)
        condition = Condition(condition_id=uuid4().hex, project_id=project_id, code=request.code.strip(), label=request.label, created_at=utc_now())
        try:
            with self._connect() as connection:
                connection.execute("INSERT INTO conditions VALUES (?, ?, ?, ?, ?)", tuple(condition.model_dump().values()))
        except sqlite3.IntegrityError as exc:
            raise ProjectConflictError("condition code already exists in this project") from exc
        return condition

    def list_conditions(self, project_id: str) -> list[Condition]:
        self._require_project(project_id)
        with self._connect() as connection:
            rows = connection.execute("SELECT * FROM conditions WHERE project_id = ? ORDER BY code", (project_id,)).fetchall()
        return [Condition(**dict(row)) for row in rows]

    def create_session(self, project_id: str, request: SessionCreateRequest) -> Session:
        self._require_project(project_id)
        self._require_project_value("subjects", "subject_id", request.subject_id, project_id)
        if request.condition_id:
            self._require_project_value("conditions", "condition_id", request.condition_id, project_id)
        self._require_recording(request.recording_id)
        session = Session(session_id=uuid4().hex, project_id=project_id, subject_id=request.subject_id,
                          recording_id=request.recording_id, condition_id=request.condition_id,
                          label=request.label, created_at=utc_now())
        with self._connect() as connection:
            connection.execute("INSERT INTO sessions VALUES (?, ?, ?, ?, ?, ?, ?)", tuple(session.model_dump().values()))
            connection.execute("UPDATE projects SET updated_at = ? WHERE project_id = ?", (session.created_at, project_id))
        return session

    def list_sessions(self, project_id: str) -> list[Session]:
        self._require_project(project_id)
        with self._connect() as connection:
            rows = connection.execute("SELECT * FROM sessions WHERE project_id = ? ORDER BY created_at", (project_id,)).fetchall()
        return [Session(**dict(row)) for row in rows]

    def project_recording_ids(self, project_id: str) -> set[str]:
        self._require_project(project_id)
        with self._connect() as connection:
            rows = connection.execute("SELECT DISTINCT recording_id FROM sessions WHERE project_id = ?", (project_id,)).fetchall()
        return {str(row["recording_id"]) for row in rows}

    def _require_project(self, project_id: str) -> None:
        if self.get(project_id) is None:
            raise KeyError("project not found")

    def _require_project_value(self, table: str, identifier: str, value: str, project_id: str) -> None:
        with self._connect() as connection:
            row = connection.execute(f"SELECT 1 FROM {table} WHERE {identifier} = ? AND project_id = ?", (value, project_id)).fetchone()
        if row is None:
            raise KeyError(f"{identifier} is not available in project")

    def _require_recording(self, recording_id: str) -> None:
        with self._connect() as connection:
            row = connection.execute("SELECT 1 FROM recordings WHERE id = ?", (recording_id,)).fetchone()
        if row is None:
            raise KeyError("recording not found")
