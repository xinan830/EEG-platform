"""Visual payloads for WPF ICA review (2D topomap + IC waveform) and cleaned-waveform overlay preview."""

from __future__ import annotations

import math
from typing import Any, Sequence

import mne
import numpy as np

from app.scientific.contracts.preprocessing import ArtifactRejectionSpec, ArtifactRemovalSpec, BadChannelSpec
from app.services.electrode_locations import resolve_electrode_labels


def _strip_montage_suffix(label: str) -> str:
    return label.split("-", 1)[0].strip()


def _build_idw_grid(points: list[tuple[str, float, float, float]], grid_size: int = 33, head_radius: float = 1.0) -> dict[str, object]:
    axis = np.linspace(-head_radius, head_radius, grid_size)
    grid_x, grid_y = np.meshgrid(axis, axis)
    grid_pts = np.column_stack((grid_x.ravel(), grid_y.ravel()))
    locs = np.asarray([(x, y) for _, x, y, _ in points], dtype=np.float64)
    vals = np.asarray([v for _, _, _, v in points], dtype=np.float64)
    dists = np.linalg.norm(grid_pts[:, None, :] - locs[None, :, :], axis=2)
    exact = dists < 1e-12
    weights = 1.0 / np.maximum(dists, 1e-12) ** 2
    weights /= weights.sum(axis=1, keepdims=True)
    interp = np.where(exact.any(axis=1), vals[np.argmax(exact, axis=1)], weights @ vals)
    inside = np.hypot(grid_pts[:, 0], grid_pts[:, 1]) <= head_radius
    rows = [
        [round(float(interp[r * grid_size + c]), 5) if inside[r * grid_size + c] else None for c in range(grid_size)]
        for r in range(grid_size)
    ]
    return {
        "method": "inverse_distance_weighted_v1",
        "width": grid_size,
        "height": grid_size,
        "x_min": -head_radius,
        "x_max": head_radius,
        "y_min": -head_radius,
        "y_max": head_radius,
        "head_radius": head_radius,
        "values": rows,
    }


def _build_ic_topomap_field(
    component_index: int,
    channel_names: Sequence[str],
    weights: np.ndarray,
    resolved_coords: dict[str, tuple[float, float]],
    *,
    grid_size: int = 33,
) -> dict[str, object]:
    # Normalize weights symmetrically to [-1, 1] for crisp bipolar scalp heatmap display.
    peak = float(np.max(np.abs(weights))) if weights.size > 0 else 0.0
    scaled = weights / peak if peak > 1e-12 else weights

    points: list[dict[str, object]] = []
    usable: list[tuple[str, float, float, float]] = []
    seen_coords: set[tuple[float, float]] = set()
    included: list[str] = []
    excluded: list[str] = []

    for ch_name, val in zip(channel_names, scaled, strict=True):
        base_label = _strip_montage_suffix(str(ch_name))
        xy = resolved_coords.get(base_label.casefold())
        num = float(val) if math.isfinite(float(val)) else None
        if xy is None or num is None:
            excluded.append(str(ch_name))
            points.append({
                "channel": str(ch_name), "electrode_label": base_label or None,
                "x": None, "y": None, "value": num, "status": "excluded",
                "reason": "electrode_location_unavailable", "quality_status": "unknown", "quality_reasons": [],
            })
            continue
        x, y = xy
        key = (round(x, 6), round(y, 6))
        if key in seen_coords or math.hypot(x, y) > 0.96:
            excluded.append(str(ch_name))
            points.append({
                "channel": str(ch_name), "electrode_label": base_label,
                "x": x, "y": y, "value": num, "status": "excluded",
                "reason": "duplicate_or_out_of_bounds", "quality_status": "unknown", "quality_reasons": [],
            })
            continue
        seen_coords.add(key)
        included.append(str(ch_name))
        usable.append((str(ch_name), x, y, num))
        points.append({
            "channel": str(ch_name), "electrode_label": base_label,
            "x": x, "y": y, "value": round(num, 5), "status": "included",
            "reason": None, "quality_status": "good", "quality_reasons": [],
        })

    if len(usable) < 3:
        return {
            "field_id": f"ic_{component_index:02d}", "label": f"IC{component_index:02d}",
            "unit": "a.u.", "frequency_hz": None, "points": points, "grid": None,
            "status": "unavailable", "included_channels": included, "excluded_channels": excluded,
            "failure": {"code": "TOPOMAP_INSUFFICIENT_POINTS", "message": "可用电极坐标不足 3 个"},
            "provenance": {},
        }

    return {
        "field_id": f"ic_{component_index:02d}", "label": f"IC{component_index:02d}",
        "unit": "a.u.", "frequency_hz": None, "points": points,
        "grid": _build_idw_grid(usable, grid_size=grid_size),
        "status": "ready", "included_channels": included, "excluded_channels": excluded,
        "failure": None, "provenance": {"source": "mne_ica_mixing_matrix"},
    }


