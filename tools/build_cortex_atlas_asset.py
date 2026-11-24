"""Build and anatomically verify the fsaverage ico-5 pial cortex + atlas asset aligned to gnm_skin_mesh."""

from __future__ import annotations

import json
import os
import struct
from hashlib import sha256
from pathlib import Path

import mne
import numpy as np
from mne.transforms import apply_trans, invert_transform
from scipy.spatial import cKDTree

REPO_ROOT = Path(__file__).resolve().parents[1]
ASSETS_DIR = REPO_ROOT / "backend" / "app" / "assets" / "topomap_3d"
MAPPING_PATH = ASSETS_DIR / "colin27_1020_gnm_mapping_v4.json"
SKIN_MESH_PATH = ASSETS_DIR / "gnm_skin_mesh.npz"
OUT_NPZ = ASSETS_DIR / "fsaverage_cortex_atlas_v1.npz"
OUT_JSON = ASSETS_DIR / "fsaverage_cortex_atlas_v1.json"

# Clean Chinese names & refined medical-atlas color palette for major lobes and Yeo-7 networks.
LOBE_INFO = {
    "LOBE.FRONTAL": ("额叶", "#EF6C6C"),
    "LOBE.PARIETAL": ("顶叶", "#5B9BD5"),
    "LOBE.TEMPORAL": ("颞叶", "#68C37A"),
    "LOBE.OCCIPITAL": ("枕叶", "#F2B650"),
    "LOBE.LIMBIC": ("边缘叶", "#B28CE2"),
    "LOBE.INSULA": ("脑岛", "#55C4C4"),
    "MEDIAL.WALL": ("内侧壁", "#CBD5E1"),
}

YEO7_INFO = {
    "7Networks_1": ("视觉网络 (Visual)", "#8B5CF6"),
    "7Networks_2": ("躯体运动网络 (Somatomotor)", "#3B82F6"),
    "7Networks_3": ("背侧注意网络 (Dorsal Attention)", "#10B981"),
    "7Networks_4": ("腹侧注意网络 (Ventral Attention)", "#EC4899"),
    "7Networks_5": ("边缘网络 (Limbic)", "#F59E0B"),
    "7Networks_6": ("额顶控制网络 (Frontoparietal)", "#F97316"),
    "7Networks_7": ("默认模式网络 (Default Mode)", "#EF4444"),
}

# Representative Chinese labels for Desikan-Killiany (aparc) regions (abbreviated cleanly for tooltips & legend)
DK_ZH = {
    "bankssts": "颞上沟后部",
    "caudalanteriorcingulate": "尾侧前扣带回",
    "caudalmiddlefrontal": "额中回尾侧",
    "cuneus": "楔叶",
    "entorhinal": "内嗅皮层",
    "fusiform": "梭状回",
    "inferiorparietal": "顶下小叶",
    "inferiortemporal": "颞下回",
    "isthmuscingulate": "扣带回峡部",
    "lateraloccipital": "枕外侧皮层",
    "lateralorbitofrontal": "外侧眶额皮层",
    "lingual": "舌回",
    "medialorbitofrontal": "内侧眶额皮层",
    "middletemporal": "颞中回",
    "parahippocampal": "海马旁回",
    "paracentral": "中央旁小叶",
    "parsopercularis": "额下回岛盖部",
    "parsorbitalis": "额下回眶部",
    "parstriangularis": "额下回三角部",
    "pericalcarine": "距状裂周围皮层",
    "postcentral": "中央后回",
    "posteriorcingulate": "后扣带回",
    "precentral": "中央前回",
    "precuneus": "楔前叶",
    "rostralanteriorcingulate": "喙侧前扣带回",
    "rostralmiddlefrontal": "额中回喙侧",
    "superiorfrontal": "额上回",
    "superiorparietal": "顶上小叶",
    "superiortemporal": "颞上回",
    "supramarginal": "缘上回",
    "frontalpole": "额极",
    "temporalpole": "颞极",
    "transversetemporal": "颞横回",
    "insula": "脑岛",
    "unknown": "内侧壁",
}

