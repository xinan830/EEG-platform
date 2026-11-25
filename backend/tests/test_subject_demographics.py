"""Person demographics and recording-specific ages never change scientific source identity."""

import sqlite3

import pytest

from app.persistence import connect_database
from app.persistence.migrations import MIGRATIONS, migrate_database
from test_recording_import import _client, _edf_bytes


def test_subject_sex_and_historical_ages_survive_subject_changes_and_deletion(tmp_path):
    client, recordings, projects = _client(tmp_path)
    client.put('/api/projects/desktop/demographics', json={'name': 'Study'})
    root = '/api/projects/demographics'
    subject = client.post(root + '/subjects/resolve', json={'local_code': 'S1', 'sex': 'female'}).json()['subject']
    assert subject['sex'] == 'female'
    source = _edf_bytes(tmp_path / 'demographics.edf')
    imported = []
    for age in [25, 26]:
        response = client.post(root + '/recordings/import', data={
            'subject_id': subject['subject_id'], 'age_at_recording_years': str(age)},
            files={'file': ('demographics.edf', source)})
        assert response.status_code == 201, response.json()
        imported.append(response.json())
    before = [recordings.require_recording(item['recording_id']) for item in imported]
    assert [row.age_at_recording_years for row in projects.list_sessions('demographics')] == [25, 26]
    endpoint = root + '/subjects/' + subject['subject_id']
    # Omitted sex preserves existing metadata for older clients.
    assert client.patch(endpoint, json={'local_code': 'S2', 'name': '更名'}).json()['sex'] == 'female'
    assert client.patch(endpoint, json={'local_code': 'S2', 'sex': 'male'}).json()['sex'] == 'male'
    sessions = client.get(root + '/sessions').json()
    assert all(item['subject_sex'] == 'male' for item in sessions)
    assert [item['age_at_recording_years'] for item in sessions] == [25, 26]
    assert client.patch(endpoint, json={'local_code': 'S2', 'sex': 'invalid'}).status_code == 422
    assert projects.list_subjects('demographics')[0].sex == 'male'
    assert client.patch(endpoint, json={'local_code': 'S2', 'sex': None}).json()['sex'] is None
    session_url = root + '/sessions/' + imported[0]['session_id']
    assert client.patch(session_url, json={'label': 'record'}).json()['age_at_recording_years'] == 25
    assert client.patch(session_url, json={'label': 'record', 'age_at_recording_years': None}).json()['age_at_recording_years'] is None
    assert client.patch(session_url, json={'label': 'record', 'age_at_recording_years': 0}).json()['age_at_recording_years'] == 0
    assert client.delete(endpoint).status_code == 200
    sessions = client.get(root + '/sessions').json()
    assert all(item['subject_id'] is None and item['subject_sex'] is None for item in sessions)
    assert [item['age_at_recording_years'] for item in sessions] == [0, 26]
    for original in before:
        assert recordings.require_recording(original.id) == original
        assert (recordings.storage_dir / original.stored_name).read_bytes() == source


@pytest.mark.parametrize('sex', [None, 'male', 'female', 'other'])
def test_import_new_subject_demographics_and_age_without_subject(tmp_path, sex):
    client, _, projects = _client(tmp_path)
    client.put('/api/projects/desktop/p', json={'name': 'Study'})
    source = _edf_bytes(tmp_path / 'new-person.edf')
    metadata = {'new_subject_name': '同名', 'age_at_recording_years': '150'}
    if sex is not None:
        metadata['new_subject_sex'] = sex
    response = client.post('/api/projects/p/recordings/import', data=metadata, files={'file': ('new-person.edf', source)})
    assert response.status_code == 201, response.json()
    assert projects.list_subjects('p')[0].sex == sex
    assert projects.list_sessions('p')[0].age_at_recording_years == 150
    response = client.post('/api/projects/p/recordings/import', data={'age_at_recording_years': '20'},
                           files={'file': ('new-person.edf', source)})
    assert response.status_code == 201, response.json()
    assert response.json()['subject_id'] is None
    assert len(projects.list_subjects('p')) == 1


@pytest.mark.parametrize('value', [-1, 151, 25.5, True, '25'])
def test_json_age_validation_preserves_existing_session(tmp_path, value):
    client, _, projects = _client(tmp_path)
    client.put('/api/projects/desktop/p', json={'name': 'Study'})
    source = _edf_bytes(tmp_path / 'invalid-age.edf')
    imported = client.post('/api/projects/p/recordings/import', files={'file': ('invalid-age.edf', source)}).json()
    endpoint = '/api/projects/p/sessions/' + imported['session_id']
    assert client.patch(endpoint, json={'label': 'bad', 'age_at_recording_years': value}).status_code == 422
    assert projects.list_sessions('p')[0].age_at_recording_years is None


@pytest.mark.parametrize('metadata', [
    {'new_subject_name': '姓名', 'new_subject_sex': 'invalid'},
    {'new_subject_sex': 'male'}, {'age_at_recording_years': '-1'},
    {'age_at_recording_years': '151'}, {'age_at_recording_years': '25.5'}])
def test_invalid_import_demographics_publish_nothing(tmp_path, metadata):
    client, recordings, projects = _client(tmp_path)
    client.put('/api/projects/desktop/p', json={'name': 'Study'})
    source = _edf_bytes(tmp_path / 'bad-demographics.edf')
    response = client.post('/api/projects/p/recordings/import', data=metadata, files={'file': ('bad-demographics.edf', source)})
    assert response.status_code == 422, response.json()
    assert projects.list_sessions('p') == []
    assert projects.list_subjects('p') == []
    assert recordings.list_recordings() == []
    assert list(recordings.storage_dir.glob('*.edf')) == []


def test_additive_demographics_migration_preserves_populated_database(tmp_path):
    database = tmp_path / 'v25.sqlite3'
    migrate_database(database, MIGRATIONS[:25])
    with connect_database(database, foreign_keys=True) as connection:
        connection.execute("INSERT INTO projects VALUES ('p','Study','','now','now')")
        connection.execute("INSERT INTO subjects VALUES ('u','p','S1','now','姓名')")
        connection.execute("INSERT INTO recordings(id,original_name,stored_name,extension,created_at) VALUES ('r','r.edf','r.edf','.edf','now')")
        connection.execute("INSERT INTO sessions VALUES ('s','p','u','r',NULL,'record','now')")
    migrate_database(database)
    migrate_database(database)
    with connect_database(database, foreign_keys=True) as connection:
        assert connection.execute('PRAGMA foreign_key_check').fetchall() == []
        row = connection.execute("SELECT * FROM sessions WHERE session_id = 's'").fetchone()
        assert row['subject_id'] == 'u' and row['age_at_recording_years'] is None
        assert connection.execute("SELECT sex FROM subjects WHERE subject_id = 'u'").fetchone()[0] is None
        with pytest.raises(sqlite3.IntegrityError):
            connection.execute("UPDATE sessions SET age_at_recording_years = 25.5 WHERE session_id = 's'")
        with pytest.raises(sqlite3.IntegrityError):
            connection.execute("UPDATE subjects SET sex = 'invalid' WHERE subject_id = 'u'")
