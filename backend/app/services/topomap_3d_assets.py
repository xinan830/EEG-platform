"""Load and validate the versioned MNE-to-GNM 3D topomap assets."""

from dataclasses import dataclass
from functools import lru_cache
from hashlib import sha256
from importlib.resources import files
from io import BytesIO
import json

import mne
import numpy as np


MAPPING_ID = "mne.colin27_1020-to-gnm-head"
MAPPING_VERSION = 4
MODEL_ID = "gnm-colin27-head"
MODEL_VERSION = "gnm-skin-mesh-v1"
MAPPING_NAME = "colin27_1020_gnm_mapping_v4.json"
MESH_NAME = "gnm_skin_mesh.npz"
MAPPING_SHA256 = "a973409df056b801113461c0e40d4381751217921505c3b64201cfb682b0f5d8"
MESH_ASSET_SHA256 = "5bf5155d05b5b5cd377dc2870454f9291ba1e66726cd6ef2cc1bb636398bcabd"
MESH_GEOMETRY_SHA256 = "67e9c419ddeaab84dafceabd835e134cb30e5b5bfbc048da6b339c7d373f0a46"
EXPECTED_CHANNEL_COUNT = 94
CHANNEL_ALIASES = {"T3": "T7", "T4": "T8", "T5": "P7", "T6": "P8"}


class MappingAssetsInvalid(ValueError):
    """Raised when packaged mapping/model data fail identity or source checks."""


@dataclass(frozen=True)
class Topomap3DAssets:
    mapping: dict[str, object]
    full_montage: dict[str, object]
    channel_locations: dict[str, dict[str, object]]
    vertices: np.ndarray
    triangles: np.ndarray
    head_origin: np.ndarray
    right_axis: np.ndarray
    forward_axis: np.ndarray
    up_axis: np.ndarray
    mapping_sha256: str
    mesh_asset_sha256: str
    mesh_geometry_sha256: str
    mne_version: str


def _geometry_digest(vertices: np.ndarray, triangles: np.ndarray) -> str:
    digest = sha256()
    digest.update(np.asarray(vertices, dtype="<f8").tobytes())
    digest.update(np.asarray(triangles, dtype="<i8").tobytes())
    return digest.hexdigest()


def _mne_head_positions() -> tuple[list[str], dict[str, np.ndarray]]:
    montage = mne.channels.make_standard_montage("colin27_1020")
    info = mne.create_info(montage.ch_names, sfreq=500.0, ch_types="eeg")
    info.set_montage(montage, on_missing="raise")
    names = list(info.ch_names)
    points = {channel["ch_name"]: np.asarray(channel["loc"][:3], dtype=float)
              for channel in info["chs"]}
    if len(names) != EXPECTED_CHANNEL_COUNT or not np.isfinite(list(points.values())).all():
        raise MappingAssetsInvalid("MNE colin27_1020 returned an unexpected channel inventory")
    return names, points


def _head_frame(core: dict[str, object]) -> tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
    landmarks = core["landmarks_gnm"]
    origin = (np.asarray(landmarks["lpa"], dtype=float) + np.asarray(landmarks["rpa"], dtype=float)) / 2
    right = np.asarray(landmarks["rpa"], dtype=float) - np.asarray(landmarks["lpa"], dtype=float)
    right /= np.linalg.norm(right)
    forward = np.asarray(landmarks["nasion"], dtype=float) - origin
    forward -= np.dot(forward, right) * right
    forward /= np.linalg.norm(forward)
    up = np.cross(right, forward)
    up /= np.linalg.norm(up)
    return origin, right, forward, up


