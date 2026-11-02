"""Backend-owned, deterministic spatial projection for existing EEG results."""

from __future__ import annotations

import math
import json
from dataclasses import dataclass
from types import SimpleNamespace
from typing import Any, Protocol

import numpy as np

from app.models.topomap import TopomapBand, TopomapGrid, TopomapPoint, TopomapResponse


class TopomapUnavailable(ValueError):
    """Raised when a requested result cannot produce a spatial map."""

    def __init__(self, code: str, message: str, detail: dict[str, object] | None = None):
        super().__init__(message)
        self.code = code
        self.message = message
        self.detail = detail or {}


@dataclass(frozen=True)
class TopomapInput:
    """Typed spatial input supplied by one algorithm adapter."""

    algorithm_id: str
    recording_id: str
    channel_order: list[str]
    values_by_band: dict[str, list[object]]
    units_by_band: dict[str, str]
    window_start_s: float
    window_end_s: float
    algorithm_version: str
    implementation_version: str
    quality: str | None = None
    provenance: dict[str, object] | None = None


class TopomapAdapter(Protocol):
    algorithm_id: str

    def build_input(self, *, recording_id: str, payload: dict[str, Any],
                    window_start_s: float, window_end_s: float) -> TopomapInput: ...


class RbpTopomapAdapter:
    algorithm_id = "rbp"
    metrics = ("delta", "theta", "alpha", "beta", "gamma")

    def build_input(self, *, recording_id: str, payload: dict[str, Any],
                    window_start_s: float, window_end_s: float) -> TopomapInput:
        result = payload.get("result") or payload
        identity = payload.get("identity") or result.get("identity") or {}
        arrays = result.get("arrays") or {}
        evidence = result.get("evidence") or {}
        extensions = evidence.get("extensions") if isinstance(evidence, dict) else {}
        channel_meta = extensions.get("channel_order") if isinstance(extensions, dict) else {}
        channels = channel_meta.get("channels") if isinstance(channel_meta, dict) else None
        if not isinstance(channels, list):
            channels = payload.get("channel_order")
        if not isinstance(channels, list):
            raise TopomapUnavailable("TOPOMAP_CHANNEL_VALUES_MISSING", "RBP 窗口缺少通道顺序证据。")
        values_by_band: dict[str, list[object]] = {}
        units_by_band: dict[str, str] = {}
        array_units = result.get("array_units") or {}
        for band in self.metrics:
            values = arrays.get(band)
            values_by_band[band] = values if isinstance(values, list) and len(values) == len(channels) else []
            if values_by_band[band]:
                unit = array_units.get(band)
                units_by_band[band] = str(unit.get("unit") if isinstance(unit, dict) else unit or "ratio")
            else:
                units_by_band[band] = "ratio"
        return TopomapInput(
            algorithm_id=self.algorithm_id, recording_id=recording_id,
            channel_order=[str(item) for item in channels], values_by_band=values_by_band,
            units_by_band=units_by_band,
            window_start_s=window_start_s, window_end_s=window_end_s,
            algorithm_version=str(identity.get("scientific_version") or "unknown"),
            implementation_version=str(identity.get("implementation_identity") or "unknown"),
            quality=result.get("quality"), provenance=result.get("provenance") or {},
        )


