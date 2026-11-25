"""Preserve research identities while making subject association optional."""

import sqlite3


def migration_026_subject_demographics(connection: sqlite3.Connection) -> None:
    """Append management metadata; existing people and recording ages stay unknown."""
    connection.execute("ALTER TABLE subjects ADD COLUMN sex TEXT CHECK(sex IN ('male', 'female', 'other'))")
    connection.execute("""ALTER TABLE sessions ADD COLUMN age_at_recording_years INTEGER
        CHECK(age_at_recording_years IS NULL OR
              (typeof(age_at_recording_years) = 'integer' AND age_at_recording_years BETWEEN 0 AND 150))""")


def migration_025_optional_subjects(connection: sqlite3.Connection) -> None:
    connection.execute("""CREATE TABLE subjects_new (
        subject_id TEXT PRIMARY KEY, project_id TEXT NOT NULL, local_code TEXT,
        created_at TEXT NOT NULL, name TEXT NOT NULL DEFAULT '',
        UNIQUE(project_id, local_code),
        FOREIGN KEY(project_id) REFERENCES projects(project_id))""")
    connection.execute("""INSERT INTO subjects_new
        SELECT subject_id, project_id, local_code, created_at, '' FROM subjects""")
    connection.execute("""CREATE TABLE sessions_new (
        session_id TEXT PRIMARY KEY, project_id TEXT NOT NULL, subject_id TEXT,
        recording_id TEXT NOT NULL, condition_id TEXT, label TEXT NOT NULL, created_at TEXT NOT NULL,
        FOREIGN KEY(project_id) REFERENCES projects(project_id),
        FOREIGN KEY(subject_id) REFERENCES subjects_new(subject_id),
        FOREIGN KEY(condition_id) REFERENCES conditions(condition_id),
        FOREIGN KEY(recording_id) REFERENCES recordings(id))""")
    connection.execute("INSERT INTO sessions_new SELECT * FROM sessions")
    connection.execute("DROP TABLE sessions")
    connection.execute("DROP TABLE subjects")
    connection.execute("ALTER TABLE subjects_new RENAME TO subjects")
    connection.execute("ALTER TABLE sessions_new RENAME TO sessions")
    connection.execute("CREATE INDEX idx_subjects_project ON subjects(project_id, local_code)")
    connection.execute("CREATE INDEX idx_sessions_project ON sessions(project_id, created_at)")
    connection.execute("CREATE INDEX idx_sessions_recording ON sessions(recording_id)")