def attach_ica_component_visuals(
    report: dict[str, object],
    fitted_ica: Any,
    data_v: np.ndarray,
    *,
    preview_seconds: float = 10.0,
    preview_points: int = 240,
    grid_size: int = 33,
) -> dict[str, object]:
    """Enrich ICA inspection components with 2D scalp topomaps and downsampled IC waveforms."""
    names = tuple(fitted_ica.channel_names)
    mixing = np.asarray(fitted_ica.ica.get_components(), dtype=np.float64)
    base_labels = [_strip_montage_suffix(name) for name in names]
    resolved_coords: dict[str, tuple[float, float]] = {}
    for item in resolve_electrode_labels(base_labels):
        coords = item.get("map_coordinates")
        if item.get("status") == "available" and isinstance(coords, list) and len(coords) == 2:
            x, y = float(coords[0]), float(coords[1])
            if math.isfinite(x) and math.isfinite(y):
                resolved_coords[str(item.get("requested_label", "")).casefold()] = (x, y)

    # Extract up to preview_seconds of finite source activations via MNE's public get_sources().
    finite_mask = np.all(np.isfinite(data_v), axis=1)
    finite_block = data_v[finite_mask]
    max_samples = max(16, int(round(preview_seconds * float(fitted_ica.sampling_rate_hz))))
    snippet = finite_block[:max_samples]
    preview_duration_s = round(snippet.shape[0] / float(fitted_ica.sampling_rate_hz), 3) if snippet.size else 0.0
    sources_ds: np.ndarray | None = None
    if snippet.shape[0] >= 16:
        raw_snip = mne.io.RawArray(np.ascontiguousarray(snippet.T, dtype=np.float64), fitted_ica.info, verbose=False)
        src_arr = np.asarray(fitted_ica.ica.get_sources(raw_snip).get_data(), dtype=np.float64)
        idx = np.linspace(0, src_arr.shape[1] - 1, num=min(preview_points, src_arr.shape[1]), dtype=int)
        sources_ds = src_arr[:, idx]

    enriched: list[dict[str, object]] = []
    for row in report.get("components", []):
        comp_idx = int(row["component_index"])
        topo = _build_ic_topomap_field(comp_idx, names, mixing[:, comp_idx], resolved_coords, grid_size=grid_size)
        wave: list[float] = []
        if sources_ds is not None and comp_idx < sources_ds.shape[0]:
            sig = sources_ds[comp_idx]
            std = float(np.std(sig))
            normed = (sig - float(np.mean(sig))) / std if std > 1e-12 else sig * 0.0
            wave = [round(float(v), 4) for v in normed]
        enriched.append({
            **row,
            "topomap": topo,
            "preview_waveform": wave,
            "preview_duration_s": preview_duration_s,
        })
    report["components"] = enriched
    return report


def build_preprocessing_preview_window(
    recordings: Any,
    recording: Any,
    *,
    start_s: float,
    window_s: float,
    montage_id: str | None = None,
    low_hz: float = 1.0,
    high_hz: float = 50.0,
    notch_hz: float | None = 50.0,
    bad_channel_spec: BadChannelSpec | None = None,
    artifact_rejection_spec: ArtifactRejectionSpec | None = None,
    artifact_removal_spec: ArtifactRemovalSpec | None = None,
    max_points: int = 1200,
) -> dict[str, object]:
    """Return a downsampled cleaned viewport window (uV, NaN -> null) reusing continuous/ICA caches."""
    target_recording = recording
    if montage_id is not None:
        target_recording = recordings.select_analysis_montage(recording, montage_id)

    norm_bad = bad_channel_spec.normalized() if bad_channel_spec is not None else BadChannelSpec()
    norm_art = artifact_rejection_spec.normalized() if artifact_rejection_spec is not None else ArtifactRejectionSpec()
    norm_rem = artifact_removal_spec.normalized() if artifact_removal_spec is not None else ArtifactRemovalSpec()

    prepared = recordings.offline_scientific_preprocessing.prepare_continuous(
        target_recording,
        low_hz=low_hz,
        high_hz=high_hz,
        notch_hz=notch_hz,
        bad_channel_spec=norm_bad if norm_bad.is_active else None,
        artifact_rejection_spec=norm_art if norm_art.is_active else None,
        artifact_removal_spec=norm_rem if norm_rem.is_active else None,
    )
    sfreq = float(prepared.sfreq)
    total = int(prepared.data.shape[0])
    s_idx = max(0, min(total, int(math.floor(float(start_s) * sfreq))))
    e_idx = max(s_idx, min(total, s_idx + max(1, int(round(float(window_s) * sfreq)))))
    if e_idx <= s_idx:
        raise ValueError("预览窗口超出录音范围")

    window_data = np.asarray(prepared.data[s_idx:e_idx], dtype=np.float64)
    n_pts = window_data.shape[0]
    if n_pts > max_points:
        sample_idx = np.linspace(0, n_pts - 1, num=max_points, dtype=int)
    else:
        sample_idx = np.arange(n_pts, dtype=int)

    times_s = [round((s_idx + int(i)) / sfreq, 6) for i in sample_idx]
    traces_uv: dict[str, list[float | None]] = {}
    for col_i, ch_name in enumerate(prepared.channel_names):
        vals = window_data[sample_idx, col_i] * 1e6
        traces_uv[str(ch_name)] = [round(float(v), 4) if math.isfinite(float(v)) else None for v in vals]

    bad_set = {ch.casefold() for ch in norm_bad.channels}
    interpolated = [
        str(ch) for ch in prepared.channel_names
        if norm_bad.policy == "interpolate"
        and (ch.casefold() in bad_set or _strip_montage_suffix(str(ch)).casefold() in bad_set)
    ]
    excluded = list(norm_bad.channels) if norm_bad.policy == "exclude" else []
    rem_ev = prepared.artifact_removal_evidence or {}
    return {
        "recording_id": recording.id,
        "montage_id": montage_id,
        "start_s": round(s_idx / sfreq, 6),
        "end_s": round(e_idx / sfreq, 6),
        "sfreq_hz": sfreq,
        "channels": list(prepared.channel_names),
        "interpolated_channels": interpolated,
        "excluded_channels": excluded,
        "times_s": times_s,
        "traces_uv": traces_uv,
        "ica_model_reused": bool(rem_ev.get("ica_model_reused", False)),
        "artifact_removal_status": str(rem_ev.get("status", "disabled")),
    }
