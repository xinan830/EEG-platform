"""Optional subject management must never mutate recording signal identity."""

import pytest

from app.persistence import connect_database
from app.persistence.migrations import MIGRATIONS, migrate_database
from test_recording_import import _client, _edf_bytes


@pytest.mark.parametrize('metadata, code, name', [({}, None, ''),
    ({'new_subject_local_code': ' S001 '}, 'S001', ''),
    ({'new_subject_name': ' 测试者 '}, None, '测试者'),
    ({'new_subject_local_code': 'S001', 'new_subject_name': '测试者'}, 'S001', '测试者')])
def test_import_optional_subject_and_edit_clear_preserve_source(tmp_path, metadata, code, name):
    client, recordings, projects = _client(tmp_path)
    project = client.put('/api/projects/desktop/subject-test', json={'name': 'Study'}).json()['project_id']
    source = _edf_bytes(tmp_path / 'subject.edf')
    response = client.post(f'/api/projects/{project}/recordings/import', data=metadata,
                           files={'file': ('subject.edf', source)})
    assert response.status_code == 201, response.json()
    imported = response.json()
    sessions = projects.list_sessions(project)
    assert sessions[0].subject_code == code
    assert sessions[0].subject_name == name
    assert (imported['subject_id'] is None) == (not metadata)
    assert len(projects.list_subjects(project)) == bool(metadata)
    before = recordings.require_recording(imported['recording_id'])
    endpoint = f"/api/projects/{project}/sessions/{imported['session_id']}"
    partial = client.patch(endpoint, json={'label': '错误更新', 'subject_name': '未配对姓名'})
    assert partial.status_code == 422
    assert projects.list_sessions(project) == sessions
    edited = client.patch(endpoint, json={'label': '记录', 'subject_code': 'S002', 'subject_name': '新姓名'})
    assert edited.status_code == 200, edited.json()
    assert edited.json()['subject_name'] == '新姓名'
    cleared = client.patch(endpoint, json={'label': '记录', 'subject_code': '', 'subject_name': ''})
    assert cleared.status_code == 200
    assert cleared.json()['subject_id'] is None
    assert recordings.require_recording(before.id) == before
    assert (recordings.storage_dir / before.stored_name).read_bytes() == source


def test_subject_code_reuse_name_only_isolation_and_shared_name_edit(tmp_path):
    client, _, projects = _client(tmp_path)
    project = client.put('/api/projects/desktop/shared', json={'name': 'Study'}).json()['project_id']
    source = _edf_bytes(tmp_path / 'subject.edf')
    def upload(metadata):
        return client.post(f'/api/projects/{project}/recordings/import', data=metadata,
                           files={'file': ('subject.edf', source)})
    first = upload({'new_subject_local_code': 'S001', 'new_subject_name': '姓名'}).json()
    second = upload({'new_subject_local_code': 'S001'}).json()
    assert first['subject_id'] == second['subject_id']
    assert upload({'new_subject_local_code': 'S001', 'new_subject_name': '其他姓名'}).status_code == 409
    names = [upload({'new_subject_name': '同名'}).json()['subject_id'] for _ in range(2)]
    assert names[0] != names[1]
    result = client.patch(f"/api/projects/{project}/sessions/{first['session_id']}",
        json={'label': '记录', 'subject_code': 'S001', 'subject_name': '更正姓名'})
    assert result.status_code == 200
    assert all(s.subject_name == '更正姓名' for s in projects.list_sessions(project) if s.subject_id == first['subject_id'])


def test_optional_subject_migration_preserves_existing_relationships(tmp_path):
    database = tmp_path / 'legacy.sqlite3'
    migrate_database(database, MIGRATIONS[:24])
    with connect_database(database, foreign_keys=True) as connection:
        connection.execute("INSERT INTO projects VALUES ('p','Study','','now','now')")
        connection.execute("INSERT INTO subjects VALUES ('u','p','S001','now')")
        connection.execute("INSERT INTO recordings(id,original_name,stored_name,extension,created_at) VALUES ('r','r.edf','r.edf','.edf','now')")
        connection.execute("INSERT INTO sessions VALUES ('s','p','u','r',NULL,'record','now')")
    migrate_database(database)
    with connect_database(database, foreign_keys=True) as connection:
        assert connection.execute('PRAGMA foreign_key_check').fetchall() == []
        assert connection.execute("SELECT subject_id FROM sessions WHERE session_id = 's'").fetchone()[0] == 'u'
        connection.execute("UPDATE sessions SET subject_id = NULL WHERE session_id = 's'")
        connection.execute("INSERT INTO subjects(subject_id,project_id,local_code,created_at,name) VALUES ('name','p',NULL,'now','仅姓名')")


def test_invalid_subject_requests_leave_file_catalog_and_sessions_unchanged(tmp_path):
    client, recordings, projects = _client(tmp_path)
    project = client.put('/api/projects/desktop/validation', json={'name': 'Study'}).json()['project_id']
    other = client.put('/api/projects/desktop/other', json={'name': 'Other'}).json()['project_id']
    subject = client.post(f'/api/projects/{other}/subjects', json={'name': '姓名'}).json()['subject_id']
    source = _edf_bytes(tmp_path / 'invalid-subject.edf')
    for metadata, status in [({'subject_id': subject}, 409),
                              ({'new_subject_name': 'x' * 161}, 422)]:
        response = client.post(f'/api/projects/{project}/recordings/import', data=metadata,
                               files={'file': ('subject.edf', source)})
        assert response.status_code == status, response.json()
        assert recordings.list_recordings() == []
        assert projects.list_sessions(project) == []
        assert projects.list_subjects(project) == []
        assert list(recordings.storage_dir.glob('*.edf')) == []
    assert client.post(f'/api/projects/{project}/subjects', json={}).status_code == 422