DK_LOBE_GROUP = {
    "caudalmiddlefrontal": "额叶", "frontalpole": "额叶", "lateralorbitofrontal": "额叶",
    "medialorbitofrontal": "额叶", "paracentral": "额叶", "parsopercularis": "额叶",
    "parsorbitalis": "额叶", "parstriangularis": "额叶", "precentral": "额叶",
    "rostralmiddlefrontal": "额叶", "superiorfrontal": "额叶",
    "inferiorparietal": "顶叶", "postcentral": "顶叶", "precuneus": "顶叶",
    "superiorparietal": "顶叶", "supramarginal": "顶叶",
    "bankssts": "颞叶", "entorhinal": "颞叶", "fusiform": "颞叶", "inferiortemporal": "颞叶",
    "middletemporal": "颞叶", "parahippocampal": "颞叶", "superiortemporal": "颞叶",
    "temporalpole": "颞叶", "transversetemporal": "颞叶",
    "cuneus": "枕叶", "lateraloccipital": "枕叶", "lingual": "枕叶", "pericalcarine": "枕叶",
    "caudalanteriorcingulate": "边缘叶", "isthmuscingulate": "边缘叶",
    "posteriorcingulate": "边缘叶", "rostralanteriorcingulate": "边缘叶",
    "insula": "脑岛",
}


def read_fs_surface(path: Path) -> tuple[np.ndarray, np.ndarray]:
    """Read a FreeSurfer binary triangle surface (.pial / .white) without nibabel."""
    with path.open("rb") as f:
        magic = int.from_bytes(f.read(3), "big")
        if magic != 16777214:
            raise ValueError(f"Unsupported FreeSurfer surface magic {magic} in {path}")
        # Two newline-terminated header lines
        f.readline()
        f.readline()
        n_verts, n_faces = struct.unpack(">ii", f.read(8))
        coords = np.frombuffer(f.read(n_verts * 3 * 4), dtype=">f4").reshape(n_verts, 3).astype(np.float64)
        faces = np.frombuffer(f.read(n_faces * 3 * 4), dtype=">i4").reshape(n_faces, 3).astype(np.int32)
        return coords, faces


def read_fs_curv(path: Path) -> np.ndarray:
    """Read a FreeSurfer new-format curvature file (.sulc / .curv) without nibabel."""
    with path.open("rb") as f:
        magic = int.from_bytes(f.read(3), "big")
        if magic != 16777215:
            raise ValueError(f"Unsupported FreeSurfer curv magic {magic} in {path}")
        n_verts, _n_faces, _vals_per_vert = struct.unpack(">iii", f.read(12))
        return np.frombuffer(f.read(n_verts * 4), dtype=">f4").astype(np.float32)


def read_fs_annot(path: Path) -> tuple[np.ndarray, list[tuple[str, int, int, int]]]:
    """Read a FreeSurfer .annot file returning (per-vertex ctab index, list of (name, r, g, b))."""
    with path.open("rb") as f:
        n_verts = struct.unpack(">i", f.read(4))[0]
        raw = np.frombuffer(f.read(n_verts * 8), dtype=">i4").reshape(n_verts, 2)
        vnos = raw[:, 0]
        packed = raw[:, 1]

        has_ctab = struct.unpack(">i", f.read(4))[0]
        if not has_ctab:
            raise ValueError(f"No color table in {path}")
        ctab_ver = struct.unpack(">i", f.read(4))[0]
        if ctab_ver > 0:
            raise ValueError(f"Legacy ctab format unsupported in {path}")
        _max_struct = struct.unpack(">i", f.read(4))[0]
        fname_len = struct.unpack(">i", f.read(4))[0]
        f.read(fname_len)
        num_entries = struct.unpack(">i", f.read(4))[0]

        code_to_entry: dict[int, int] = {}
        entries: list[tuple[str, int, int, int]] = []
        for _ in range(num_entries):
            _struct_id, name_len = struct.unpack(">ii", f.read(8))
            name = f.read(name_len).rstrip(b"\x00").decode("utf-8", errors="replace")
            r, g, b, _flag = struct.unpack(">iiii", f.read(16))
            rgb_code = r + (g << 8) + (b << 16)
            entry_idx = len(entries)
            entries.append((name, r, g, b))
            code_to_entry[rgb_code] = entry_idx

        max_v = int(vnos.max()) + 1 if len(vnos) > 0 else n_verts
        out = np.full(max_v, -1, dtype=np.int32)
        # Mask out top byte (transparency flag) to match rgb_code reliably
        rgb_only = packed & 0x00FFFFFF
        for v, code in zip(vnos.tolist(), rgb_only.tolist()):
            idx = code_to_entry.get(code, -1)
            if idx >= 0:
                out[v] = idx
        return out, entries


