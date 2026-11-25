"""Resolve optional project subjects within the caller's transaction."""

import sqlite3
from uuid import uuid4


class SubjectBindingError(ValueError):
    def __init__(self, code: str, message: str, status_code: int = 422):
        super().__init__(message)
        self.code, self.status_code = code, status_code


def resolve_subject(connection: sqlite3.Connection, project_id: str, created_at: str, *,
                    subject_id: str | None = None, code: str | None = None,
                    name: str | None = None) -> str | None:
    """Bind by explicit identity/code only; an empty pair means unassociated."""
    code, name = (code or '').strip(), (name or '').strip()
    if len(code) > 160 or len(name) > 160:
        raise SubjectBindingError('SUBJECT_METADATA_INVALID', '受试者编号和姓名不能超过 160 个字符。')
    if subject_id is not None:
        if code or name:
            raise SubjectBindingError('SUBJECT_SELECTION_CONFLICT', '请选择已有受试者，或填写受试者信息。')
        row = connection.execute(
            'SELECT subject_id FROM subjects WHERE subject_id = ? AND project_id = ?',
            (subject_id, project_id)).fetchone()
        if row is None:
            raise SubjectBindingError('SUBJECT_PROJECT_MISMATCH', '受试者不属于当前项目。', 409)
        return str(row['subject_id'])
    if not code and not name:
        return None
    if code:
        row = connection.execute(
            'SELECT subject_id, name FROM subjects WHERE project_id = ? AND local_code = ?',
            (project_id, code)).fetchone()
        if row is not None:
            if name and row['name'] != name:
                raise SubjectBindingError('SUBJECT_CODE_CONFLICT', '该编号已关联其他姓名，请选择已有受试者或使用其他编号。', 409)
            return str(row['subject_id'])
    identity = uuid4().hex
    connection.execute(
        'INSERT INTO subjects(subject_id, project_id, local_code, created_at, name) VALUES (?, ?, ?, ?, ?)',
        (identity, project_id, code or None, created_at, name))
    return identity


def edit_session_subject(connection: sqlite3.Connection, project_id: str, current_id: str | None,
                         created_at: str, code: str | None, name: str | None) -> str | None:
    """Edit shared metadata only when the current identity retains its code."""
    code, name = (code or '').strip(), (name or '').strip()
    if len(code) > 160 or len(name) > 160:
        raise SubjectBindingError('SUBJECT_METADATA_INVALID', '受试者编号和姓名不能超过 160 个字符。')
    if current_id and (code or name):
        current = connection.execute(
            'SELECT local_code FROM subjects WHERE subject_id = ? AND project_id = ?',
            (current_id, project_id)).fetchone()
        if current is not None and (current['local_code'] or '') == code:
            connection.execute('UPDATE subjects SET name = ? WHERE subject_id = ?', (name, current_id))
            return current_id
    return resolve_subject(connection, project_id, created_at, code=code, name=name)
