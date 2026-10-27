"""Resolve scalp sites from MNE's bundled standard 10-20 template."""

from functools import lru_cache
from threading import Lock

import matplotlib
matplotlib.use("Agg", force=True)
import matplotlib.pyplot as plt
import mne
import numpy as np


MONTAGE_NAME = "standard_1020"
SOURCE_ID = "mne.standard_1020"
COORDINATE_FRAME = "mri"
COORDINATE_UNIT = "m"
MAP_EDGE_SITES = ("Fp1", "Fpz", "Fp2", "T7", "T8", "O1", "Oz", "O2")
MAP_EDGE_RADIUS = 0.92
_POSITION_CACHE_LOCK = Lock()


def _mne_topomap_positions(info: mne.Info) -> dict[str, tuple[float, float]]:
    """Read MNE's EEG topomap projection and fit its 10-20 edge to the diagram."""
    figure = mne.viz.plot_sensors(
        info,
        kind="topomap",
        ch_type="eeg",
        sphere="eeglab",
        show_names=False,
        show=False,
    )
    try:
        axes = figure.axes[0]
        sensor_collections = [
            collection for collection in axes.collections
            if len(collection.get_offsets()) == len(info.ch_names)
        ]
        if len(sensor_collections) != 1:
            raise RuntimeError("MNE did not return one complete EEG sensor collection")

        sensor_positions = np.asarray(sensor_collections[0].get_offsets(), dtype=float)
        head_outlines = []
        for line in axes.lines:
            points = np.column_stack(line.get_data())
            if len(points) >= 20 and np.allclose(points[0], points[-1]):
                head_outlines.append(points)
        if not head_outlines:
            raise RuntimeError("MNE did not return a closed EEG head outline")

        head_outline = max(head_outlines, key=lambda points: np.ptp(points[:, 0]) * np.ptp(points[:, 1]))
        head_center = (head_outline.min(axis=0) + head_outline.max(axis=0)) / 2
        head_radius = (np.ptp(head_outline[:, 0]) + np.ptp(head_outline[:, 1])) / 4
        if not np.isfinite(head_radius) or head_radius <= 0:
            raise RuntimeError("MNE returned an invalid EEG head outline")

        # MNE's sphere projection is in physical plotting units. Scale the
        # standard 10-20 perimeter to the editable diagram's inner margin.
        projected = (sensor_positions - head_center) / head_radius
        projected[:, 1] *= -1
        names_to_indices = {name: index for index, name in enumerate(info.ch_names)}
        edge_radius = max(
            float(np.linalg.norm(projected[names_to_indices[name]]))
            for name in MAP_EDGE_SITES
        )
        if not np.isfinite(edge_radius) or edge_radius <= 0:
            raise RuntimeError("MNE returned invalid standard 10-20 edge positions")
        projected *= MAP_EDGE_RADIUS / edge_radius
        radii = np.linalg.norm(projected, axis=1)
        outside_head = radii > MAP_EDGE_RADIUS
        projected[outside_head] *= (MAP_EDGE_RADIUS / radii[outside_head])[:, np.newaxis]
        return {
            name: (float(position[0]), float(position[1]))
            for name, position in zip(info.ch_names, projected, strict=True)
        }
    finally:
        plt.close(figure)


@lru_cache(maxsize=1)
def _standard_positions() -> dict[str, tuple[str, tuple[float, float, float], tuple[float, float]]]:
    montage = mne.channels.make_standard_montage(MONTAGE_NAME)
    positions = montage.get_positions()
    if positions["coord_frame"] != COORDINATE_FRAME:
        raise RuntimeError("MNE standard montage coordinate frame changed")

    # MNE's public EEG sensor plot rejects overlapping legacy/current names
    # (for example T3/T7). Plot one name per exact XYZ point, then let aliases
    # sharing that point reuse the projected map coordinate.
    unique_positions: dict[tuple[float, float, float], str] = {}
    for name, xyz in positions["ch_pos"].items():
        key = tuple(float(value) for value in xyz)
        unique_positions.setdefault(key, name)
    info = mne.create_info(list(unique_positions.values()), sfreq=256, ch_types="eeg")
    info.set_montage(montage, on_missing="raise")
    projected_positions = _mne_topomap_positions(info)
    map_by_xyz = {xyz: projected_positions[name] for xyz, name in unique_positions.items()}
    return {
        name.casefold(): (
            name,
            tuple(float(value) for value in xyz),
            map_by_xyz[tuple(float(value) for value in xyz)],
        )
        for name, xyz in positions["ch_pos"].items()
    }


def resolve_electrode_labels(labels: list[str]) -> list[dict[str, object]]:
    """Return exact case-insensitive matches, preserving request order."""
    # lru_cache protects its bookkeeping, but concurrent first calls can still
    # execute the wrapped function more than once. Matplotlib is not thread-safe.
    with _POSITION_CACHE_LOCK:
        positions = _standard_positions()
    resolved: list[dict[str, object]] = []
    for label in labels:
        requested = label.strip()
        match = positions.get(requested.casefold()) if requested else None
        canonical, xyz, map_coordinates = match if match is not None else (None, None, None)
        resolved.append(
            {
                "requested_label": label,
                "status": "available" if xyz is not None else "unavailable",
                "canonical_site": canonical,
                "coordinates": list(xyz) if xyz is not None else None,
                "map_coordinates": list(map_coordinates) if map_coordinates is not None else None,
                "coordinate_frame": COORDINATE_FRAME if xyz is not None else None,
                "coordinate_unit": COORDINATE_UNIT if xyz is not None else None,
                "source_id": SOURCE_ID,
                "source_version": mne.__version__,
            }
        )
    return resolved
