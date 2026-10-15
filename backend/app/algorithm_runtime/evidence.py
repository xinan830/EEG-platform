"""Scientific sample coordinates shared by full Runs and on-demand windows."""

from typing import Any

from app.scientific.contracts import SampleRange


def with_sample_coordinate(evidence: dict[str, Any], time_range: dict[str, float] | None,
                           sfreq_hz: float) -> dict[str, Any]:
    """Attach the canonical scientific coordinate without changing display seconds."""
    copied = dict(evidence)
    extensions = dict(copied.get("extensions", {}))
    existing = extensions.get("sample_coordinate")
    if isinstance(existing, dict) and existing.get("coordinate_system") == "recording_relative_sample":
        copied["extensions"] = extensions
        return copied
    if time_range is None or "start_s" not in time_range or "end_s" not in time_range:
        coordinate = {"coordinate_system": "recording_relative_seconds", "sample_range": None}
    else:
        sample_range = SampleRange.from_seconds(float(time_range["start_s"]), float(time_range["end_s"]), float(sfreq_hz))
        coordinate = {"coordinate_system": "recording_relative_sample",
                      "sample_range": sample_range.as_dict(), "sfreq_hz": float(sfreq_hz)}
    extensions["sample_coordinate"] = coordinate
    copied["extensions"] = extensions
    return copied
