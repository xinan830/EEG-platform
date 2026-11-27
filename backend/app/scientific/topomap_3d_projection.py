"""Dynamic MNE head-frame to GNM scalp-mesh 3D electrode projection."""

from __future__ import annotations

from dataclasses import dataclass

import mne
import numpy as np


@dataclass(frozen=True)
class CoreMontageSpec:
    name: str
    display_name: str
    category: str
    is_default: bool = False


# Curated 15 core EEG montages covering standard 10-20/10-10/10-05 and high-density research caps.
CORE_MONTAGE_CATALOG: tuple[CoreMontageSpec, ...] = (
    CoreMontageSpec("colin27_1020", "标准 10-20 · Colin27（系统默认）", "国际标准系列", is_default=True),
    CoreMontageSpec("colin27_1005", "标准 10-05 · Colin27（高密度）", "国际标准系列"),
    CoreMontageSpec("fsaverage_1020", "标准 10-20 · fsaverage", "国际标准系列"),
    CoreMontageSpec("fsaverage_1010", "标准 10-10 · fsaverage", "国际标准系列"),
    CoreMontageSpec("fsaverage_1005", "标准 10-05 · fsaverage（高密度）", "国际标准系列"),
    CoreMontageSpec("biosemi32", "BioSemi 32", "BioSemi 系列"),
    CoreMontageSpec("biosemi64", "BioSemi 64", "BioSemi 系列"),
    CoreMontageSpec("biosemi128", "BioSemi 128", "BioSemi 系列"),
    CoreMontageSpec("biosemi256", "BioSemi 256", "BioSemi 系列"),
    CoreMontageSpec("GSN-HydroCel-64_1.0", "GSN-HydroCel 64", "GSN-HydroCel 系列"),
    CoreMontageSpec("GSN-HydroCel-128", "GSN-HydroCel 128", "GSN-HydroCel 系列"),
    CoreMontageSpec("GSN-HydroCel-256", "GSN-HydroCel 256", "GSN-HydroCel 系列"),
    CoreMontageSpec("easycap-M1", "EasyCap M1（74 导）", "EasyCap 系列"),
    CoreMontageSpec("easycap-M10", "EasyCap M10（61 导）", "EasyCap 系列"),
    CoreMontageSpec("brainproducts-RNP-BA-128", "BrainProducts RNP-BA 128", "BrainProducts 系列"),
)

CORE_MONTAGE_BY_NAME: dict[str, CoreMontageSpec] = {item.name: item for item in CORE_MONTAGE_CATALOG}
GNM_SCALP_CENTER_UP_OFFSET = 0.035


def montage_head_positions(montage_name: str) -> dict[str, tuple[float, float, float]]:
    """Load an MNE built-in montage and return finite channel positions in MNE head frame (meters)."""
    montage = mne.channels.make_standard_montage(montage_name)
    positions = montage.get_positions()
    if positions.get("coord_frame") != "head":
        montage = mne.channels.transform_to_head(montage.copy())
        positions = montage.get_positions()
    if positions.get("coord_frame") != "head":
        raise ValueError(f"Montage '{montage_name}' could not be converted to head coordinate frame")

    resolved: dict[str, tuple[float, float, float]] = {}
    for ch_name, xyz in positions["ch_pos"].items():
        coords = np.asarray(xyz, dtype=np.float64)
        if coords.shape != (3,) or not np.all(np.isfinite(coords)):
            raise ValueError(f"Montage '{montage_name}' contains non-finite coordinates for '{ch_name}'")
        resolved[str(ch_name)] = (float(coords[0]), float(coords[1]), float(coords[2]))
    if not resolved:
        raise ValueError(f"Montage '{montage_name}' contains no electrode positions")
    return resolved


def fit_cap_sphere(points_m: np.ndarray) -> tuple[np.ndarray, float]:
    """Fit a sphere center and radius (m) to an EEG cap point cloud via least squares."""
    pts = np.asarray(points_m, dtype=np.float64)
    if pts.ndim != 2 or pts.shape[1] != 3 or len(pts) < 4:
        return np.zeros(3, dtype=np.float64), float(np.median(np.linalg.norm(pts, axis=1)))
    design = np.column_stack([2.0 * pts, np.ones(len(pts), dtype=np.float64)])
    rhs = np.sum(pts * pts, axis=1)
    solved, *_ = np.linalg.lstsq(design, rhs, rcond=None)
    center = solved[:3]
    radius_sq = float(solved[3] + np.dot(center, center))
    if not np.all(np.isfinite(center)) or radius_sq <= 1e-6:
        return np.zeros(3, dtype=np.float64), float(np.median(np.linalg.norm(pts, axis=1)))
    return center, float(np.sqrt(radius_sq))