@lru_cache(maxsize=1)
def load_topomap_3d_assets() -> Topomap3DAssets:
    package = files("app.assets.topomap_3d")
    mapping_bytes = package.joinpath(MAPPING_NAME).read_bytes()
    mesh_bytes = package.joinpath(MESH_NAME).read_bytes()
    mapping_hash = sha256(mapping_bytes).hexdigest()
    mesh_hash = sha256(mesh_bytes).hexdigest()
    if mapping_hash != MAPPING_SHA256 or mesh_hash != MESH_ASSET_SHA256:
        raise MappingAssetsInvalid("Packaged GNM mapping or mesh content identity does not match")

    document = json.loads(mapping_bytes.decode("utf-8-sig"))
    if document.get("schema_version") != MAPPING_VERSION or document.get("algorithm") != "gnm-colin27-scalp-proportions-v1":
        raise MappingAssetsInvalid("Unsupported GNM mapping format")
    core = document.get("core_mapping")
    full = document.get("full_montage")
    if not isinstance(core, dict) or not isinstance(full, dict):
        raise MappingAssetsInvalid("GNM mapping is missing its core or full montage")
    if (core.get("montage") != "colin27_1020" or core.get("mne_coordinate_frame") != "head"
            or core.get("mne_units") != "m" or core.get("units") != "GNM model units"
            or full.get("montage") != "colin27_1020" or full.get("mne_coordinate_frame") != "head"
            or full.get("mne_units") != "m" or full.get("gnm_units") != "GNM model units"):
        raise MappingAssetsInvalid("GNM mapping coordinate frames or units do not match the 3D contract")

    with np.load(BytesIO(mesh_bytes), allow_pickle=False) as mesh:
        vertices = np.asarray(mesh["vertices"], dtype=np.float64)
        triangles = np.asarray(mesh["faces"], dtype=np.int64)
    if (vertices.ndim != 2 or vertices.shape[1] != 3 or not len(vertices) or not np.isfinite(vertices).all()
            or triangles.ndim != 2 or triangles.shape[1] != 3 or not len(triangles) or triangles.min() < 0
            or triangles.max() >= len(vertices)):
        raise MappingAssetsInvalid("GNM skin mesh arrays are invalid")
    geometry_hash = _geometry_digest(vertices, triangles)
    if geometry_hash != MESH_GEOMETRY_SHA256 or core.get("mesh_sha256") != geometry_hash:
        raise MappingAssetsInvalid("GNM mapping targets a different skin mesh geometry")

    channel_order = full.get("channel_order")
    locations = full.get("channels")
    names, source_points = _mne_head_positions()
    if (not isinstance(channel_order, list) or channel_order != names
            or not isinstance(locations, dict) or len(locations) != EXPECTED_CHANNEL_COUNT
            or full.get("channel_count") != EXPECTED_CHANNEL_COUNT):
        raise MappingAssetsInvalid("GNM mapping channel order differs from MNE colin27_1020")
    for name in channel_order:
        location = locations.get(name)
        if not isinstance(location, dict):
            raise MappingAssetsInvalid(f"GNM mapping is missing channel {name}")
        source = np.asarray(location.get("coordinate_mne"), dtype=float)
        target = np.asarray(location.get("coordinate_gnm"), dtype=float)
        if (source.shape != (3,) or target.shape != (3,) or not np.isfinite(source).all()
                or not np.isfinite(target).all()
                or not np.allclose(source, source_points[name], rtol=0, atol=1e-9)):
            raise MappingAssetsInvalid(f"GNM mapping coordinates do not match MNE channel {name}")
    aliases = full.get("aliases")
    if aliases != CHANNEL_ALIASES or any(
        locations.get(alias, {}).get("coordinate_gnm") != locations.get(canonical, {}).get("coordinate_gnm")
        for alias, canonical in CHANNEL_ALIASES.items()
    ):
        raise MappingAssetsInvalid("GNM mapping aliases do not resolve to canonical electrode coordinates")
    if full.get("accuracy_status") != "measured contour fractions; anatomical review required":
        raise MappingAssetsInvalid("GNM mapping accuracy status is missing or unexpected")

    origin, right, forward, up = _head_frame(core)
    for array in (vertices, triangles, origin, right, forward, up):
        array.setflags(write=False)
    return Topomap3DAssets(document, full, locations, vertices, triangles,
        origin, right, forward, up, mapping_hash, mesh_hash, geometry_hash, mne.__version__)