def fit_affine(src_pts: np.ndarray, dst_pts: np.ndarray) -> np.ndarray:
    """Solve least-squares 3D affine transform X_dst = X_src @ A + t (returns 4x4 matrix)."""
    n = src_pts.shape[0]
    aug = np.hstack([src_pts, np.ones((n, 1), dtype=np.float64)])
    sol, _, _, _ = np.linalg.lstsq(aug, dst_pts, rcond=None)
    mat = np.eye(4, dtype=np.float64)
    mat[:3, :3] = sol[:3, :].T
    mat[:3, 3] = sol[3, :]
    return mat


def load_atlas_for_ico5(
    subjects_dir: str,
    parc: str,
    lh_vertno: np.ndarray,
    rh_vertno: np.ndarray,
) -> tuple[np.ndarray, list[dict]]:
    """Map FreeSurfer .annot labels onto concatenated [lh_ico5, rh_ico5] vertices."""
    n_lh = len(lh_vertno)
    n_rh = len(rh_vertno)
    region_ids = np.zeros(n_lh + n_rh, dtype=np.int16)  # 0 = medial wall / unassigned

    regions: list[dict] = [
        {"id": 0, "code": "unknown", "name": "内侧壁", "name_en": "Medial Wall", "lobe": "内侧壁", "hemi": "both", "color": "#CBD5E1"}
    ]
    key_to_id: dict[str, int] = {}
    label_dir = Path(subjects_dir) / "fsaverage" / "label"

    for hemi, vertno, offset in [("lh", lh_vertno, 0), ("rh", rh_vertno, n_lh)]:
        vert_entry_idx, entries = read_fs_annot(label_dir / f"{hemi}.{parc}.annot")
        used_entries = set(int(x) for x in np.unique(vert_entry_idx[vertno]) if x >= 0)
        entry_to_rid = np.zeros(len(entries), dtype=np.int16)
        for entry_idx, (base, r, g, b) in enumerate(entries):
            if entry_idx not in used_entries:
                continue
            if base.lower() in ("unknown", "medial_wall", "medial.wall") or not base.strip("? "):
                continue

            if parc == "PALS_B12_Lobes":
                zh, hex_col = LOBE_INFO.get(base, (base, "#94A3B8"))
                key = base
                if key not in key_to_id:
                    rid = len(regions)
                    key_to_id[key] = rid
                    regions.append({
                        "id": rid, "code": base, "name": zh, "name_en": base.replace("LOBE.", "").title(),
                        "lobe": zh, "hemi": "both", "color": hex_col,
                    })
                entry_to_rid[entry_idx] = key_to_id[key]
            elif parc == "Yeo2011_7Networks_N1000":
                if base.startswith("FreeSurfer_Defined"):
                    continue
                zh, hex_col = YEO7_INFO.get(base, (base, "#94A3B8"))
                key = base
                if key not in key_to_id:
                    rid = len(regions)
                    key_to_id[key] = rid
                    regions.append({
                        "id": rid, "code": base, "name": zh, "name_en": base,
                        "lobe": zh, "hemi": "both", "color": hex_col,
                    })
                entry_to_rid[entry_idx] = key_to_id[key]
            else:
                hemi_zh = "左" if hemi == "lh" else "右"
                base_zh = DK_ZH.get(base, base)
                lobe_zh = DK_LOBE_GROUP.get(base, "其他")
                sr = int(round(r * 0.78 + 52))
                sg = int(round(g * 0.78 + 52))
                sb = int(round(b * 0.78 + 52))
                hex_col = f"#{sr:02X}{sg:02X}{sb:02X}"
                key = f"{hemi}:{base}"
                if key not in key_to_id:
                    rid = len(regions)
                    key_to_id[key] = rid
                    regions.append({
                        "id": rid, "code": f"{base}-{hemi}", "name": f"{hemi_zh}{base_zh}",
                        "name_en": f"{base} ({hemi.upper()})", "lobe": lobe_zh, "hemi": hemi, "color": hex_col,
                    })
                entry_to_rid[entry_idx] = key_to_id[key]

        mapped = np.where(vert_entry_idx[vertno] >= 0, entry_to_rid[np.maximum(vert_entry_idx[vertno], 0)], 0)
        region_ids[offset:offset + len(vertno)] = mapped

    return region_ids, regions


