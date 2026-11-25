"""Resolve optional project subjects within the caller's transaction."""

import sqlite3
from uuid import uuid4


class SubjectBindingError(ValueError):
    def __init__(self, code: str, message: str, status_code: int = 422):
        super().__init__(message)
        self.code, self.status_code = code, status_code


def validate_recording_age(value: int | None) -> None:
    """Age is optional whole years at recording time, never inferred from today."""
    if value is not None and (type(value) is not int or not 0 <= value <= 150):
        raise SubjectBindingError('RECORDING_AGE_INVALID', '采集时年龄须为 0–150 之间的整数，或留空。')


def resolve_subject(connection: sqlite3.Connection, project_id: str, created_at: str, *,
                    subject_id: str | None = None, code: str | None = None,
                    name: str | None = None, sex: str | None = None) -> str | None:
    """Bind by explicit identity/code only; an empty pair means unassociated."""
    code, name = (code or '').strip(), (name or '').strip()
    if sex not in {None, 'male', 'female', 'other'}:
        raise SubjectBindingError('SUBJECT_SEX_INVALID', '请选择有效的性别，或留空。')
    if len(code) > 160 or len(name) > 160:
        raise SubjectBindingError('SUBJECT_METADATA_INVALID', '受试者编号和姓名不能超过 160 个字符。')
    if subject_id is not None:
        if code or name or sex is not None:
            raise SubjectBindingError('SUBJECT_SELECTION_CONFLICT', '请选择已有受试者，或填写受试者信息。')
        row = connection.execute(
            'SELECT subject_id FROM subjects WHERE subject_id = ? AND project_id = ?',
            (subject_id, project_id)).fetchone()
        if row is None:
            raise SubjectBindingError('SUBJECT_PROJECT_MISMATCH', '受试者不属于当前项目。', 409)
        return str(row['subject_id'])
    if not code and not name:
        if sex is not None:
            raise SubjectBindingError('SUBJECT_METADATA_INVALID', '填写性别时请同时填写受试者编号或姓名。')
        return None
    if code:
        row = connection.execute(
            'SELECT subject_id, name, sex FROM subjects WHERE project_id = ? AND local_code = ?',
            (project_id, code)).fetchone()
        if row is not None:
            if (name and row['name'] != name) or (sex is not None and row['sex'] != sex):
                raise SubjectBindingError('SUBJECT_CODE_CONFLICT', '该编号的受试者信息与填写内容不一致，请选择已有受试者或使用其他编号。', 409)
            return str(row['subject_id'])
    identity = uuid4().hex
    connection.execute(
        'INSERT INTO subjects(subject_id, project_id, local_code, created_at, name, sex) VALUES (?, ?, ?, ?, ?, ?)',
        (identity, project_id, code or None, created_at, name, sex))
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
