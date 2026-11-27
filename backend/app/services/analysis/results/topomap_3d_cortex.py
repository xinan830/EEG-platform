"""Load and serve the packaged fsaverage ico-5 pial cortex and cortical atlas assets."""

from __future__ import annotations

import base64
from functools import lru_cache
from hashlib import sha256
from importlib.resources import files
from io import BytesIO
import json

import numpy as np

from app.services.analysis.results.topomap_3d_assets import MappingAssetsInvalid

CORTEX_NPZ_NAME = "fsaverage_cortex_atlas_v1.npz"
CORTEX_META_NAME = "fsaverage_cortex_atlas_v1.json"


def _b64(array: np.ndarray, dtype: str) -> str:
    return base64.b64encode(np.ascontiguousarray(array, dtype=dtype).tobytes()).decode("ascii")


@lru_cache(maxsize=1)
def load_cortex_atlas_payload() -> dict[str, object]:
    package = files("app.assets.topomap_3d")
    meta_resource = package.joinpath(CORTEX_META_NAME)
    npz_resource = package.joinpath(CORTEX_NPZ_NAME)
    if not meta_resource.is_file() or not npz_resource.is_file():
        raise MappingAssetsInvalid("三维大脑皮层与脑区图谱资产缺失。")

    meta = json.loads(meta_resource.read_bytes().decode("utf-8-sig"))
    npz_bytes = npz_resource.read_bytes()
    actual_sha = sha256(npz_bytes).hexdigest()
    expected_sha = meta.get("npz_sha256")
    if not expected_sha or actual_sha != expected_sha:
        raise MappingAssetsInvalid("三维大脑皮层资产哈希校验失败。")

    with np.load(BytesIO(npz_bytes), allow_pickle=False) as archive:
        vertices = np.asarray(archive["vertices"], dtype="<f4")
        triangles = np.asarray(archive["triangles"], dtype="<i4")
        sulc = np.asarray(archive["sulc"], dtype="<f4")
        atlas_arrays = {
            "lobes": np.asarray(archive["atlas_lobes"], dtype="<i2"),
            "dk": np.asarray(archive["atlas_dk"], dtype="<i2"),
            "yeo7": np.asarray(archive["atlas_yeo7"], dtype="<i2"),
        }

    atlases_payload: dict[str, object] = {}
    for atlas_id, atlas_meta in meta["atlases"].items():
        ids_array = atlas_arrays.get(atlas_id)
        if ids_array is None:
            continue
        atlases_payload[atlas_id] = {
            "id": atlas_meta["id"],
            "name": atlas_meta["name"],
            "regions": atlas_meta["regions"],
            "vertex_region_ids_i16_b64": _b64(ids_array, "<i2"),
        }

    return {
        "model_id": meta["model_id"],
        "model_version": meta["model_version"],
        "source_subject": meta["source_subject"],
        "source_surface": meta["source_surface"],
        "target_coordinate_frame": meta["target_coordinate_frame"],
        "vertex_count": int(vertices.shape[0]),
        "triangle_count": int(triangles.shape[0]),
        "lh_vertex_count": int(meta["lh_vertex_count"]),
        "rh_vertex_count": int(meta["rh_vertex_count"]),
        "npz_sha256": actual_sha,
        "vertices_f32_b64": _b64(vertices, "<f4"),
        "triangles_i32_b64": _b64(triangles, "<i4"),
        "sulc_f32_b64": _b64(sulc, "<f4"),
        "atlases": atlases_payload,
    }