def main() -> None:
    subjects_dir = os.path.expanduser("~/mne_data/MNE-fsaverage-data")
    fs_root = Path(subjects_dir) / "fsaverage"

    # 1. Load existing GNM mapping & skin mesh and compute anatomical head frame (R, F, U) from LPA/RPA/Nasion
    doc = json.loads(MAPPING_PATH.read_text(encoding="utf-8-sig"))
    skin = np.load(SKIN_MESH_PATH)
    skin_verts = np.asarray(skin["vertices"], dtype=np.float64)

    lm = doc["core_mapping"]["landmarks_gnm"]
    el = doc["core_mapping"]["electrodes_gnm"]
    gnm_origin = (np.asarray(lm["lpa"], dtype=np.float64) + np.asarray(lm["rpa"], dtype=np.float64)) / 2.0
    gnm_right = np.asarray(lm["rpa"], dtype=np.float64) - np.asarray(lm["lpa"], dtype=np.float64)
    gnm_right /= np.linalg.norm(gnm_right)
    gnm_fwd = np.asarray(lm["nasion"], dtype=np.float64) - gnm_origin
    gnm_fwd -= np.dot(gnm_fwd, gnm_right) * gnm_right
    gnm_fwd /= np.linalg.norm(gnm_fwd)
    gnm_up = np.cross(gnm_right, gnm_fwd)
    gnm_up /= np.linalg.norm(gnm_up)
    gnm_basis = np.vstack([gnm_right, gnm_fwd, gnm_up])  # rows = R, F, U in GNM space

    def gnm_to_head(pts: np.ndarray) -> np.ndarray:
        return (pts - gnm_origin) @ gnm_basis.T

    def head_to_gnm(pts_h: np.ndarray) -> np.ndarray:
        return gnm_origin + pts_h @ gnm_basis

    # Cranial vault bounds of gnm_skin_mesh from actual 10-20 scalp electrodes
    el_h = {k: gnm_to_head(np.asarray(v, dtype=np.float64)) for k, v in el.items()}
    gnm_r_min, gnm_r_max = float(el_h["T3"][0]), float(el_h["T4"][0])
    gnm_f_min = float(el_h["Oz"][1])
    gnm_f_max = float(max(el_h["Fp1"][1], el_h["Fp2"][1]))
    gnm_u_max = float(el_h["Cz"][2])
    gnm_u_base = float(min(el_h["T3"][2], el_h["T4"][2], el_h["Oz"][2]))

    # 2. Load fsaverage mri->head transform and fsaverage outer_skin to measure exact anatomical scalp ratios
    head_to_mri = mne.read_trans(fs_root / "bem" / "fsaverage-trans.fif", verbose=False)
    mri_to_head = invert_transform(head_to_mri)
    fs_skin_mm, _ = read_fs_surface(fs_root / "bem" / "outer_skin.surf")
    fs_skin_h = apply_trans(mri_to_head, fs_skin_mm / 1000.0)
    # Cranial cap of fsaverage outer_skin (above LPA-RPA plane, excluding lower nose tip)
    fs_cap = fs_skin_h[fs_skin_h[:, 2] >= 0.025]
    fs_r_min, fs_r_max = float(fs_cap[:, 0].min()), float(fs_cap[:, 0].max())
    fs_f_min = float(fs_cap[:, 1].min())
    fs_f_max = float(np.percentile(fs_cap[:, 1], 99.2))
    fs_u_max = float(fs_cap[:, 2].max())

    # 3. Load decimated ico-5 topology + high-res pial coordinates + sulcal depth
    src = mne.read_source_spaces(fs_root / "bem" / "fsaverage-ico-5-src.fif", verbose=False)
    lh_vertno = src[0]["vertno"]
    rh_vertno = src[1]["vertno"]
    lh_tris_orig = src[0]["use_tris"]
    rh_tris_orig = src[1]["use_tris"]

    lh_map = np.full(src[0]["np"], -1, dtype=np.int32)
    lh_map[lh_vertno] = np.arange(len(lh_vertno), dtype=np.int32)
    lh_tris = lh_map[lh_tris_orig]

    rh_map = np.full(src[1]["np"], -1, dtype=np.int32)
    rh_map[rh_vertno] = np.arange(len(rh_vertno), dtype=np.int32)
    rh_tris = rh_map[rh_tris_orig] + len(lh_vertno)

    assert (lh_tris >= 0).all() and (rh_tris >= 0).all(), "Failed to remap ico-5 triangle indices!"

    lh_pial_mm, _ = read_fs_surface(fs_root / "surf" / "lh.pial")
    rh_pial_mm, _ = read_fs_surface(fs_root / "surf" / "rh.pial")
    lh_sulc = read_fs_curv(fs_root / "surf" / "lh.sulc")
    rh_sulc = read_fs_curv(fs_root / "surf" / "rh.sulc")

    pial_mri_m = np.vstack([lh_pial_mm[lh_vertno], rh_pial_mm[rh_vertno]]) / 1000.0
    sulc = np.concatenate([lh_sulc[lh_vertno], rh_sulc[rh_vertno]]).astype(np.float32)

    # Transform fsaverage MRI -> fsaverage head -> globally optimal fit inside gnm_skin_mesh cranial vault.
    # A gentle -2 deg pitch aligns the dorsal crown directly beneath Cz while keeping the orbitofrontal
    # base clear of the brow recess; binary search finds the largest uniform-proportion scale that keeps
    # every single one of the 20,484 pial vertices >= 3.2 mm strictly inside the scalp shell (0 protrusion).
    pial_fs_h = apply_trans(mri_to_head, pial_mri_m)
    pial_centroid = pial_fs_h.mean(axis=0)
    pial_centered = pial_fs_h - pial_centroid

    pitch_rad = np.deg2rad(-2.0)
    cp, sp = float(np.cos(pitch_rad)), float(np.sin(pitch_rad))
    pial_rot = np.column_stack([
        pial_centered[:, 0],
        pial_centered[:, 1] * cp - pial_centered[:, 2] * sp,
        pial_centered[:, 1] * sp + pial_centered[:, 2] * cp,
    ])

    gnm_center_r = 0.5 * (gnm_r_min + gnm_r_max)
    target_center_f = -0.004
    target_center_u = 0.056
    skin_h = gnm_to_head(skin_verts)
    cranial_center_h = np.array([gnm_center_r, 0.0, 0.042])
    skin_tree = cKDTree(skin_h)

    lo, hi = 0.85, 1.30
    for _ in range(16):
        s = 0.5 * (lo + hi)
        cand = np.column_stack([
            gnm_center_r + pial_rot[:, 0] * (s * 1.04),
            target_center_f + pial_rot[:, 1] * (s * 1.01),
            target_center_u + pial_rot[:, 2] * (s * 1.07),
        ])
        _, idx = skin_tree.query(cand)
        cr = np.linalg.norm(cand - cranial_center_h, axis=1)
        sr = np.linalg.norm(skin_h[idx] - cranial_center_h, axis=1)
        if float(np.min(sr - cr)) >= 0.0032:
            lo = s
        else:
            hi = s

    pial_gnm_h = np.column_stack([
        gnm_center_r + pial_rot[:, 0] * (lo * 1.04),
        target_center_f + pial_rot[:, 1] * (lo * 1.01),
        target_center_u + pial_rot[:, 2] * (lo * 1.07),
    ])
    dists, nearest_idx = skin_tree.query(pial_gnm_h)
    cortex_r = np.linalg.norm(pial_gnm_h - cranial_center_h, axis=1)
    skin_r = np.linalg.norm(skin_h[nearest_idx] - cranial_center_h, axis=1)
    min_clearance = float(np.min(skin_r - cortex_r))

    pial_gnm = head_to_gnm(pial_gnm_h)
    protruding = int(np.sum(cortex_r >= skin_r))
    print(f"Cortex in GNM head-frame min/max (R,F,U): {np.round(pial_gnm_h.min(0),4)} .. {np.round(pial_gnm_h.max(0),4)}")
    print(f"Electrodes bounds: R=[{gnm_r_min:.4f},{gnm_r_max:.4f}] F=[{gnm_f_min:.4f},{gnm_f_max:.4f}] Cz={gnm_u_max:.4f}")
    print(f"Cranial vault fit check: protruding={protruding}, min_clearance={min_clearance:.4f}, mean_skin_dist={dists.mean():.4f}")

    triangles = np.vstack([lh_tris, rh_tris]).astype(np.int32)
    vertices_f32 = pial_gnm.astype(np.float32)

    # 4. Extract 3 standard atlases
    lobes_ids, lobes_regions = load_atlas_for_ico5(subjects_dir, "PALS_B12_Lobes", lh_vertno, rh_vertno)
    dk_ids, dk_regions = load_atlas_for_ico5(subjects_dir, "aparc", lh_vertno, rh_vertno)
    yeo7_ids, yeo7_regions = load_atlas_for_ico5(subjects_dir, "Yeo2011_7Networks_N1000", lh_vertno, rh_vertno)

    np.savez_compressed(
        OUT_NPZ,
        vertices=vertices_f32,
        triangles=triangles,
        sulc=sulc,
        atlas_lobes=lobes_ids,
        atlas_dk=dk_ids,
        atlas_yeo7=yeo7_ids,
    )

    npz_sha256 = sha256(OUT_NPZ.read_bytes()).hexdigest()
    meta = {
        "schema_version": 1,
        "model_id": "fsaverage-ico5-pial-gnm",
        "model_version": "v1",
        "source_subject": "fsaverage",
        "source_surface": "pial (ico-5 decimated)",
        "target_coordinate_frame": "gnm_native",
        "vertex_count": int(vertices_f32.shape[0]),
        "triangle_count": int(triangles.shape[0]),
        "lh_vertex_count": int(len(lh_vertno)),
        "rh_vertex_count": int(len(rh_vertno)),
        "npz_sha256": npz_sha256,
        "atlases": {
            "lobes": {"id": "lobes", "name": "大脑脑叶分区 (PALS-B12)", "regions": lobes_regions},
            "dk": {"id": "dk", "name": "Desikan-Killiany (68区)", "regions": dk_regions},
            "yeo7": {"id": "yeo7", "name": "Yeo-7 功能网络", "regions": yeo7_regions},
        },
    }
    OUT_JSON.write_text(json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Wrote {OUT_NPZ} ({OUT_NPZ.stat().st_size / 1024:.1f} KB) and {OUT_JSON} ({OUT_JSON.stat().st_size / 1024:.1f} KB)")
    print(f"Vertices: {vertices_f32.shape[0]}, Triangles: {triangles.shape[0]}, NPZ SHA-256: {npz_sha256}")


if __name__ == "__main__":
    main()