class TopomapService:
    """Build a topomap from a saved dynamic RBP window without recomputing EEG."""

    GRID_SIZE = 65
    ELECTRODE_BOUNDARY = 0.92
    HEAD_RADIUS = 1.0
    MINIMUM_POINTS = 3

    def __init__(self, result_service: Any, run_queue: Any, dynamic_window_service: Any | None = None):
        self.result_service = result_service
        self.run_queue = run_queue
        self.dynamic_window_service = dynamic_window_service
        self.adapters: dict[str, TopomapAdapter] = {"rbp": RbpTopomapAdapter()}

    def build_rbp_window(
        self,
        run_id: str,
        *,
        cursor_sample: int | None = None,
        cursor_s: float | None = None,
    ) -> TopomapResponse:
        run = self.run_queue.get(run_id)
        if str((run.config or {}).get("algorithm_id", "")) != "rbp":
            raise TopomapUnavailable("TOPOMAP_ALGORITHM_UNSUPPORTED", "当前运行不是 RBP 结果。")
        preview = self.result_service.window_preview(
            run_id, cursor_sample=cursor_sample, cursor_s=cursor_s,
        )
        window = preview.get("window")
        recording = self.run_queue.recordings.require_recording(run.recording_id)
        sfreq = float(recording.sfreq)
        start_s = float((window or {}).get("start_sample", 0)) / sfreq if window else 0.0
        end_s = float((window or {}).get("end_sample", 0)) / sfreq if window else float(preview.get("cursor_s", 0.0))
        base = self._base(run_id, str(preview.get("recording_id") or run.recording_id),
                          str(preview.get("scientific_version") or run.scientific_version),
                          str(preview.get("implementation_version") or run.implementation_version), start_s, end_s)
        if preview.get("status") != "available" or window is None:
            return TopomapResponse(**base, bands=[], failure={
                "code": "TOPOMAP_WINDOW_UNAVAILABLE",
                "message": "当前 RBP 窗口没有可用结果。",
                "detail": {"status": preview.get("status"), "reason_codes": preview.get("reason_codes", [])},
            })
        adapter = self.adapters["rbp"]
        try:
            spatial_input = adapter.build_input(recording_id=run.recording_id,
                payload={"result": {"arrays": preview.get("arrays"), "array_units": preview.get("array_metadata"),
                                     "evidence": preview.get("evidence"), "provenance": preview.get("provenance"),
                                     "quality": preview.get("quality")},
                         "channel_order": list(((run.result_summary or {}).get("structured") or {}).get("channel_order") or []),
                         "identity": {"scientific_version": preview.get("scientific_version"),
                                      "implementation_identity": preview.get("implementation_version")}},
                window_start_s=max(0.0, start_s), window_end_s=max(0.0, end_s))
        except TopomapUnavailable as exc:
            return TopomapResponse(**base, bands=[], failure={
                "code": exc.code, "message": exc.message, "detail": exc.detail,
            })
        return self._build_response(run_id, spatial_input, run)

    def build_dynamic_window(self, window_id: str, *, project_id: str | None = None) -> TopomapResponse:
        if self.dynamic_window_service is None:
            raise TopomapUnavailable("TOPOMAP_DYNAMIC_UNAVAILABLE", "当前后端未启用按需窗口地形图。")
        payload = self.dynamic_window_service.get(window_id, project_id=project_id)
        identity = payload.get("identity") or {}
        algorithm_id = str(identity.get("algorithm_id", ""))
        adapter = self.adapters.get(algorithm_id)
        if adapter is None:
            raise TopomapUnavailable("TOPOMAP_ALGORITHM_UNSUPPORTED", "当前算法没有注册地形图适配器。")
        plan = payload.get("plan") or payload.get("range") or {}
        if payload.get("state") != "ready" or not payload.get("result"):
            return TopomapResponse(**self._base(
                window_id, str(identity.get("recording_id", "")),
                str(identity.get("scientific_version", "unknown")),
                str(identity.get("implementation_identity", "unknown")),
                float(plan.get("start_s", 0.0)), float(plan.get("end_s", 0.0))),
                bands=[], failure={"code": "TOPOMAP_WINDOW_UNAVAILABLE", "message": "当前按需窗口没有可用结果。"})
        spatial_input = adapter.build_input(
            recording_id=str(identity.get("recording_id", "")), payload=payload,
            window_start_s=float(plan.get("start_s", 0.0)), window_end_s=float(plan.get("end_s", 0.0)),
        )
        recording = self.run_queue.recordings.require_recording(spatial_input.recording_id)
        try:
            config = json.loads(str(payload.get("scientific_configuration_json") or "{}"))
        except json.JSONDecodeError:
            config = {}
        return self._build_response(window_id, spatial_input,
            SimpleNamespace(recording_id=recording.id, config=config))

    def _base(self, run_id: str, recording_id: str, algorithm_version: str,
              implementation_version: str, start_s: float, end_s: float) -> dict[str, object]:
        return {"run_id": run_id, "recording_id": recording_id,
            "window_start_s": max(0.0, start_s), "window_end_s": max(0.0, end_s),
            "status": "unavailable", "algorithm_version": algorithm_version,
            "implementation_version": implementation_version, "coordinate_source": "montage_position_snapshot",
            "coordinate_frame": "mri", "coordinate_unit": "m"}

    def _build_response(self, run_id: str, spatial_input: TopomapInput, run: Any) -> TopomapResponse:
        base = self._base(run_id, spatial_input.recording_id, spatial_input.algorithm_version,
                          spatial_input.implementation_version, spatial_input.window_start_s, spatial_input.window_end_s)
        signal_provenance = self._signal_provenance(run)
        formulas = {str(item.get("name")): item for item in signal_provenance.get("formulas", [])
                    if isinstance(item, dict) and item.get("name")}
        bands = [self._build_band(band_id, spatial_input.units_by_band.get(band_id, "ratio"), values,
                 spatial_input.channel_order, formulas, signal_provenance.get("map_positions") or {},
                 spatial_input.provenance or {})
                 for band_id, values in spatial_input.values_by_band.items()]
        status = "ready" if any(band.status == "ready" for band in bands) else "unavailable"
        failure = None if bands else {"code": "TOPOMAP_BANDS_MISSING", "message": "当前窗口没有逐通道频段结果。"}
        return TopomapResponse(**{**base, "status": status, "bands": bands, "failure": failure,
            "provenance": {"bands": list(spatial_input.values_by_band),
                "coordinate_source": "montage_position_snapshot",
                "montage_id": signal_provenance.get("montage_id"),
                "montage_revision": signal_provenance.get("montage_revision"),
                "montage_fingerprint": signal_provenance.get("montage_fingerprint"),
                "snapshot_sha256": signal_provenance.get("snapshot_sha256")}})

    def _build_band(self, band_id: str, unit: str, values: list[object], channels: list[str],
                    formulas: dict[str, dict[str, object]], map_positions: dict[str, dict[str, object]],
                    provenance: dict[str, object]) -> TopomapBand:
        points, usable = self._build_points(channels, values, formulas, map_positions)
        included = [item.channel for item in points if item.status == "included"]
        excluded = [item.channel for item in points if item.status == "excluded"]
        if len(usable) < self.MINIMUM_POINTS:
            return TopomapBand(band_id=band_id, unit=unit, points=points, status="unavailable",
                included_channels=included, excluded_channels=excluded, failure={
                    "code": "TOPOMAP_INSUFFICIENT_POINTS",
                    "message": f"当前频段只有 {len(usable)} 个空间有效导联，至少需要 {self.MINIMUM_POINTS} 个。",
                }, provenance={**provenance, "minimum_points": self.MINIMUM_POINTS})
        grid = self._interpolate(usable)
        return TopomapBand(band_id=band_id, unit=unit, points=points, grid=grid, status="ready",
            included_channels=included, excluded_channels=excluded,
            provenance={**provenance, "metric_unit": unit, "interpolation": grid.method,
                "head_radius": self.HEAD_RADIUS, "minimum_points": self.MINIMUM_POINTS})

    def _signal_provenance(self, run: Any) -> dict[str, object]:
        recording = self.run_queue.recordings.require_recording(run.recording_id)
        montage_id = (run.config or {}).get("analysis_montage_id")
        if montage_id:
            recording = self.run_queue.recordings.select_analysis_montage(recording, str(montage_id))
        provenance = self.run_queue.recordings.analysis_signal_provenance(recording)
        return provenance

    def _build_points(
        self,
        channels: list[str],
        values: list[object],
        formulas: dict[str, dict[str, object]],
        map_positions: dict[str, dict[str, object]],
    ) -> tuple[list[TopomapPoint], list[tuple[str, float, float, float]]]:
        labels = []
        for channel in channels:
            formula = formulas.get(channel)
            if formula is not None and str(formula.get("negative_kind", "")) != "originalhardwarereference":
                labels.append("")
            else:
                labels.append(str(formula.get("positive", channel)) if formula else channel)
        points: list[TopomapPoint] = []
        usable: list[tuple[str, float, float, float]] = []
        coordinates: set[tuple[float, float]] = set()
        for index, (channel, label) in enumerate(zip(channels, labels, strict=True)):
            value = values[index] if index < len(values) else None
            location = map_positions.get(label) if label else None
            reason = None
            x = y = None
            numeric = float(value) if isinstance(value, (int, float)) and math.isfinite(float(value)) else None
            if not label:
                reason = "bipolar_or_derived_output_has_no_spatial_rule"
            elif not location or location.get("status") != "available":
                reason = "electrode_location_unavailable"
            elif location.get("enabled") is False:
                reason = "channel_not_enabled"
            elif numeric is None:
                reason = "channel_value_unavailable"
            else:
                x, y = float(location["x"]), float(location["y"])
                key = (round(x, 12), round(y, 12))
                if key in coordinates:
                    reason = "duplicate_spatial_coordinate"
                elif math.hypot(x, y) > self.ELECTRODE_BOUNDARY:
                    reason = "coordinate_outside_head_boundary"
                else:
                    coordinates.add(key)
            if reason:
                points.append(TopomapPoint(channel=channel, electrode_label=label or None, x=x, y=y,
                    value=numeric, status="excluded", reason=reason))
            else:
                points.append(TopomapPoint(channel=channel, electrode_label=label, x=x, y=y,
                    value=numeric, status="included"))
                usable.append((channel, x, y, numeric))
        return points, usable

    def _interpolate(self, points: list[tuple[str, float, float, float]]) -> TopomapGrid:
        axis = np.linspace(-self.HEAD_RADIUS, self.HEAD_RADIUS, self.GRID_SIZE)
        locations = np.asarray([[x, y] for _, x, y, _ in points], dtype=float)
        samples = np.asarray([value for _, _, _, value in points], dtype=float)
        grid_x, grid_y = np.meshgrid(axis, axis)
        grid_points = np.column_stack((grid_x.ravel(), grid_y.ravel()))
        distances = np.linalg.norm(grid_points[:, None, :] - locations[None, :, :], axis=2)
        exact = distances < 1e-12
        weights = 1 / np.maximum(distances, 1e-12) ** 2
        interpolated = (weights @ samples) / weights.sum(axis=1)
        exact_index = np.argmax(exact, axis=1)
        values_flat = np.where(exact.any(axis=1), samples[exact_index], interpolated)
        inside = np.hypot(grid_points[:, 0], grid_points[:, 1]) <= self.HEAD_RADIUS
        values_flat = values_flat.tolist()
        values = [
            [float(values_flat[index]) if inside[index] else None
             for index in range(row * self.GRID_SIZE, (row + 1) * self.GRID_SIZE)]
            for row in range(self.GRID_SIZE)
        ]
        return TopomapGrid(method="inverse_distance_weighted_v1", width=self.GRID_SIZE, height=self.GRID_SIZE,
            x_min=-self.HEAD_RADIUS, x_max=self.HEAD_RADIUS, y_min=-self.HEAD_RADIUS, y_max=self.HEAD_RADIUS,
            head_radius=self.HEAD_RADIUS, values=values)
