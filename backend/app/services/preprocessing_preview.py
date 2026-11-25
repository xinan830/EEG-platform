"""Visual payloads for WPF ICA review (2D topomap + IC waveform) and cleaned-waveform overlay preview."""

from __future__ import annotations

import math
from typing import Any, Sequence

import mne
import numpy as np

from app.scientific.contracts.preprocessing import ArtifactRejectionSpec, ArtifactRemovalSpec, BadChannelSpec
from app.services.electrode_locations import resolve_electrode_labels
from app.services.preview_envelope import build_preview_envelope, preview_envelope_indices


def _scalp_label(label: str, formulas: dict[str, dict[str, object]] | None = None) -> str:
    formula = (formulas or {}).get(label.casefold())
    if formula is not None:
        return str(formula["positive"]).strip() if str(formula.get("negative_kind", "")).casefold() in {
            "originalhardwarereference", "mean",
        } else ""
    parts = label.rsplit("-", 1)
    return parts[0].strip() if len(parts) == 1 or parts[1].strip().casefold() in {"ref", "avg"} else ""


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
    formulas: dict[str, dict[str, object]] | None = None,
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
        base_label = _scalp_label(str(ch_name), formulas)
        xy = resolved_coords.get(base_label.casefold())
        num = float(val) if math.isfinite(float(val)) else None
        if xy is None or num is None:
            excluded.append(str(ch_name))
            points.append({
                "channel": str(ch_name), "electrode_label": base_label or None,
                "x": None, "y": None, "value": num, "status": "excluded",
                "reason": "electrode_location_unavailable" if base_label else "bipolar_or_derived_output_has_no_spatial_rule",
                "quality_status": "unknown", "quality_reasons": [],
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
    signal_provenance: dict[str, object] | None = None,
) -> dict[str, object]:
    """Enrich ICA inspection with scalp maps and dimensionless display envelopes."""
    names = tuple(fitted_ica.channel_names)
    mixing = np.asarray(fitted_ica.ica.get_components(), dtype=np.float64)
    formulas = {str(row["name"]).casefold(): row for row in (signal_provenance or {}).get("formulas", [])}
    base_labels = [label for name in names if (label := _scalp_label(name, formulas))]
    resolved_coords: dict[str, tuple[float, float]] = {}
    for item in resolve_electrode_labels(base_labels):
        coords = item.get("map_coordinates")
        if item.get("status") == "available" and isinstance(coords, list) and len(coords) == 2:
            x, y = float(coords[0]), float(coords[1])
            if math.isfinite(x) and math.isfinite(y):
                resolved_coords[str(item.get("requested_label", "")).casefold()] = (x, y)

    sfreq = float(fitted_ica.sampling_rate_hz)
    snippet = data_v[:max(16, int(round(preview_seconds * sfreq)))]
    finite_mask = np.all(np.isfinite(snippet), axis=1)
    sources = np.full((mixing.shape[1], len(snippet)), np.nan, dtype=np.float64)
    if finite_mask.any():
        # ICA source projection is pointwise; scatter finite projections back to the original timeline.
        raw_snip = mne.io.RawArray(np.ascontiguousarray(snippet[finite_mask].T), fitted_ica.info, verbose=False)
        sources[:, finite_mask] = fitted_ica.ica.get_sources(raw_snip).get_data()
    preview_duration_s = round(len(snippet) / sfreq, 6)

    enriched: list[dict[str, object]] = []
    for row in report.get("components", []):
        comp_idx = int(row["component_index"])
        topo = _build_ic_topomap_field(comp_idx, names, mixing[:, comp_idx], resolved_coords,
                                       grid_size=grid_size, formulas=formulas)
        sig = sources[comp_idx]
        finite = sig[np.isfinite(sig)]
        if finite.size:
            std = float(np.std(finite))
            sig = (sig - float(np.mean(finite))) / std if std > 1e-12 else sig * 0.0
        indices = preview_envelope_indices(sig, target_points=preview_points)
        wave = [round(float(sig[i]), 4) if np.isfinite(sig[i]) else None for i in indices]
        enriched.append({
            **row,
            "topomap": topo,
            "preview_waveform": wave,
            "preview_times_s": [round(i / sfreq, 6) for i in indices],
            "preview_unit": "z-score",
            "preview_compression": {
                "method": "finite_run_min_max_envelope_v1", "scientific_resampling": False,
                "target_points_per_channel": preview_points, "gap_boundaries_may_exceed_target": True,
            },
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
    """Return per-channel display envelopes (absolute seconds, uV, gaps as null)."""
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
    trace_times_s: dict[str, list[float]] = {}
    traces_uv: dict[str, list[float | None]] = {}
    for col_i, ch_name in enumerate(prepared.channel_names):
        times, values = build_preview_envelope(
            window_data[:, col_i], start_sample=s_idx, sfreq=sfreq, target_points=max_points,
        )
        trace_times_s[str(ch_name)] = times
        traces_uv[str(ch_name)] = values

    bad_set = {ch.casefold() for ch in norm_bad.channels}
    provenance = recordings.analysis_signal_provenance(target_recording) if norm_bad.policy == "interpolate" else {}
    formulas = {str(row["name"]).casefold(): row for row in (provenance or {}).get("formulas", [])}

    def depends_on_interpolated_input(channel: str) -> bool:
        formula = formulas.get(channel.casefold())
        inputs = [formula["positive"], *formula.get("negative_labels", [])] if formula else [channel]
        return any(str(label).casefold() in bad_set for label in inputs)

    interpolated = [
        str(ch) for ch in prepared.channel_names
        if norm_bad.policy == "interpolate" and depends_on_interpolated_input(str(ch))
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
        "trace_times_s": trace_times_s,
        "display_compression": {
            "method": "finite_run_min_max_envelope_v1", "scientific_resampling": False,
            "target_points_per_channel": max_points, "gap_boundaries_may_exceed_target": True,
        },
        "traces_uv": traces_uv,
        "ica_model_reused": bool(rem_ev.get("ica_model_reused", False)),
        "artifact_removal_status": str(rem_ev.get("status", "disabled")),
    }