NON_SCALP_ALIGN_SKIP = {"a1", "a2", "m1", "m2", "iz"}
CARDINAL_TEN_TWENTY_ANCHORS = frozenset({"fp1", "fp2", "fpz", "fz", "cz", "pz", "oz", "t7", "t8"})


def is_ten_twenty_montage(names: list[str] | tuple[str, ...]) -> bool:
    """Identify standard 10-20/10-10/10-05 nomenclature montages vs sector-named caps (BioSemi/EGI)."""
    return len(CARDINAL_TEN_TWENTY_ANCHORS & {name.casefold() for name in names}) >= 3


def _kabsch_align_directions(
    names: list[str],
    dirs_local: np.ndarray,
    *,
    head_origin_gnm: np.ndarray,
    right_axis_gnm: np.ndarray,
    forward_axis_gnm: np.ndarray,
    up_axis_gnm: np.ndarray,
    anchor_gnm_by_channel: dict[str, tuple[float, float, float]] | None,
) -> np.ndarray:
    """Align 10-20/10-05 cap orientation to GNM scalp anchors via pure 3D rotation."""
    if not anchor_gnm_by_channel:
        return dirs_local
    folded = {name.casefold() for name in names}
    if len(CARDINAL_TEN_TWENTY_ANCHORS & folded) < 5:
        return dirs_local

    folded_anchors = {key.casefold(): np.asarray(val, dtype=np.float64) for key, val in anchor_gnm_by_channel.items()}
    scalp_origin = np.asarray(head_origin_gnm, dtype=np.float64) + GNM_SCALP_CENTER_UP_OFFSET * np.asarray(
        up_axis_gnm, dtype=np.float64,
    )
    basis = np.column_stack([right_axis_gnm, forward_axis_gnm, up_axis_gnm])

    src_pairs: list[np.ndarray] = []
    dst_pairs: list[np.ndarray] = []
    for idx, name in enumerate(names):
        key = name.casefold()
        if key in NON_SCALP_ALIGN_SKIP or key not in folded_anchors:
            continue
        dst_vec = (folded_anchors[key] - scalp_origin) @ basis
        dst_norm = float(np.linalg.norm(dst_vec))
        if dst_norm > 1e-6:
            src_pairs.append(dirs_local[idx])
            dst_pairs.append(dst_vec / dst_norm)

    if len(src_pairs) < 6:
        return dirs_local
    src_mat = np.asarray(src_pairs, dtype=np.float64)
    dst_mat = np.asarray(dst_pairs, dtype=np.float64)
    cov = dst_mat.T @ src_mat
    u_mat, _, vt_mat = np.linalg.svd(cov)
    rot = u_mat @ vt_mat
    if np.linalg.det(rot) < 0:
        u_mat[:, -1] *= -1.0
        rot = u_mat @ vt_mat
    return dirs_local @ rot.T


def normalized_cap_directions(
    points_head_m: np.ndarray,
    *,
    channel_names: list[str] | None = None,
    head_origin_gnm: np.ndarray | None = None,
    right_axis_gnm: np.ndarray | None = None,
    forward_axis_gnm: np.ndarray | None = None,
    up_axis_gnm: np.ndarray | None = None,
    anchor_gnm_by_channel: dict[str, tuple[float, float, float]] | None = None,
) -> np.ndarray:
    """Compute upright unit scalp directions centered on the upper-cap fitted sphere."""
    pts = np.asarray(points_head_m, dtype=np.float64)
    if pts.ndim != 2 or pts.shape[1] != 3 or len(pts) == 0:
        raise ValueError("points_head_m must be a non-empty (N, 3) array")

    upper = pts[pts[:, 2] >= np.percentile(pts[:, 2], 15)]
    cap_center, _ = fit_cap_sphere(upper if len(upper) >= 4 else pts)
    offsets = pts - cap_center
    norms = np.linalg.norm(offsets, axis=1, keepdims=True)
    dirs_local = offsets / np.where(norms > 1e-9, norms, 1.0)
    if (
        channel_names is not None
        and len(channel_names) == len(pts)
        and head_origin_gnm is not None
        and right_axis_gnm is not None
        and forward_axis_gnm is not None
        and up_axis_gnm is not None
    ):
        dirs_local = _kabsch_align_directions(
            channel_names,
            dirs_local,
            head_origin_gnm=head_origin_gnm,
            right_axis_gnm=right_axis_gnm,
            forward_axis_gnm=forward_axis_gnm,
            up_axis_gnm=up_axis_gnm,
            anchor_gnm_by_channel=anchor_gnm_by_channel,
        )
    return dirs_local


