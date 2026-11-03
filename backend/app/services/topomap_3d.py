"""Build bounded 3D scalp fields from existing RBP window results."""

from functools import lru_cache
import json
import math
from types import SimpleNamespace
from typing import Any

import numpy as np
from scipy.spatial import Delaunay, QhullError

from app.models.topomap_3d import (
    Topomap3DBand,
    Topomap3DElectrode,
    Topomap3DExcludedChannel,
    Topomap3DLocationResponse,
    Topomap3DResolvedLocation,
    Topomap3DModelResponse,
    Topomap3DResponse,
)
from app.services.topomap import RbpTopomapAdapter, TopomapInput, TopomapService, TopomapUnavailable
from app.services.topomap_3d_assets import (
    MAPPING_ID,
    MAPPING_VERSION,
    MODEL_ID,
    MODEL_VERSION,
    Topomap3DAssets,
    load_topomap_3d_assets,
)


SPATIAL_METHOD = "gnm_upper_scalp_gnomonic_linear_v1"
HARDWARE_LANDMARKS = {"ref", "gnd"}
NON_SCALP_FIELD_CHANNELS = {"a1", "a2", "m1", "m2"} | HARDWARE_LANDMARKS


@lru_cache(maxsize=32)
def _surface_basis(canonical_sites: tuple[str, ...]) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    assets = load_topomap_3d_assets()
    sites = np.asarray([
        assets.channel_locations[name]["coordinate_gnm"] for name in canonical_sites
    ], dtype=float)
    local = (sites - assets.head_origin) @ np.column_stack((
        assets.right_axis, assets.forward_axis, assets.up_axis,
    ))
    if np.any(local[:, 2] <= 1e-8):
        raise TopomapUnavailable("TOPOMAP_3D_SCALP_SITE_INVALID", "导联位置不在头顶部插值区域。")
    sensor_xy = local[:, :2] / local[:, 2, None]
    try:
        triangulation = Delaunay(sensor_xy)
    except QhullError as exc:
        raise TopomapUnavailable("TOPOMAP_3D_CHANNEL_GEOMETRY_INVALID", "有效导联位置无法构成头顶部插值区域。") from exc

    projected = assets.projected_vertices
    simplex = triangulation.find_simplex(projected)
    valid = (simplex >= 0) & np.isfinite(projected).all(axis=1)
    vertex_indices = np.flatnonzero(valid)
    selected_simplex = simplex[valid]
    transform = triangulation.transform[selected_simplex]
    first_weights = np.einsum(
        "nij,nj->ni", transform[:, :2], projected[valid] - transform[:, 2],
    )
    weights = np.column_stack((first_weights, 1 - first_weights.sum(axis=1)))
    weights = np.clip(weights, 0.0, 1.0)
    weights /= weights.sum(axis=1, keepdims=True)
    source_indices = triangulation.simplices[selected_simplex]
    for array in (vertex_indices, weights, source_indices):
        array.setflags(write=False)
    return vertex_indices, weights, source_indices


