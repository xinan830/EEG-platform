from pathlib import Path
import hashlib
import shutil

import numpy as np
import pytest

from app.persistence import connect_database
from app.services.recordings.recordings import RecordingService
from tests.on_demand_fixtures import write_recording
from tests.test_offline_bad_channels import _write_multi_channel_edf


def test_create_recording_never_uses_user_filename_as_storage_path(tmp_path: Path):
    service = RecordingService(
        storage_dir=tmp_path / "recordings",
        database_path=tmp_path / "catalog.sqlite3",
    )

    item = service.create_recording("../../unsafe.bdf", ".bdf", b"raw")

    assert item.stored_name.endswith(".bdf")
    assert ".." not in item.stored_name
    assert (tmp_path / "recordings" / item.stored_name).read_bytes() == b"raw"
    assert item.source_sha256 == hashlib.sha256(b"raw").hexdigest()
    assert item.file_size_bytes == 3
    assert item.import_version == "recording-import-v1"


def test_legacy_recording_identity_is_backfilled_without_requiring_valid_eeg(tmp_path: Path):
    storage = tmp_path / "recordings"
    database = tmp_path / "catalog.sqlite3"
    service = RecordingService(storage_dir=storage, database_path=database)
    item = service.create_recording("legacy.edf", ".edf", b"legacy-bytes")
    with connect_database(service.database_path) as connection:
        connection.execute(
            "UPDATE recordings SET source_sha256 = NULL, file_size_bytes = NULL, import_version = 'legacy-unversioned' WHERE id = ?",
            (item.id,),
        )

    reloaded = RecordingService(storage_dir=storage, database_path=database).require_recording(item.id)

    assert reloaded.source_sha256 == hashlib.sha256(b"legacy-bytes").hexdigest()
    assert reloaded.file_size_bytes == len(b"legacy-bytes")


def test_missing_legacy_source_remains_readable_with_unavailable_identity(tmp_path: Path):
    storage = tmp_path / "recordings"
    database = tmp_path / "catalog.sqlite3"
    service = RecordingService(storage_dir=storage, database_path=database)
    item = service.create_recording("missing.edf", ".edf", b"temporary")
    (storage / item.stored_name).unlink()
    with connect_database(service.database_path) as connection:
        connection.execute(
            "UPDATE recordings SET source_sha256 = NULL, file_size_bytes = NULL WHERE id = ?",
            (item.id,),
        )

    reloaded = RecordingService(storage_dir=storage, database_path=database).require_recording(item.id)

    assert reloaded.id == item.id
    assert reloaded.source_sha256 is None
    assert reloaded.file_size_bytes is None


def test_legacy_recording_with_incomplete_channel_types_remains_readable(tmp_path: Path):
    service = RecordingService(tmp_path / "recordings", tmp_path / "catalog.sqlite3")
    item = service.create_recording("legacy.edf", ".edf", b"legacy-bytes")
    with connect_database(service.database_path) as connection:
        connection.execute(
            "UPDATE recordings SET channels_json = ?, channel_types_json = ? WHERE id = ?",
            ('["Fz", "Pz"]', '["eeg"]', item.id),
        )

    recording = service.require_recording(item.id)

    assert recording.channels == ("Fz", "Pz")
    assert recording.analysis_channels == ("Fz", "Pz")


def test_register_wpf_recording_is_idempotent_for_duplicate_source(tmp_path: Path):
    source_dir = tmp_path / "wpf-source"
    write_recording(source_dir)
    service = RecordingService(tmp_path / "recordings", tmp_path / "catalog.sqlite3")

    first = service.register_wpf_recording(str(source_dir))
    second = service.register_wpf_recording(str(source_dir))

    assert second.id == first.id
    assert len(service.list_recordings()) == 1


def test_register_wpf_recording_rebinds_stale_directory_path_by_content_hash(tmp_path: Path):
    source_dir = tmp_path / "wpf-source"
    moved_dir = tmp_path / "wpf-moved"
    write_recording(source_dir)
    service = RecordingService(tmp_path / "recordings", tmp_path / "catalog.sqlite3")

    first = service.register_wpf_recording(str(source_dir))
    shutil.move(str(source_dir), str(moved_dir))
    rebound = service.register_wpf_recording(str(moved_dir))

    assert rebound.id == first.id
    assert rebound.stored_name == f"wpf:{moved_dir}"
    assert rebound.original_name == "wpf-moved"


def test_delete_import_if_unreferenced_respects_references_and_deletes_unreferenced_blob(tmp_path: Path):
    storage = tmp_path / "recordings"
    service = RecordingService(storage, tmp_path / "catalog.sqlite3")
    deleted: list[str] = []
    service.add_delete_listener(deleted.append)
    values = np.random.default_rng(3).normal(0, 10, (400, 2))
    payload = _write_multi_channel_edf(tmp_path / "sample.edf", ("F3", "O1"), values, sfreq=200)
    recording = service.create_imported_recording("sample.edf", ".edf", payload)

    with connect_database(service.database_path) as connection:
        connection.execute(
            "INSERT INTO event_markers (id, recording_id, time_s, label, duration_s, created_at) VALUES (?, ?, ?, ?, ?, ?)",
            ("marker-1", recording.id, 0.5, "blink", 0.0, "2026-11-27T00:00:00Z"),
        )
    assert service.delete_import_if_unreferenced(recording.id) is False
    assert (storage / recording.stored_name).is_file()
    assert deleted == []

    with connect_database(service.database_path) as connection:
        connection.execute("DELETE FROM event_markers WHERE id = ?", ("marker-1",))
    assert service.delete_import_if_unreferenced(recording.id) is True
    assert not (storage / recording.stored_name).exists()
    assert service.list_recordings() == []
    assert deleted == [recording.id]


def test_create_imported_recording_rolls_back_file_and_row_when_metadata_read_fails(tmp_path: Path):
    storage = tmp_path / "recordings"
    service = RecordingService(storage, tmp_path / "catalog.sqlite3")

    with pytest.raises(ValueError, match="无法读取 BDF/EDF 脑电文件"):
        service.create_imported_recording("corrupt.edf", ".edf", b"not-a-valid-edf")

    assert list(storage.iterdir()) == []
    assert service.list_recordings() == []