def project_directions_to_2d_aep(dirs_local: np.ndarray, *, max_radius: float = 0.88) -> np.ndarray:
    """Linear azimuthal equidistant 2D scalp projection (+X right, +Y down on canvas)."""
    theta = np.arccos(np.clip(dirs_local[:, 2], -1.0, 1.0))
    xy_norm = np.hypot(dirs_local[:, 0], dirs_local[:, 1])
    xy_dir = np.zeros((len(dirs_local), 2), dtype=np.float64)
    nonzero = xy_norm > 1e-9
    xy_dir[nonzero, 0] = dirs_local[nonzero, 0] / xy_norm[nonzero]
    xy_dir[nonzero, 1] = -dirs_local[nonzero, 1] / xy_norm[nonzero]
    theta_max = max(float(np.max(theta)), 1e-6)
    return xy_dir * (theta[:, None] / theta_max * max_radius)


EAR_CLEARANCE_CENTER_F = -0.020
EAR_CLEARANCE_RADIUS_F = 0.022
EAR_CLEARANCE_CENTER_U = -0.005
EAR_CLEARANCE_RADIUS_U = 0.027
EAR_CLEARANCE_MIN_ABS_R = 0.064
EAR_EXEMPT_LANDMARKS = frozenset({"a1", "a2", "m1", "m2"})
EAR_MIN_NEIGHBOR_SEPARATION_M = 0.006


def is_in_gnm_ear_zone(rfu: np.ndarray) -> np.ndarray:
    """Return boolean mask for GNM (R, F, U) coordinates lying inside the bilateral outer ear pinna."""
    df = (rfu[:, 1] - EAR_CLEARANCE_CENTER_F) / EAR_CLEARANCE_RADIUS_F
    du = (rfu[:, 2] - EAR_CLEARANCE_CENTER_U) / EAR_CLEARANCE_RADIUS_U
    return (np.abs(rfu[:, 0]) > EAR_CLEARANCE_MIN_ABS_R) & ((df * df + du * du) < 1.0)


def _raycast_scalp_directions(
    dirs_gnm: np.ndarray,
    *,
    scalp_origin: np.ndarray,
    head_origin_gnm: np.ndarray,
    up_axis_gnm: np.ndarray,
    verts: np.ndarray,
    tris: np.ndarray,
) -> np.ndarray:
    v0 = verts[tris[:, 0]]
    e1 = verts[tris[:, 1]] - v0
    e2 = verts[tris[:, 2]] - v0
    s = scalp_origin - v0

    vertex_up = (verts - head_origin_gnm) @ up_axis_gnm
    vertex_dist = np.linalg.norm(verts - scalp_origin, axis=1)
    head_mask = (vertex_up > -0.075) & (vertex_dist >= 0.055) & (vertex_dist <= 0.135)
    head_verts = verts[head_mask] if np.any(head_mask) else verts
    head_off = head_verts - scalp_origin
    head_norms = np.linalg.norm(head_off, axis=1, keepdims=True)
    head_dirs = head_off / np.where(head_norms > 1e-9, head_norms, 1.0)

    projected = np.empty((len(dirs_gnm), 3), dtype=np.float64)
    for idx, direction in enumerate(dirs_gnm):
        h = np.cross(direction, e2)
        det = np.einsum("ij,ij->i", e1, h)
        valid = np.abs(det) > 1e-9
        inv_det = np.zeros_like(det)
        inv_det[valid] = 1.0 / det[valid]

        u = inv_det * np.einsum("ij,ij->i", s, h)
        valid &= (u >= -1e-6) & (u <= 1.0 + 1e-6)

        q = np.cross(s, e1)
        v_coord = inv_det * np.einsum("ij,j->i", q, direction)
        valid &= (v_coord >= -1e-6) & (u + v_coord <= 1.0 + 1e-6)

        t = inv_det * np.einsum("ij,ij->i", e2, q)
        valid &= (t >= 0.045) & (t <= 0.135)

        if np.any(valid):
            projected[idx] = scalp_origin + float(np.min(t[valid])) * direction
        else:
            projected[idx] = head_verts[int(np.argmax(head_dirs @ direction))]
    return projected


