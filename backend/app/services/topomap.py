"""Backend-owned, deterministic spatial projection for existing EEG results."""

from __future__ import annotations

import math
import logging
import json
from time import perf_counter
from dataclasses import dataclass
from typing import Any, Literal, Protocol

import numpy as np

from app.models.topomap import TopomapField, TopomapGrid, TopomapPoint, TopomapResponse
from app.services.topomap_context import DynamicTopomapContextError, resolve_dynamic_signal_provenance

logger = logging.getLogger("brain_platform.topomap")
TopomapBandId = Literal["delta", "theta", "alpha", "beta", "gamma"]


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
    metadata_by_field: dict[str, dict[str, object]] | None = None
    quality_by_channel: dict[str, dict[str, object]] | None = None
    quality: str | None = None
    provenance: dict[str, object] | None = None
    power_type: str = "relative"
    unavailable: dict[str, object] | None = None


class TopomapAdapter(Protocol):
    algorithm_id: str

    def build_input(self, *, recording_id: str, payload: dict[str, Any],
                    window_start_s: float, window_end_s: float,
                    field_ids: list[str]) -> TopomapInput: ...


class RbpTopomapAdapter:
    algorithm_id = "rbp"
    metrics = ("delta", "theta", "alpha", "beta", "gamma")

    def build_input(self, *, recording_id: str, payload: dict[str, Any],
                    window_start_s: float, window_end_s: float,
                    field_ids: list[str]) -> TopomapInput:
        power_type, band_id = TopomapService._resolve_rbp_field_ids(field_ids)
        if power_type not in {"relative", "absolute"}:
            raise TopomapUnavailable("TOPOMAP_POWER_TYPE_INVALID", "不支持的地形图功率类型。")
        if band_id is not None and band_id not in self.metrics:
            raise TopomapUnavailable("TOPOMAP_BAND_ID_INVALID", "不支持的地形图频段。")
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
        missing: list[str] = []
        array_units = result.get("array_units") or {}
        for band in self.metrics:
            if band_id is not None and band != band_id:
                continue
            array_name = band if power_type == "relative" else f"{band}_absolute_power"
            values = arrays.get(array_name)
            unit = array_units.get(array_name)
            declared_unit = unit.get("unit") if isinstance(unit, dict) else unit
            expected_unit = "ratio" if power_type == "relative" else "uV^2"
            is_valid = isinstance(values, list) and len(values) == len(channels)
            if power_type == "absolute" and declared_unit != expected_unit:
                is_valid = False
            field_id = f"rbp.{power_type}.{band}"
            values_by_band[field_id] = values if is_valid else []
            units_by_band[field_id] = expected_unit
            if not is_valid and power_type == "absolute":
                missing.append(band)
        unavailable = None if not missing else {
            "code": "ABSOLUTE_POWER_NOT_AVAILABLE",
            "message": "此 RBP 结果未声明完整的绝对功率输出。",
            "details": {"bands": missing},
        }
        return TopomapInput(
            algorithm_id=self.algorithm_id, recording_id=recording_id,
            channel_order=[str(item) for item in channels], values_by_band=values_by_band,
            units_by_band=units_by_band,
            window_start_s=window_start_s, window_end_s=window_end_s,
            algorithm_version=str(identity.get("scientific_version") or "unknown"),
            implementation_version=str(identity.get("implementation_identity") or "unknown"),
            quality=result.get("quality"), provenance=result.get("provenance") or {},
            power_type=power_type, unavailable=unavailable,
        )


