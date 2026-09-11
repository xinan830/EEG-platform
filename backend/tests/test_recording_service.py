from pathlib import Path

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