def deflect_points_around_ear_pinna(
    points_gnm: np.ndarray,
    *,
    channel_names: list[str] | tuple[str, ...],
    is_ten_twenty: bool,
    head_origin_gnm: np.ndarray,
    right_axis_gnm: np.ndarray,
    forward_axis_gnm: np.ndarray,
    up_axis_gnm: np.ndarray,
    vertices_gnm: np.ndarray,
    triangles: np.ndarray,
) -> np.ndarray:
    """Route scalp electrodes that land inside the GNM outer ear onto nearby non-ear scalp sites."""
    pts = np.asarray(points_gnm, dtype=np.float64).copy()
    head_origin = np.asarray(head_origin_gnm, dtype=np.float64)
    basis = np.column_stack([right_axis_gnm, forward_axis_gnm, up_axis_gnm])
    rfu = (pts - head_origin) @ basis

    in_ear = is_in_gnm_ear_zone(rfu)
    if is_ten_twenty:
        exempt = np.array([name.casefold() in EAR_EXEMPT_LANDMARKS for name in channel_names], dtype=bool)
        in_ear &= ~exempt
    if not np.any(in_ear):
        return pts

    verts = np.asarray(vertices_gnm, dtype=np.float64)
    v_rfu = (verts - head_origin) @ basis
    v_ear = is_in_gnm_ear_zone(v_rfu)
    non_ear_verts = verts[~v_ear] if np.any(~v_ear) else verts

    placed = list(pts[~in_ear])
    for pt_idx in np.flatnonzero(in_ear):
        r, f, u = rfu[pt_idx]
        df = (f - EAR_CLEARANCE_CENTER_F) / EAR_CLEARANCE_RADIUS_F
        du = (u - EAR_CLEARANCE_CENTER_U) / EAR_CLEARANCE_RADIUS_U
        # Route lower/mid ear points horizontally around the front/back rather than crowding Row 7 above.
        if u < 0.012:
            df *= 1.4
            if abs(df) < 0.15:
                df = 0.25 if f >= EAR_CLEARANCE_CENTER_F else -0.25
        ell = float(np.hypot(df, du))
        if ell < 1e-6:
            df, du, ell = 0.0, 1.0, 1.0
        cleared_rfu = np.array([
            np.sign(r) * min(abs(r), 0.074),
            EAR_CLEARANCE_CENTER_F + (df / ell) * EAR_CLEARANCE_RADIUS_F * 1.06,
            EAR_CLEARANCE_CENTER_U + (du / ell) * EAR_CLEARANCE_RADIUS_U * 1.06,
        ])
        target_gnm = head_origin + cleared_rfu @ basis.T
        dists_to_target = np.linalg.norm(non_ear_verts - target_gnm, axis=1)
        candidates = np.argsort(dists_to_target)[:48]
        chosen = non_ear_verts[candidates[0]]
        if placed:
            placed_arr = np.asarray(placed, dtype=np.float64)
            for cand_idx in candidates:
                cand = non_ear_verts[cand_idx]
                if float(np.min(np.linalg.norm(placed_arr - cand, axis=1))) >= EAR_MIN_NEIGHBOR_SEPARATION_M:
                    chosen = cand
                    break
        pts[pt_idx] = chosen
        placed.append(chosen)
    return pts


def project_head_points_to_gnm_mesh(
    points_head_m: np.ndarray,
    *,
    head_origin_gnm: np.ndarray,
    right_axis_gnm: np.ndarray,
    forward_axis_gnm: np.ndarray,
    up_axis_gnm: np.ndarray,
    vertices_gnm: np.ndarray,
    triangles: np.ndarray,
    channel_names: list[str] | None = None,
    anchor_gnm_by_channel: dict[str, tuple[float, float, float]] | None = None,
    dirs_local: np.ndarray | None = None,
) -> np.ndarray:
    """Project MNE head-frame coordinates onto the outer GNM cranial scalp surface."""
    if dirs_local is None:
        dirs_local = normalized_cap_directions(
            points_head_m,
            channel_names=channel_names,
            head_origin_gnm=head_origin_gnm,
            right_axis_gnm=right_axis_gnm,
            forward_axis_gnm=forward_axis_gnm,
            up_axis_gnm=up_axis_gnm,
            anchor_gnm_by_channel=anchor_gnm_by_channel,
        )

    basis = np.column_stack([right_axis_gnm, forward_axis_gnm, up_axis_gnm])
    dirs_gnm = dirs_local @ basis.T
    dirs_gnm /= np.linalg.norm(dirs_gnm, axis=1, keepdims=True)

    head_origin = np.asarray(head_origin_gnm, dtype=np.float64)
    up_axis = np.asarray(up_axis_gnm, dtype=np.float64)
    scalp_origin = head_origin + GNM_SCALP_CENTER_UP_OFFSET * up_axis
    verts = np.asarray(vertices_gnm, dtype=np.float64)
    tris = np.asarray(triangles, dtype=np.int64)
    projected = _raycast_scalp_directions(
        dirs_gnm,
        scalp_origin=scalp_origin,
        head_origin_gnm=head_origin,
        up_axis_gnm=up_axis,
        verts=verts,
        tris=tris,
    )
    if channel_names is not None and len(channel_names) == len(projected):
        projected = deflect_points_around_ear_pinna(
            projected,
            channel_names=channel_names,
            is_ten_twenty=is_ten_twenty_montage(channel_names),
            head_origin_gnm=head_origin,
            right_axis_gnm=right_axis_gnm,
            forward_axis_gnm=forward_axis_gnm,
            up_axis_gnm=up_axis,
            vertices_gnm=verts,
            triangles=tris,
        )
    return projected
