from pathlib import Path
import hashlib

from app.services.recordings import RecordingService


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
    with service._connect() as connection:
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
    with service._connect() as connection:
        connection.execute(
            "UPDATE recordings SET source_sha256 = NULL, file_size_bytes = NULL WHERE id = ?",
            (item.id,),
        )

    reloaded = RecordingService(storage_dir=storage, database_path=database).require_recording(item.id)

    assert reloaded.id == item.id
    assert reloaded.source_sha256 is None
    assert reloaded.file_size_bytes is None
