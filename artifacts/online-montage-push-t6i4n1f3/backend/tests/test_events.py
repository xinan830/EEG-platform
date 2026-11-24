from pathlib import Path

from app.services.events import EventMarkerService


def test_event_markers_are_sorted_and_scoped_to_recording(tmp_path: Path):
    service = EventMarkerService(tmp_path / "catalog.sqlite3")
    service.create("recording-1", 8.0, "结束")
    service.create("recording-1", 2.0, "开始", 1.5)
    service.create("recording-2", 1.0, "其他")
    markers = service.list_markers("recording-1")
    assert [marker["time_s"] for marker in markers] == [2.0, 8.0]
    assert markers[0]["duration_s"] == 1.5


def test_event_marker_delete_is_scoped_and_idempotent(tmp_path: Path):
    service = EventMarkerService(tmp_path / "catalog.sqlite3")
    marker = service.create("recording-1", 2.0, "事件")
    assert service.delete("recording-2", marker["id"]) is False
    assert service.delete("recording-1", marker["id"]) is True
    assert service.delete("recording-1", marker["id"]) is False