class Topomap3DService:
    """Expose the GNM model once and interpolate only existing RBP values."""

    def __init__(self, topomap_service: TopomapService):
        self.topomap_service = topomap_service
        self._assets: Topomap3DAssets | None = None
        self._model: Topomap3DModelResponse | None = None

    @property
    def assets(self) -> Topomap3DAssets:
        if self._assets is None:
            self._assets = load_topomap_3d_assets()
        return self._assets

    def get_model(self) -> Topomap3DModelResponse:
        if self._model is None:
            assets = self.assets
            self._model = Topomap3DModelResponse(
                model_id=MODEL_ID, model_version=MODEL_VERSION,
                mapping_id=MAPPING_ID, mapping_version=MAPPING_VERSION,
                mapping_sha256=assets.mapping_sha256, mesh_asset_sha256=assets.mesh_asset_sha256,
                mesh_geometry_sha256=assets.mesh_geometry_sha256, source_montage="colin27_1020",
                source_mne_version=assets.mne_version, source_coordinate_frame="head",
                source_coordinate_unit="m", target_coordinate_frame="gnm_native",
                target_coordinate_unit="GNM model units", intended_use="standard_template_visualization",
                accuracy_status=str(assets.full_montage["accuracy_status"]),
                head_origin_gnm=tuple(assets.head_origin), right_axis_gnm=tuple(assets.right_axis),
                forward_axis_gnm=tuple(assets.forward_axis), up_axis_gnm=tuple(assets.up_axis),
                electrode_locations=[
                    {
                        "channel": channel,
                        "canonical_channel": str(location.get("canonical_name") or channel),
                        "coordinate_gnm": tuple(location["coordinate_gnm"]),
                        "mapping_method": str(location.get("method") or "unknown"),
                        "manual_adjusted": bool(location.get("manual_adjusted", False)),
                        "is_scalp_field_site": channel.casefold() not in NON_SCALP_FIELD_CHANNELS,
                    }
                    for channel, location in assets.channel_locations.items()
                ],
                vertices_gnm=assets.vertices.tolist(), triangles=assets.triangles.tolist(),
            )
        return self._model

    def resolve_locations(self, channels: list[str]) -> Topomap3DLocationResponse:
        assets = self.assets
        names = {name.casefold(): name for name in assets.channel_locations}
        locations = []
        for requested in channels:
            label = requested.strip()
            canonical_name = names.get(label.casefold()) if label else None
            site = assets.channel_locations.get(canonical_name) if canonical_name else None
            reason = None
            if not label:
                reason = "blank_channel_label"
            elif label.casefold() in HARDWARE_LANDMARKS:
                reason = "hardware_landmark_has_no_standard_electrode_site"
            elif site is None:
                reason = "electrode_location_unavailable"
            locations.append(Topomap3DResolvedLocation(
                channel=requested,
                canonical_channel=str(site.get("canonical_name") or canonical_name) if site else None,
                source_coordinate_mne=tuple(site["coordinate_mne"]) if site else None,
                target_coordinate_gnm=tuple(site["coordinate_gnm"]) if site and reason is None else None,
                mapping_method=str(site.get("method") or "unknown") if site else None,
                is_scalp_field_site=bool(site and
                    str(site.get("canonical_name") or canonical_name).casefold() not in NON_SCALP_FIELD_CHANNELS),
                status="available" if site and reason is None else "unavailable", reason=reason,
            ))
        return Topomap3DLocationResponse(
            mapping_id=MAPPING_ID, mapping_version=MAPPING_VERSION,
            mapping_sha256=assets.mapping_sha256, model_id=MODEL_ID, model_version=MODEL_VERSION,
            source_montage="colin27_1020", source_coordinate_frame="head", source_coordinate_unit="m",
            target_coordinate_frame="gnm_native", target_coordinate_unit="GNM model units",
            locations=locations,
        )

    def build_run_window(self, run_id: str, *, cursor_sample: int | None = None,
                         cursor_s: float | None = None, power_type: str = "relative",
                         band_id: str | None = None, request_id: str | None = None) -> Topomap3DResponse:
        self._validate_selection(power_type, band_id)
        topomaps = self.topomap_service
        run = topomaps.run_queue.get(run_id)
        if str((run.config or {}).get("algorithm_id", "")) != "rbp":
            raise TopomapUnavailable("TOPOMAP_ALGORITHM_UNSUPPORTED", "当前运行不是 RBP 结果。")
        preview = topomaps.result_service.window_preview(run_id, cursor_sample=cursor_sample, cursor_s=cursor_s)
        window = preview.get("window")
        recording = topomaps.run_queue.recordings.require_recording(run.recording_id)
        start_s = float((window or {}).get("start_sample", 0)) / float(recording.sfreq) if window else 0.0
        end_s = float((window or {}).get("end_sample", 0)) / float(recording.sfreq) if window else float(preview.get("cursor_s", 0.0))
        if preview.get("status") != "available" or window is None:
            raise TopomapUnavailable("TOPOMAP_WINDOW_UNAVAILABLE", "当前 RBP 窗口没有可用结果。",
                {"status": preview.get("status"), "reason_codes": preview.get("reason_codes", [])})
        adapter = topomaps.adapters["rbp"]
        spatial_input = adapter.build_input(
            recording_id=run.recording_id,
            payload={"result": {"arrays": preview.get("arrays"), "array_units": preview.get("array_metadata"),
                "evidence": preview.get("evidence"), "provenance": preview.get("provenance"),
                "quality": preview.get("quality")},
                "channel_order": list(((run.result_summary or {}).get("structured") or {}).get("channel_order") or []),
                "identity": {"scientific_version": preview.get("scientific_version"),
                    "implementation_identity": preview.get("implementation_version")}},
            window_start_s=max(0.0, start_s), window_end_s=max(0.0, end_s),
            power_type=power_type, band_id=band_id,
        )
        return self._build_response(run_id, spatial_input, run, request_id)

    def build_dynamic_window(self, window_id: str, *, project_id: str | None = None,
                             power_type: str = "relative", band_id: str | None = None,
                             request_id: str | None = None) -> Topomap3DResponse:
        self._validate_selection(power_type, band_id)
        topomaps = self.topomap_service
        if topomaps.dynamic_window_service is None:
            raise TopomapUnavailable("TOPOMAP_DYNAMIC_UNAVAILABLE", "当前后端未启用按需窗口地形图。")
        payload = topomaps.dynamic_window_service.get(window_id, project_id=project_id)
        identity = payload.get("identity") or {}
        if identity.get("algorithm_id") != "rbp":
            raise TopomapUnavailable("TOPOMAP_ALGORITHM_UNSUPPORTED", "当前动态窗口不是 RBP 结果。")
        plan = payload.get("plan") or payload.get("range") or {}
        if payload.get("state") != "ready" or not payload.get("result"):
            raise TopomapUnavailable("TOPOMAP_WINDOW_UNAVAILABLE", "当前按需窗口没有可用结果。")
        spatial_input = topomaps.adapters["rbp"].build_input(
            recording_id=str(identity.get("recording_id", "")), payload=payload,
            window_start_s=float(plan.get("start_s", 0.0)), window_end_s=float(plan.get("end_s", 0.0)),
            power_type=power_type, band_id=band_id,
        )
        try:
            config = json.loads(str(payload.get("scientific_configuration_json") or "{}"))
        except json.JSONDecodeError:
            config = {}
        recording = topomaps.run_queue.recordings.require_recording(spatial_input.recording_id)
        run_context = SimpleNamespace(recording_id=recording.id, config=config)
        return self._build_response(window_id, spatial_input, run_context, request_id)

    @staticmethod
    def _validate_selection(power_type: str, band_id: str | None) -> None:
        if power_type not in {"relative", "absolute"}:
            raise TopomapUnavailable("TOPOMAP_POWER_TYPE_INVALID", "不支持的地形图功率类型。")
        if band_id is not None and band_id not in RbpTopomapAdapter.metrics:
            raise TopomapUnavailable("TOPOMAP_BAND_ID_INVALID", "不支持的地形图频段。")

    def _build_response(self, run_id: str, spatial_input: TopomapInput, run: Any,
                        request_id: str | None) -> Topomap3DResponse:
        assets = self.assets
        signal_provenance = self.topomap_service._signal_provenance(run)
        formulas = {str(item.get("name")): item for item in signal_provenance.get("formulas", [])
                    if isinstance(item, dict) and item.get("name")}
        bands = [self._build_band(band_id, spatial_input.units_by_band[band_id], values,
                    spatial_input.channel_order, formulas)
                 for band_id, values in spatial_input.values_by_band.items()]
        status = "ready" if any(band.status == "ready" for band in bands) else "unavailable"
        failure = spatial_input.unavailable or (None if status == "ready" else {
            "code": "TOPOMAP_3D_INSUFFICIENT_CHANNELS", "message": "当前频段没有足够的有效头皮导联。",
        })
        logger_quality = str(spatial_input.quality) if spatial_input.quality is not None else None
        return Topomap3DResponse(
            run_id=run_id, recording_id=spatial_input.recording_id,
            window_start_s=max(0.0, spatial_input.window_start_s),
            window_end_s=max(0.0, spatial_input.window_end_s), status=status,
            algorithm_id="rbp", algorithm_version=spatial_input.algorithm_version,
            implementation_version=spatial_input.implementation_version,
            power_type=spatial_input.power_type, quality=logger_quality,
            model_id=MODEL_ID, model_version=MODEL_VERSION,
            mapping_id=MAPPING_ID, mapping_version=MAPPING_VERSION,
            mapping_sha256=assets.mapping_sha256, mesh_asset_sha256=assets.mesh_asset_sha256,
            mesh_geometry_sha256=assets.mesh_geometry_sha256, source_montage="colin27_1020",
            source_mne_version=assets.mne_version, source_coordinate_frame="head",
            source_coordinate_unit="m", target_coordinate_frame="gnm_native",
            target_coordinate_unit="GNM model units", spatial_method=SPATIAL_METHOD,
            spatial_parameters={
                "projection": "head_landmark_gnomonic",
                "interpolation": "scipy_delaunay_linear_barycentric",
                "minimum_unique_scalp_electrodes": 3,
                "support": "included_electrode_convex_hull",
                "outside_support": "unavailable",
            },
            intended_use="standard_template_visualization",
            accuracy_status=str(assets.full_montage["accuracy_status"]), bands=bands,
            failure=failure, provenance={
                "request_id": request_id,
                "montage_id": signal_provenance.get("montage_id"),
                "montage_revision": signal_provenance.get("montage_revision"),
                "montage_fingerprint": signal_provenance.get("montage_fingerprint"),
                "snapshot_sha256": signal_provenance.get("snapshot_sha256"),
                "display_only": True,
                "scalp_region": "upper-surface gnomonic projection bounded by included electrode hull",
            },
        )

    def _build_band(self, band_id: str, unit: str, values: list[object], channels: list[str],
                    formulas: dict[str, dict[str, object]]) -> Topomap3DBand:
        assets = self.assets
        excluded: list[Topomap3DExcludedChannel] = []
        candidates: list[dict[str, object]] = []
        for index, channel in enumerate(channels):
            formula = formulas.get(channel)
            electrode = None
            if formula is not None:
                if str(formula.get("negative_kind", "")).casefold() != "originalhardwarereference":
                    excluded.append(Topomap3DExcludedChannel(channel=channel,
                        reason="derived_channel_has_no_standard_scalp_site"))
                    continue
                electrode = str(formula.get("positive", channel))
            else:
                electrode = channel
            label = electrode.strip()
            if label.casefold() in NON_SCALP_FIELD_CHANNELS:
                excluded.append(Topomap3DExcludedChannel(channel=channel, electrode_label=label,
                    reason="reference_or_mastoid_has_no_scalp_field"))
                continue
            location = next((name for name in assets.channel_locations if name.casefold() == label.casefold()), None)
            numeric = values[index] if index < len(values) else None
            if location is None:
                excluded.append(Topomap3DExcludedChannel(channel=channel, electrode_label=label,
                    reason="electrode_location_unavailable"))
                continue
            if location.casefold() in NON_SCALP_FIELD_CHANNELS:
                excluded.append(Topomap3DExcludedChannel(channel=channel, electrode_label=location,
                    reason="reference_or_mastoid_has_no_scalp_field"))
                continue
            if isinstance(numeric, bool) or not isinstance(numeric, (int, float)) or not math.isfinite(float(numeric)):
                excluded.append(Topomap3DExcludedChannel(channel=channel, electrode_label=location,
                    reason="channel_value_unavailable"))
                continue
            site = assets.channel_locations[location]
            point = np.asarray(site["coordinate_gnm"], dtype=float)
            local = (point - assets.head_origin) @ np.column_stack((
                assets.right_axis, assets.forward_axis, assets.up_axis,
            ))
            if local[2] <= 1e-8:
                excluded.append(Topomap3DExcludedChannel(channel=channel, electrode_label=location,
                    reason="outside_upper_scalp_patch"))
                continue
            candidates.append({"index": index, "channel": channel, "label": location,
                "canonical": str(site.get("canonical_name") or location), "point": point,
                "projected": local[:2] / local[2], "value": float(numeric),
                "method": str(site.get("method") or "unknown")})

        coordinate_groups: dict[tuple[float, float, float], list[dict[str, object]]] = {}
        for candidate in candidates:
            key = tuple(round(float(value), 12) for value in candidate["point"])
            coordinate_groups.setdefault(key, []).append(candidate)
        included = []
        for group in coordinate_groups.values():
            chosen = min(group, key=lambda item: (item["label"] != item["canonical"], item["index"]))
            included.append(chosen)
            for duplicate in group:
                if duplicate is not chosen:
                    excluded.append(Topomap3DExcludedChannel(channel=str(duplicate["channel"]),
                        electrode_label=str(duplicate["label"]), reason="duplicate_spatial_coordinate"))
        included.sort(key=lambda item: int(item["index"]))
        if len(included) < 3:
            return Topomap3DBand(band_id=band_id, unit=unit, status="unavailable",
                included_channels=[str(item["channel"]) for item in included],
                excluded_channels=excluded, failure={"code": "TOPOMAP_3D_INSUFFICIENT_CHANNELS",
                    "message": "至少需要 3 个有效且空间不重复的头皮导联。"})
        site_names = tuple(str(item["canonical"]) for item in included)
        try:
            vertex_indices, weights, source_indices = _surface_basis(site_names)
        except TopomapUnavailable as exc:
            return Topomap3DBand(band_id=band_id, unit=unit, status="unavailable",
                included_channels=[str(item["channel"]) for item in included],
                excluded_channels=excluded, failure={"code": exc.code, "message": exc.message})
        samples = np.asarray([float(item["value"]) for item in included], dtype=float)
        projected_values = np.einsum("ni,ni->n", samples[source_indices], weights)
        vertex_values: list[float | None] = [None] * len(assets.vertices)
        for index, value in zip(vertex_indices, projected_values, strict=True):
            vertex_values[int(index)] = float(value)
        electrodes = [Topomap3DElectrode(
            channel=str(item["channel"]), electrode_label=str(item["label"]),
            coordinate_gnm=tuple(item["point"]), value=float(item["value"]),
            mapping_method=str(item["method"]),
            accuracy_warning="anatomical_review_required" if item["method"] == "scalp-arc-proportion" else None,
        ) for item in included]
        return Topomap3DBand(band_id=band_id, unit=unit, status="ready",
            vertex_values=vertex_values, electrodes=electrodes,
            included_channels=[str(item["channel"]) for item in included],
            excluded_channels=excluded, value_min=float(samples.min()), value_max=float(samples.max()))
