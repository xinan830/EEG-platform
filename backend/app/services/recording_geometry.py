"""Read immutable recording geometry and attach its scientific source identity."""
from dataclasses import replace
import hashlib
import json
from pathlib import Path
from typing import TYPE_CHECKING

from app.models.recording import RecordingSummary

from app.scientific.electrode_geometry import geometry_from_channel_snapshot, geometry_fingerprint

if TYPE_CHECKING:
    from app.services.recording_access import RecordingAccessSession


def recording_electrode_geometry(recording: RecordingSummary) -> dict[str, object] | None:
    """Read the recording snapshot without deriving geometry from labels or diagrams."""
    if getattr(recording, "extension", None) == ".wpf":
        name = recording.stored_name
        root = Path(name.split(":", 1)[1] if name.startswith(("wpf:", "wpf-montage:")) else name)
        manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8-sig"))
        snapshot = manifest.get("HardwareConfiguration", {}).get("channel_configuration_snapshot_json")
    else:
        snapshot = getattr(recording, "import_channel_configuration_snapshot_json", None)
    if snapshot is None:
        return None
    snapshot = json.loads(snapshot) if isinstance(snapshot, str) else snapshot
    if not isinstance(snapshot, dict):
        raise ValueError("ELECTRODE_GEOMETRY_INVALID: 通道快照必须为对象")
    return geometry_from_channel_snapshot(snapshot)


def bind_session_geometry(session: "RecordingAccessSession", geometry: dict[str, object] | None) -> "RecordingAccessSession":
    """Separate scientific cache/model identities when source geometry differs."""
    if geometry is not None:
        session.identity = replace(session.identity, signal_view_sha256=hashlib.sha256(
            json.dumps([session.identity.signal_view_sha256, geometry_fingerprint(geometry)]).encode(),
        ).hexdigest())
    return session
