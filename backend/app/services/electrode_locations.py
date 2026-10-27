"""Resolve scalp sites from MNE's bundled standard 10-20 template."""

from functools import lru_cache

import mne


MONTAGE_NAME = "standard_1020"
SOURCE_ID = "mne.standard_1020"
COORDINATE_FRAME = "mri"
COORDINATE_UNIT = "m"


@lru_cache(maxsize=1)
def _standard_positions() -> dict[str, tuple[str, tuple[float, float, float]]]:
    montage = mne.channels.make_standard_montage(MONTAGE_NAME)
    positions = montage.get_positions()
    if positions["coord_frame"] != COORDINATE_FRAME:
        raise RuntimeError("MNE standard montage coordinate frame changed")
    return {
        name.casefold(): (name, tuple(float(value) for value in xyz))
        for name, xyz in positions["ch_pos"].items()
    }


def resolve_electrode_labels(labels: list[str]) -> list[dict[str, object]]:
    """Return exact case-insensitive matches, preserving request order."""
    positions = _standard_positions()
    resolved: list[dict[str, object]] = []
    for label in labels:
        requested = label.strip()
        match = positions.get(requested.casefold()) if requested else None
        canonical, xyz = match if match is not None else (None, None)
        resolved.append(
            {
                "requested_label": label,
                "status": "available" if xyz is not None else "unavailable",
                "canonical_site": canonical,
                "coordinates": list(xyz) if xyz is not None else None,
                "coordinate_frame": COORDINATE_FRAME if xyz is not None else None,
                "coordinate_unit": COORDINATE_UNIT if xyz is not None else None,
                "source_id": SOURCE_ID,
                "source_version": mne.__version__,
            }
        )
    return resolved