class PsdTopomapAdapter:
    algorithm_id = "psd"

    def build_input(self, *, recording_id: str, payload: dict[str, Any],
                    window_start_s: float, window_end_s: float,
                    field_ids: list[str]) -> TopomapInput:
        if len(field_ids) != 1 or not field_ids[0].startswith("psd.frequency-bin."):
            raise TopomapUnavailable("TOPOMAP_FIELD_ID_INVALID", "PSD 地形图必须选择一个精确频率 bin。")
        try:
            frequency_index = int(field_ids[0].rsplit(".", 1)[1])
        except ValueError as exc:
            raise TopomapUnavailable("TOPOMAP_FIELD_ID_INVALID", "PSD 频率 bin 索引无效。") from exc
        if frequency_index < 0 or field_ids[0] != f"psd.frequency-bin.{frequency_index}":
            raise TopomapUnavailable("TOPOMAP_FIELD_ID_INVALID", "PSD 频率 bin 索引必须是非负十进制整数。")
        result = payload.get("result") or payload
        frequencies = np.asarray((payload.get("axes") or result.get("axes") or {}).get("frequency_hz", []), dtype=float)
        arrays = result.get("arrays") or payload.get("arrays") or {}
        values = np.asarray(arrays.get("psd", []), dtype=float)
        channels = payload.get("channel_order") or result.get("channel_order")
        if channels is None and isinstance(payload.get("scientific_configuration_json"), str):
            try:
                configuration = json.loads(payload["scientific_configuration_json"])
            except (TypeError, json.JSONDecodeError) as exc:
                raise TopomapUnavailable("TOPOMAP_PSD_CONTRACT_INVALID", "PSD 科学配置快照无效。") from exc
            mapping = ((configuration.get("context") or {}).get("signal_mapping") or {})
            channels = mapping.get("channels") or configuration.get("channels")
        channels = channels or []
        if (frequency_index >= frequencies.size or values.ndim != 2 or
                values.shape != (len(channels), frequencies.size)):
            raise TopomapUnavailable("TOPOMAP_FIELD_ID_INVALID", "PSD 频率 bin 不存在或通道矩阵形状无效。")
        if values.shape[1] != frequencies.size or frequencies.ndim != 1 or not np.isfinite(frequencies).all() or np.any(np.diff(frequencies) <= 0):
            raise TopomapUnavailable("TOPOMAP_PSD_CONTRACT_INVALID", "PSD 矩阵与严格递增的频率轴不匹配。")
        psd_unit = (result.get("array_units") or {}).get("psd")
        psd_unit = psd_unit.get("unit") if isinstance(psd_unit, dict) else psd_unit
        if psd_unit != "V^2/Hz":
            raise TopomapUnavailable("TOPOMAP_PSD_CONTRACT_INVALID", "PSD 数组单位必须是 V^2/Hz。")
        evidence = result.get("evidence") or {}
        extensions = evidence.get("extensions") if isinstance(evidence, dict) else None
        channel_quality = extensions.get("channel_quality") if isinstance(extensions, dict) else None
        by_channel = channel_quality.get("by_channel") if isinstance(channel_quality, dict) else None
        if not isinstance(by_channel, dict) or set(by_channel) != set(channels):
            raise TopomapUnavailable("TOPOMAP_PSD_CONTRACT_INVALID", "PSD 逐通道质量必须与 channel_order 完全对齐。")
        axes = result.get("axes") or {}
        frequency_unit = (result.get("axis_units") or {}).get("frequency_hz")
        if frequency_unit != "Hz":
            raise TopomapUnavailable("TOPOMAP_PSD_CONTRACT_INVALID", "PSD 频率轴单位必须是 Hz。")
        if not channels or len(set(str(item).casefold() for item in channels)) != len(channels):
            raise TopomapUnavailable("TOPOMAP_PSD_CONTRACT_INVALID", "PSD 通道顺序为空或包含重复通道。")
        selected = values[:, frequency_index]
        field_id = field_ids[0]
        return TopomapInput(
            algorithm_id=self.algorithm_id, recording_id=recording_id,
            channel_order=[str(item) for item in channels],
            values_by_band={field_id: selected.tolist()}, units_by_band={field_id: "V^2/Hz"},
            window_start_s=window_start_s, window_end_s=window_end_s,
            algorithm_version=str((payload.get("identity") or {}).get("scientific_version") or "unknown"),
            implementation_version=str((payload.get("identity") or {}).get("implementation_identity") or "unknown"),
            quality=result.get("quality"), provenance=result.get("provenance") or {},
            quality_by_channel={str(channel): dict(by_channel[channel]) for channel in channels},
            power_type="psd", metadata_by_field={field_id: {
                "frequency_hz": float(frequencies[frequency_index]),
                "label": f"{frequencies[frequency_index]:g} Hz",
            }}, unavailable=None,
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
        self.adapters: dict[str, TopomapAdapter] = {"rbp": RbpTopomapAdapter(), "psd": PsdTopomapAdapter()}

    def build_rbp_window(
        self,
        run_id: str,
        *,
        cursor_sample: int | None = None,
        cursor_s: float | None = None,
        field_ids: list[str] | None = None,
        request_id: str | None = None,
    ) -> TopomapResponse:
        run = self.run_queue.get(run_id)
        algorithm_id = str((run.config or {}).get("algorithm_id", ""))
        adapter = self.adapters.get(algorithm_id)
        if adapter is None:
            raise TopomapUnavailable("TOPOMAP_ALGORITHM_UNSUPPORTED", "当前算法没有注册地形图适配器。")
        power_type = "psd" if algorithm_id == "psd" else self._resolve_rbp_field_ids(field_ids)[0]
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
                          str(preview.get("implementation_version") or run.implementation_version), start_s, end_s,
                          power_type)
        if preview.get("status") != "available" or window is None:
            return TopomapResponse(**base, fields=[], failure={
                "code": "TOPOMAP_WINDOW_UNAVAILABLE",
                "message": "当前 RBP 窗口没有可用结果。",
                "detail": {"status": preview.get("status"), "reason_codes": preview.get("reason_codes", [])},
            })
        try:
            spatial_input = adapter.build_input(recording_id=run.recording_id,
                payload={"result": {"arrays": preview.get("arrays"), "array_units": preview.get("array_metadata"),
                                     "evidence": preview.get("evidence"), "provenance": preview.get("provenance"),
                                     "axes": preview.get("axes"),
                                     "axis_units": {name: metadata.get("unit") for name, metadata in
                                                    (preview.get("axis_metadata") or {}).items()},
                                     "quality": preview.get("quality")},
                         "channel_order": list(((run.result_summary or {}).get("structured") or {}).get("channel_order") or []),
                         "identity": {"scientific_version": preview.get("scientific_version"),
                                      "implementation_identity": preview.get("implementation_version")}},
                window_start_s=max(0.0, start_s), window_end_s=max(0.0, end_s),
                field_ids=field_ids)
        except TopomapUnavailable as exc:
            return TopomapResponse(**base, fields=[], failure={
                "code": exc.code, "message": exc.message, "detail": exc.detail,
            })
        return self._build_response(run_id, spatial_input, run, request_id=request_id)

    def build_dynamic_window(self, window_id: str, *, project_id: str | None = None,
                             field_ids: list[str] | None = None,
                             request_id: str | None = None) -> TopomapResponse:
        started = perf_counter()
        if self.dynamic_window_service is None:
            raise TopomapUnavailable("TOPOMAP_DYNAMIC_UNAVAILABLE", "当前后端未启用按需窗口地形图。")
        payload_started = perf_counter()
        payload = self.dynamic_window_service.get(window_id, project_id=project_id)
        payload_ms = (perf_counter() - payload_started) * 1000
        identity = payload.get("identity") or {}
        algorithm_id = str(identity.get("algorithm_id", ""))
        adapter = self.adapters.get(algorithm_id)
        if adapter is None:
            raise TopomapUnavailable("TOPOMAP_ALGORITHM_UNSUPPORTED", "当前算法没有注册地形图适配器。")
        plan = payload.get("plan") or payload.get("range") or {}
        if payload.get("state") != "ready" or not payload.get("result"):
            power_type = "psd" if algorithm_id == "psd" else self._resolve_rbp_field_ids(field_ids)[0]
            return TopomapResponse(**self._base(
                window_id, str(identity.get("recording_id", "")),
                str(identity.get("scientific_version", "unknown")),
                str(identity.get("implementation_identity", "unknown")),
                float(plan.get("start_s", 0.0)), float(plan.get("end_s", 0.0)), power_type),
                fields=[], failure={"code": "TOPOMAP_WINDOW_UNAVAILABLE", "message": "当前按需窗口没有可用结果。"})
        input_started = perf_counter()
        power_type = "psd" if algorithm_id == "psd" else self._resolve_rbp_field_ids(field_ids)[0]
        spatial_input = adapter.build_input(
            recording_id=str(identity.get("recording_id", "")), payload=payload,
            window_start_s=float(plan.get("start_s", 0.0)), window_end_s=float(plan.get("end_s", 0.0)),
            field_ids=field_ids,
        )
        input_ms = (perf_counter() - input_started) * 1000
        context_started = perf_counter()
        try:
            signal_provenance = resolve_dynamic_signal_provenance(payload, spatial_input.channel_order)
        except DynamicTopomapContextError as exc:
            raise TopomapUnavailable("TOPOMAP_SOURCE_CONTEXT_INVALID", str(exc)) from exc
        context_ms = (perf_counter() - context_started) * 1000
        self._log_stage(
            request_id=request_id, window_id=window_id, stage="signal_provenance",
            total_ms=context_ms,
        )
        response = self._build_response(window_id, spatial_input,
            signal_provenance=signal_provenance, request_id=request_id)
        self._log_stage(
            request_id=request_id, window_id=window_id, stage="dynamic_window_total",
            payload_ms=payload_ms, input_ms=input_ms, context_ms=context_ms,
            build_ms=(perf_counter() - started) * 1000 - payload_ms - input_ms - context_ms,
            total_ms=(perf_counter() - started) * 1000,
        )
        return response

    @staticmethod
    def _resolve_rbp_field_ids(field_ids: list[str] | None) -> tuple[str, str | None]:
        if not field_ids:
            raise TopomapUnavailable("TOPOMAP_FIELD_IDS_REQUIRED", "地形图必须提供显式 field_ids。")
        if len(field_ids) != len(set(field_ids)):
            raise TopomapUnavailable("TOPOMAP_FIELD_ID_INVALID", "field_ids 不能重复。")
        if any(not isinstance(value, str) or not value.startswith("rbp.") for value in field_ids):
            raise TopomapUnavailable("TOPOMAP_FIELD_ID_INVALID", "地形图 field_id 不属于当前算法结果。")
        parsed = []
        for value in field_ids:
            parts = value.split(".")
            if len(parts) != 3 or parts[1] not in {"relative", "absolute"} or parts[2] not in RbpTopomapAdapter.metrics:
                raise TopomapUnavailable("TOPOMAP_FIELD_ID_INVALID", "RBP field_id 必须是 rbp.relative.<band> 或 rbp.absolute.<band>。")
            parsed.append((parts[1], parts[2]))
        if len(set(item[0] for item in parsed)) != 1:
            raise TopomapUnavailable("TOPOMAP_FIELD_ID_INVALID", "一次请求只能选择同一功率表示。")
        power_type = parsed[0][0]
        bands = [item[1] for item in parsed]
        return power_type, bands[0] if len(bands) == 1 else None

    def _base(self, run_id: str, recording_id: str, algorithm_version: str,
              implementation_version: str, start_s: float, end_s: float,
              power_type: str = "relative") -> dict[str, object]:
        return {"run_id": run_id, "recording_id": recording_id,
            "window_start_s": max(0.0, start_s), "window_end_s": max(0.0, end_s),
            "status": "unavailable", "algorithm_version": algorithm_version,
            "implementation_version": implementation_version, "coordinate_source": "montage_position_snapshot",
            "coordinate_frame": "mri", "coordinate_unit": "m"}

    def _build_response(self, run_id: str, spatial_input: TopomapInput, run: Any | None = None,
                        request_id: str | None = None,
                        signal_provenance: dict[str, object] | None = None) -> TopomapResponse:
        base = self._base(run_id, spatial_input.recording_id, spatial_input.algorithm_version,
                          spatial_input.implementation_version, spatial_input.window_start_s, spatial_input.window_end_s,
                          spatial_input.power_type)
        if spatial_input.unavailable:
            fields = [TopomapField(field_id=band, label=band, unit=spatial_input.units_by_band[band],
                **(spatial_input.metadata_by_field or {}).get(band, {}), points=[],
                status="unavailable", failure=spatial_input.unavailable)
                for band in spatial_input.values_by_band]
            return TopomapResponse(**{**base, "status": "unavailable", "fields": fields,
                "failure": spatial_input.unavailable, "provenance": {"power_type": spatial_input.power_type}})
        provenance_started = perf_counter()
        signal_provenance = signal_provenance if signal_provenance is not None else self._signal_provenance(run)
        self._log_stage(
            request_id=request_id, window_id=run_id, stage="signal_provenance",
            total_ms=(perf_counter() - provenance_started) * 1000,
        )
        formulas = {str(item.get("name")): item for item in signal_provenance.get("formulas", [])
                    if isinstance(item, dict) and item.get("name")}
        fields = []
        for band_id, values in spatial_input.values_by_band.items():
            started = perf_counter()
            band = self._build_band(band_id, spatial_input.units_by_band.get(band_id, "ratio"), values,
                spatial_input.channel_order, formulas, signal_provenance.get("map_positions") or {},
                spatial_input.provenance or {}, (spatial_input.metadata_by_field or {}).get(band_id, {}),
                spatial_input.quality_by_channel or {})
            grid_cells = band.grid.width * band.grid.height if band.grid is not None else 0
            logger.info(
                "topomap_interpolation_perf request_id=%s result_id=%s recording_id=%s field_id=%s "
                "channel_count=%d grid_cells=%d elapsed_ms=%.1f",
                request_id or "unknown", run_id, spatial_input.recording_id,
                band_id, len(spatial_input.channel_order), grid_cells, (perf_counter() - started) * 1000,
            )
            fields.append(band)
        status = "ready" if any(field.status == "ready" for field in fields) else "unavailable"
        failure = None if fields else {"code": "TOPOMAP_FIELDS_MISSING", "message": "当前窗口没有逐通道空间字段。"}
        return TopomapResponse(**{**base, "status": status, "fields": fields, "failure": failure,
            "provenance": {"field_ids": list(spatial_input.values_by_band), "power_type": spatial_input.power_type,
                "coordinate_source": "montage_position_snapshot",
                "montage_id": signal_provenance.get("montage_id"),
                "montage_revision": signal_provenance.get("montage_revision"),
                "montage_fingerprint": signal_provenance.get("montage_fingerprint"),
                "snapshot_sha256": signal_provenance.get("snapshot_sha256")}})

    @staticmethod
    def _log_stage(*, request_id: str | None, window_id: str, stage: str,
                   payload_ms: float | None = None, input_ms: float | None = None,
                   context_ms: float | None = None, build_ms: float | None = None,
                   total_ms: float | None = None) -> None:
        fields = [f"request_id={request_id or 'unknown'}", f"window_id={window_id}", f"stage={stage}"]
        for name, value in (("payload_ms", payload_ms), ("input_ms", input_ms),
                            ("context_ms", context_ms), ("build_ms", build_ms),
                            ("total_ms", total_ms)):
            if value is not None:
                fields.append(f"{name}={value:.1f}")
        logger.info("topomap_stage_perf %s", " ".join(fields))

    def _build_band(self, band_id: str, unit: str, values: list[object], channels: list[str],
                    formulas: dict[str, dict[str, object]], map_positions: dict[str, dict[str, object]],
                    provenance: dict[str, object], metadata: dict[str, object],
                    quality_by_channel: dict[str, dict[str, object]]) -> TopomapField:
        field_metadata = dict(metadata)
        label = str(field_metadata.pop("label", band_id))
        points, usable = self._build_points(channels, values, formulas, map_positions, quality_by_channel)
        included = [item.channel for item in points if item.status == "included"]
        excluded = [item.channel for item in points if item.status == "excluded"]
        if len(usable) < self.MINIMUM_POINTS:
            return TopomapField(field_id=band_id, label=label, unit=unit, **field_metadata, points=points, status="unavailable",
                included_channels=included, excluded_channels=excluded, failure={
                    "code": "TOPOMAP_INSUFFICIENT_POINTS",
                    "message": f"当前频段只有 {len(usable)} 个空间有效导联，至少需要 {self.MINIMUM_POINTS} 个。",
                }, provenance={**provenance, "minimum_points": self.MINIMUM_POINTS})
        grid = self._interpolate(usable)
        return TopomapField(field_id=band_id, label=label, unit=unit, **field_metadata, points=points, grid=grid, status="ready",
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
        quality_by_channel: dict[str, dict[str, object]] | None = None,
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
            quality = quality_by_channel.get(channel, {})
            quality_status = str(quality.get("status") or "unknown")
            quality_reasons = [str(item) for item in quality.get("reasons", [])]
            if not label:
                reason = "bipolar_or_derived_output_has_no_spatial_rule"
            elif not location or location.get("status") != "available":
                reason = "electrode_location_unavailable"
            elif location.get("enabled") is False:
                reason = "channel_not_enabled"
            elif numeric is None:
                reason = "channel_value_unavailable"
            elif quality_by_channel is not None and quality_status == "unavailable":
                reason = quality_reasons[0] if quality_reasons else "channel_quality_unavailable"
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
                    value=numeric, status="excluded", reason=reason,
                    quality_status=quality_status, quality_reasons=quality_reasons))
            else:
                points.append(TopomapPoint(channel=channel, electrode_label=label, x=x, y=y,
                    value=numeric, status="included", quality_status=quality_status,
                    quality_reasons=quality_reasons))
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
