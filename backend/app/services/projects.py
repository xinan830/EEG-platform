"""SQLite-backed local research projects and optional subject associations."""

from __future__ import annotations

import sqlite3
from pathlib import Path
from uuid import uuid4

from app.core.config import DATABASE_PATH
from app.models.research_project import (
    Condition, ConditionCreateRequest, Project, ProjectCreateRequest, Session,
    SessionCreateRequest, Subject, SubjectCreateRequest, SubjectDeletionResponse, SubjectResolutionResponse,
)
from app.persistence import connect_database, migrate_database
from app.persistence.clock import utc_now
from app.services.research_subjects import edit_session_subject, resolve_subject, validate_recording_age


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

    def ensure_desktop_project(self, project_id: str, name: str, description: str = "") -> Project:
        """Resolve the stable desktop project ID without creating duplicate rows."""
        normalized_name = name.strip()
        if not normalized_name:
            raise ValueError("project name is required")
        existing = self.get(project_id)
        if existing is not None:
            return existing
        now = utc_now()
        project = Project(project_id=project_id, name=normalized_name, description=description,
                          created_at=now, updated_at=now)
        with self._connect() as connection:
            connection.execute("INSERT OR IGNORE INTO projects VALUES (?, ?, ?, ?, ?)",
                               tuple(project.model_dump().values()))
            row = connection.execute("SELECT * FROM projects WHERE project_id = ?", (project_id,)).fetchone()
        if row is None:
            raise RuntimeError("desktop project resolution failed")
        return Project(**dict(row))

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
        subject = Subject(subject_id=uuid4().hex, project_id=project_id,
                          local_code=(request.local_code or "").strip() or None, name=request.name.strip(),
                          sex=request.sex, created_at=utc_now())
        if not subject.local_code and not subject.name:
            raise ValueError("受试者编号和姓名至少填写一项。")
        try:
            with self._connect() as connection:
                connection.execute("INSERT INTO subjects(subject_id, project_id, local_code, created_at, name, sex) VALUES (?, ?, ?, ?, ?, ?)",
                                   (subject.subject_id, project_id, subject.local_code, subject.created_at, subject.name, subject.sex))
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

    def resolve_project_subject(self, project_id: str, request: SubjectCreateRequest) -> SubjectResolutionResponse:
        """Resolve optional metadata through the same identity rules used by imports."""
        self._require_project(project_id)
        with self._connect() as connection:
            identity = resolve_subject(connection, project_id, utc_now(), code=request.local_code, name=request.name, sex=request.sex)
            row = connection.execute("SELECT * FROM subjects WHERE subject_id = ?", (identity,)).fetchone()
        return SubjectResolutionResponse(subject=Subject(**dict(row)) if row else None)

    def update_subject(self, project_id: str, subject_id: str, request: SubjectCreateRequest) -> Subject:
        """Keep identity and all recording associations stable when metadata changes."""
        code, name = (request.local_code or "").strip() or None, request.name.strip()
        if not code and not name:
            raise ValueError("受试者编号和姓名至少填写一项。")
        try:
            with self._connect() as connection:
                result = connection.execute(
                    "UPDATE subjects SET local_code = ?, name = ? WHERE subject_id = ? AND project_id = ?",
                    (code, name, subject_id, project_id))
                if result.rowcount != 1:
                    raise KeyError("subject not found")
                if "sex" in request.model_fields_set:
                    connection.execute("UPDATE subjects SET sex = ? WHERE subject_id = ?", (request.sex, subject_id))
                row = connection.execute("SELECT * FROM subjects WHERE subject_id = ?", (subject_id,)).fetchone()
        except sqlite3.IntegrityError as exc:
            raise ProjectConflictError("subject local code already exists in this project") from exc
        return Subject(**dict(row))

    def delete_subject(self, project_id: str, subject_id: str) -> SubjectDeletionResponse:
        """Detach associations transactionally; recordings and samples remain untouched."""
        with self._connect() as connection:
            row = connection.execute(
                "SELECT 1 FROM subjects WHERE subject_id = ? AND project_id = ?", (subject_id, project_id)).fetchone()
            if row is None:
                raise KeyError("subject not found")
            detached = connection.execute(
                "UPDATE sessions SET subject_id = NULL WHERE subject_id = ? AND project_id = ?",
                (subject_id, project_id)).rowcount
            connection.execute("DELETE FROM subjects WHERE subject_id = ? AND project_id = ?", (subject_id, project_id))
        return SubjectDeletionResponse(subject_id=subject_id, detached_sessions=detached)

    def list_conditions(self, project_id: str) -> list[Condition]:
        self._require_project(project_id)
        with self._connect() as connection:
            rows = connection.execute("SELECT * FROM conditions WHERE project_id = ? ORDER BY code", (project_id,)).fetchall()
        return [Condition(**dict(row)) for row in rows]

    def create_session(self, project_id: str, request: SessionCreateRequest) -> Session:
        self._require_project(project_id)
        if request.subject_id is not None:
            self._require_project_value("subjects", "subject_id", request.subject_id, project_id)
        if request.condition_id:
            self._require_project_value("conditions", "condition_id", request.condition_id, project_id)
        self._require_recording(request.recording_id)
        session = Session(session_id=uuid4().hex, project_id=project_id, subject_id=request.subject_id,
                          recording_id=request.recording_id, condition_id=request.condition_id,
                          label=request.label, created_at=utc_now(), age_at_recording_years=request.age_at_recording_years)
        with self._connect() as connection:
            connection.execute("INSERT INTO sessions(session_id, project_id, subject_id, recording_id, condition_id, label, created_at, age_at_recording_years) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                               (session.session_id, project_id, session.subject_id, session.recording_id,
                                session.condition_id, session.label, session.created_at, session.age_at_recording_years))
            connection.execute("UPDATE projects SET updated_at = ? WHERE project_id = ?", (session.created_at, project_id))
        return session

    def list_sessions(self, project_id: str) -> list[Session]:
        self._require_project(project_id)
        with self._connect() as connection:
            rows = connection.execute("""SELECT s.*, u.local_code AS subject_code, COALESCE(u.name, '') AS subject_name, u.sex AS subject_sex
                FROM sessions s LEFT JOIN subjects u ON u.subject_id = s.subject_id
                WHERE s.project_id = ? ORDER BY s.created_at""", (project_id,)).fetchall()
        return [Session(**dict(row)) for row in rows]

    def project_recording_ids(self, project_id: str) -> set[str]:
        self._require_project(project_id)
        with self._connect() as connection:
            rows = connection.execute("SELECT DISTINCT recording_id FROM sessions WHERE project_id = ?", (project_id,)).fetchall()
        return {str(row["recording_id"]) for row in rows}

    def rename_session(self, project_id: str, session_id: str, label: str, *,
                       subject_metadata: tuple[str | None, str | None] | None = None,
                       subject_binding: tuple[str | None] | None = None,
                       recording_age: tuple[int | None] | None = None) -> Session:
        """Rename a project recording without mutating its immutable source."""
        normalized = label.strip()
        if not normalized or len(normalized) > 4000:
            raise ValueError("recording name must contain 1 to 4000 characters")
        if recording_age is not None:
            validate_recording_age(recording_age[0])
        with self._connect() as connection:
            current = connection.execute(
                "SELECT subject_id FROM sessions WHERE session_id = ? AND project_id = ?",
                (session_id, project_id)).fetchone()
            if current is None:
                raise KeyError("recording session not found")
            if recording_age is not None:
                connection.execute("UPDATE sessions SET age_at_recording_years = ? WHERE session_id = ?",
                                   (recording_age[0], session_id))
            if subject_binding is not None:
                identity = resolve_subject(connection, project_id, utc_now(), subject_id=subject_binding[0])
                connection.execute("UPDATE sessions SET subject_id = ? WHERE session_id = ?", (identity, session_id))
            if subject_metadata is not None:
                subject_id = edit_session_subject(connection, project_id, current["subject_id"],
                                                  utc_now(), *subject_metadata)
                connection.execute("UPDATE sessions SET subject_id = ? WHERE session_id = ?", (subject_id, session_id))
            result = connection.execute(
                "UPDATE sessions SET label = ? WHERE session_id = ? AND project_id = ?",
                (normalized, session_id, project_id),
            )
            if result.rowcount != 1:
                raise KeyError("recording session not found")
            connection.execute("UPDATE projects SET updated_at = ? WHERE project_id = ?", (utc_now(), project_id))
            row = connection.execute(
                """SELECT s.*, u.local_code AS subject_code, COALESCE(u.name, '') AS subject_name, u.sex AS subject_sex
                FROM sessions s LEFT JOIN subjects u ON u.subject_id = s.subject_id
                WHERE s.session_id = ? AND s.project_id = ?""", (session_id, project_id),
            ).fetchone()
        return Session(**dict(row))

    def delete_imported_recording(self, project_id: str, session_id: str) -> str:
        """Remove an imported EDF/BDF recording from a project by deleting its Session link."""
        self._require_project(project_id)
        with self._connect() as connection:
            row = connection.execute(
                """SELECT s.recording_id, r.extension FROM sessions s
                   JOIN recordings r ON r.id = s.recording_id
                   WHERE s.session_id = ? AND s.project_id = ?""",
                (session_id, project_id),
            ).fetchone()
            if row is None:
                raise KeyError("recording session not found")
            if str(row["extension"]).casefold() not in {".edf", ".bdf"}:
                raise ValueError("only imported EDF/BDF recordings can be deleted here")
            connection.execute("DELETE FROM sessions WHERE session_id = ? AND project_id = ?", (session_id, project_id))
            connection.execute("UPDATE projects SET updated_at = ? WHERE project_id = ?", (utc_now(), project_id))
        return str(row["recording_id"])

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