def test_create_session_without_subject_and_explicit_existing_subject_import(tmp_path):
    client, _, projects = _client(tmp_path)
    project = client.put('/api/projects/desktop/session', json={'name': 'Study'}).json()['project_id']
    source = _edf_bytes(tmp_path / 'session.edf')
    imported = client.post(f'/api/projects/{project}/recordings/import',
                           files={'file': ('session.edf', source)}).json()
    response = client.post(f'/api/projects/{project}/sessions', json={'recording_id': imported['recording_id']})
    assert response.status_code == 201, response.json()
    assert response.json()['subject_id'] is None
    subject = client.post(f'/api/projects/{project}/subjects', json={'local_code': 'S001', 'name': '姓名'}).json()
    response = client.post(f'/api/projects/{project}/recordings/import',
                           data={'subject_id': subject['subject_id']}, files={'file': ('session.edf', source)})
    assert response.status_code == 201, response.json()
    assert response.json()['subject_id'] == subject['subject_id']
    assert projects.list_sessions(project)[-1].subject_name == '姓名'


def test_shared_subject_identity_update_and_delete_detach_without_deleting_signals(tmp_path):
    client, recordings, projects = _client(tmp_path)
    project = client.put('/api/projects/desktop/subject-crud', json={'name': 'Study'}).json()['project_id']
    endpoint = f'/api/projects/{project}/subjects'
    assert client.post(endpoint + '/resolve', json={}).json() == {'subject': None}
    first = client.post(endpoint + '/resolve', json={'local_code': ' S001 ', 'name': '姓名'}).json()['subject']
    assert client.post(endpoint + '/resolve', json={'local_code': 'S001'}).json()['subject'] == first
    assert client.post(endpoint + '/resolve', json={'local_code': 'S001', 'name': '别人'}).status_code == 409
    names = [client.post(endpoint + '/resolve', json={'name': '同名'}).json()['subject']['subject_id'] for _ in range(2)]
    assert names[0] != names[1]
    source = _edf_bytes(tmp_path / 'subject-crud.edf')
    imported = client.post(f'/api/projects/{project}/recordings/import',
        data={'subject_id': first['subject_id']}, files={'file': ('subject-crud.edf', source)}).json()
    before = recordings.require_recording(imported['recording_id'])
    changed = client.patch(endpoint + '/' + first['subject_id'], json={'local_code': 'S002', 'name': '更名'})
    assert changed.status_code == 200, changed.json()
    assert changed.json()['subject_id'] == first['subject_id']
    assert projects.list_sessions(project)[0].subject_code == 'S002'
    session_url = f"/api/projects/{project}/sessions/{imported['session_id']}"
    bound = client.patch(session_url, json={'label': '记录', 'subject_id': names[0]})
    assert bound.status_code == 200, bound.json()
    assert bound.json()['subject_id'] == names[0]
    assert client.patch(session_url, json={'label': '记录'}).json()['subject_id'] == names[0]
    assert client.patch(session_url, json={'label': '记录', 'subject_id': None}).json()['subject_id'] is None
    assert client.patch(session_url, json={'label': '记录', 'subject_id': first['subject_id']}).status_code == 200
    deleted = client.delete(endpoint + '/' + first['subject_id'])
    assert deleted.status_code == 200, deleted.json()
    assert deleted.json()['detached_sessions'] == 1
    assert projects.list_sessions(project)[0].subject_id is None
    assert recordings.require_recording(before.id) == before
    assert (recordings.storage_dir / before.stored_name).read_bytes() == source
    assert client.delete(endpoint + '/' + first['subject_id']).status_code == 404


def test_subject_management_rejects_cross_project_duplicates_and_ambiguous_binding(tmp_path):
    client, _, projects = _client(tmp_path)
    for project in ['one', 'two']:
        client.put(f'/api/projects/desktop/{project}', json={'name': project})
    first = client.post('/api/projects/one/subjects', json={'local_code': 'S1'}).json()['subject_id']
    second = client.post('/api/projects/one/subjects', json={'local_code': 'S2'}).json()['subject_id']
    endpoint = f'/api/projects/one/subjects/{first}'
    assert client.patch(endpoint, json={}).status_code == 422
    assert client.patch(endpoint, json={'local_code': 'S2'}).status_code == 409
    assert client.patch(f'/api/projects/two/subjects/{first}', json={'name': '改名'}).status_code == 404
    assert client.delete(f'/api/projects/two/subjects/{first}').status_code == 404
    source = _edf_bytes(tmp_path / 'cross.edf')
    record = client.post('/api/projects/two/recordings/import', files={'file': ('cross.edf', source)}).json()
    session = f"/api/projects/two/sessions/{record['session_id']}"
    assert client.patch(session, json={'label': 'r', 'subject_id': first}).status_code == 409
    assert client.patch(session, json={'label': 'r', 'subject_id': None, 'subject_code': '', 'subject_name': ''}).status_code == 422
    assert projects.list_sessions('two')[0].subject_id is None
    assert {s.subject_id for s in projects.list_subjects('one')} == {first, second}
