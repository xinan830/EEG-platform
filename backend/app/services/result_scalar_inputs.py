"""Algorithm scalar inputs for existing result projections."""

from __future__ import annotations

import json
from dataclasses import dataclass
from typing import Any, Protocol

import numpy as np

from app.algorithms.rbp.amplitude import band_rms_uv
from app.scientific.contracts.analysis import FREQUENCY_BANDS_HZ
from app.scientific.primitives.spectral import band_power


class ResultProjectionUnavailable(ValueError):
    """Raised when an existing result cannot supply the requested projection."""

    def __init__(self, code: str, message: str, detail: dict[str, object] | None = None):
        super().__init__(message)
        self.code = code
        self.message = message
        self.detail = detail or {}


@dataclass(frozen=True)
class ResultScalarInput:
    """Ordered scalar fields projected from one existing algorithm result."""

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
    provenance_by_field: dict[str, dict[str, object]] | None = None
    failures_by_field: dict[str, dict[str, object]] | None = None
    quality_by_channel: dict[str, dict[str, object]] | None = None
    quality: str | None = None
    provenance: dict[str, object] | None = None
    power_type: str = "relative"
    unavailable: dict[str, object] | None = None


class ResultScalarAdapter(Protocol):
    algorithm_id: str

    def build_input(self, *, recording_id: str, payload: dict[str, Any],
                    window_start_s: float, window_end_s: float,
                    field_ids: list[str]) -> ResultScalarInput: ...


class RbpResultScalarAdapter:
    algorithm_id = "rbp"
    metrics = ("delta", "theta", "alpha", "beta", "gamma")

    def build_input(self, *, recording_id: str, payload: dict[str, Any],
                    window_start_s: float, window_end_s: float,
                    field_ids: list[str]) -> ResultScalarInput:
        power_type, band_id = resolve_rbp_field_ids(field_ids)
        if power_type not in {"relative", "absolute", "amplitude"}:
            raise ResultProjectionUnavailable("TOPOMAP_POWER_TYPE_INVALID", "不支持的地形图功率类型。")
        if band_id is not None and band_id not in self.metrics:
            raise ResultProjectionUnavailable("TOPOMAP_BAND_ID_INVALID", "不支持的地形图频段。")
        result = payload.get("result") or payload
        identity = payload.get("identity") or result.get("identity") or {}
        arrays = result.get("arrays") or {}
        evidence = result.get("evidence") or {}
        extensions = evidence.get("extensions") if isinstance(evidence, dict) else {}
        channel_meta = extensions.get("channel_order") if isinstance(extensions, dict) else {}
        channel_quality = extensions.get("channel_quality") if isinstance(extensions, dict) else {}
        quality_by_channel = channel_quality.get("by_channel") if isinstance(channel_quality, dict) else None
        channels = channel_meta.get("channels") if isinstance(channel_meta, dict) else None
        if not isinstance(channels, list):
            channels = payload.get("channel_order")
        if not isinstance(channels, list):
            raise ResultProjectionUnavailable("TOPOMAP_CHANNEL_VALUES_MISSING", "RBP 窗口缺少通道顺序证据。")
        values_by_band: dict[str, list[object]] = {}
        units_by_band: dict[str, str] = {}
        metadata_by_field: dict[str, dict[str, object]] = {}
        provenance_by_field: dict[str, dict[str, object]] = {}
        failures_by_field: dict[str, dict[str, object]] = {}
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
            if power_type != "relative" and declared_unit != expected_unit:
                is_valid = False
            field_id = f"rbp.{power_type}.{band}"
            if power_type == "amplitude":
                metadata_by_field[field_id] = {"label": f"{band.title()} RMS 振幅"}
                provenance_by_field[field_id] = {
                    "projection_identity": "rbp-band-rms-amplitude-v1",
                    "formula": "sqrt(absolute_band_power_uV2)",
                }
                if is_valid:
                    values = band_rms_uv(np.asarray(
                        [float(value) if isinstance(value, (int, float)) and not isinstance(value, bool)
                         else np.nan for value in values], dtype=np.float64)).tolist()
                else:
                    failures_by_field[field_id] = {
                        "code": "ABSOLUTE_POWER_NOT_AVAILABLE",
                        "message": "振幅需要同窗且单位为 uV^2 的逐通道绝对功率。",
                        "detail": {"band": band},
                    }
            values_by_band[field_id] = values if is_valid else []
            units_by_band[field_id] = "uV" if power_type == "amplitude" else expected_unit
            if not is_valid and power_type == "absolute":
                missing.append(band)
        unavailable = None if not missing else {
            "code": "ABSOLUTE_POWER_NOT_AVAILABLE",
            "message": "此 RBP 结果未声明完整的绝对功率输出。",
            "details": {"bands": missing},
        }
        return ResultScalarInput(
            algorithm_id=self.algorithm_id, recording_id=recording_id,
            channel_order=[str(item) for item in channels], values_by_band=values_by_band,
            units_by_band=units_by_band,
            window_start_s=window_start_s, window_end_s=window_end_s,
            algorithm_version=str(identity.get("scientific_version") or "unknown"),
            implementation_version=str(identity.get("implementation_identity") or "unknown"),
            quality=result.get("quality"), provenance=result.get("provenance") or {},
            quality_by_channel=quality_by_channel,
            power_type=power_type, unavailable=unavailable,
            metadata_by_field=metadata_by_field,
            provenance_by_field=provenance_by_field,
            failures_by_field=failures_by_field,
        )


class PsdResultScalarAdapter:
    algorithm_id = "psd"
    bands = {name: (low, high) for name, low, high in FREQUENCY_BANDS_HZ}

    def build_input(self, *, recording_id: str, payload: dict[str, Any],
                    window_start_s: float, window_end_s: float,
                    field_ids: list[str]) -> ResultScalarInput:
        if not field_ids or any(not isinstance(field_id, str) for field_id in field_ids):
            raise ResultProjectionUnavailable("TOPOMAP_FIELD_ID_INVALID", "PSD 地形图字段 ID 无效。")
        if len(field_ids) != len(set(field_ids)):
            raise ResultProjectionUnavailable("TOPOMAP_FIELD_ID_INVALID", "PSD 地形图字段 ID 不能重复。")

        parsed_fields: list[tuple[str, str]] = []
        for field_id in field_ids:
            if field_id.startswith("psd.band-mean-density."):
                band_id = field_id.removeprefix("psd.band-mean-density.")
                if band_id in self.bands:
                    parsed_fields.append(("band", band_id))
                    continue
            elif field_id.startswith("psd.frequency-bin."):
                parsed_fields.append(("frequency", field_id.removeprefix("psd.frequency-bin.")))
                continue
            raise ResultProjectionUnavailable("TOPOMAP_FIELD_ID_INVALID", "PSD 地形图字段 ID 无效。")
        if len({kind for kind, _ in parsed_fields}) != 1:
            raise ResultProjectionUnavailable("TOPOMAP_FIELD_ID_INVALID", "PSD 地形图一次请求只能选择同一类型的频谱字段。")
        if parsed_fields[0][0] == "frequency" and len(field_ids) != 1:
            raise ResultProjectionUnavailable("TOPOMAP_FIELD_ID_INVALID", "PSD 频率 bin 地形图一次只能选择一个字段。")

        result = payload.get("result") or payload
        frequencies = np.asarray((payload.get("axes") or result.get("axes") or {}).get("frequency_hz", []), dtype=float)
        arrays = result.get("arrays") or payload.get("arrays") or {}
        values = np.asarray(arrays.get("psd", []), dtype=float)
        channels = payload.get("channel_order") or result.get("channel_order")
        if channels is None and isinstance(payload.get("scientific_configuration_json"), str):
            try:
                configuration = json.loads(payload["scientific_configuration_json"])
            except (TypeError, json.JSONDecodeError) as exc:
                raise ResultProjectionUnavailable("TOPOMAP_PSD_CONTRACT_INVALID", "PSD 科学配置快照无效。") from exc
            mapping = ((configuration.get("context") or {}).get("signal_mapping") or {})
            channels = mapping.get("channels") or configuration.get("channels")
        channels = channels or []
        if values.ndim != 2 or values.shape != (len(channels), frequencies.size):
            raise ResultProjectionUnavailable("TOPOMAP_PSD_CONTRACT_INVALID", "PSD 矩阵形状与通道顺序或频率轴不匹配。")
        if frequencies.ndim != 1 or not np.isfinite(frequencies).all() or np.any(np.diff(frequencies) <= 0):
            raise ResultProjectionUnavailable("TOPOMAP_PSD_CONTRACT_INVALID", "PSD 矩阵与严格递增的频率轴不匹配。")
        finite_values = values[np.isfinite(values)]
        if finite_values.size and np.any(finite_values < 0):
            raise ResultProjectionUnavailable("TOPOMAP_PSD_CONTRACT_INVALID", "PSD 密度不得为负值。")
        psd_unit = (result.get("array_units") or {}).get("psd")
        psd_unit = psd_unit.get("unit") if isinstance(psd_unit, dict) else psd_unit
        if psd_unit != "V^2/Hz":
            raise ResultProjectionUnavailable("TOPOMAP_PSD_CONTRACT_INVALID", "PSD 数组单位必须是 V^2/Hz。")
        evidence = result.get("evidence") or {}
        extensions = evidence.get("extensions") if isinstance(evidence, dict) else None
        channel_quality = extensions.get("channel_quality") if isinstance(extensions, dict) else None
        by_channel = channel_quality.get("by_channel") if isinstance(channel_quality, dict) else None
        if not isinstance(by_channel, dict) or set(by_channel) != set(channels):
            raise ResultProjectionUnavailable("TOPOMAP_PSD_CONTRACT_INVALID", "PSD 逐通道质量必须与 channel_order 完全对齐。")
        frequency_unit = (result.get("axis_units") or {}).get("frequency_hz")
        if frequency_unit != "Hz":
            raise ResultProjectionUnavailable("TOPOMAP_PSD_CONTRACT_INVALID", "PSD 频率轴单位必须是 Hz。")
        if not channels or len(set(str(item).casefold() for item in channels)) != len(channels):
            raise ResultProjectionUnavailable("TOPOMAP_PSD_CONTRACT_INVALID", "PSD 通道顺序为空或包含重复通道。")
        provenance = dict(result.get("provenance") or {})
        values_by_band: dict[str, list[object]] = {}
        units_by_band: dict[str, str] = {}
        metadata_by_field: dict[str, dict[str, object]] = {}
        provenance_by_field: dict[str, dict[str, object]] = {}
        failures_by_field: dict[str, dict[str, object]] = {}
        if parsed_fields[0][0] == "frequency":
            power_type = "psd"
            for field_id, (_, raw_index) in zip(field_ids, parsed_fields, strict=True):
                try:
                    frequency_index = int(raw_index)
                except ValueError as exc:
                    raise ResultProjectionUnavailable("TOPOMAP_FIELD_ID_INVALID", "PSD 频率 bin 索引无效。") from exc
                if frequency_index < 0 or field_id != f"psd.frequency-bin.{frequency_index}" or frequency_index >= frequencies.size:
                    raise ResultProjectionUnavailable("TOPOMAP_FIELD_ID_INVALID", "PSD 频率 bin 不存在。")
                values_by_band[field_id] = values[:, frequency_index].tolist()
                units_by_band[field_id] = "V^2/Hz"
                metadata_by_field[field_id] = {"frequency_hz": float(frequencies[frequency_index]),
                                               "label": f"{frequencies[frequency_index]:g} Hz"}
        else:
            power_type = "psd_band_mean_density"
            provenance.update({"projection_identity": "psd-band-mean-density-v1",
                               "frequency_reduction": "trapezoidal_integral_divided_by_full_bandwidth"})
            for field_id, (_, band_id) in zip(field_ids, parsed_fields, strict=True):
                low_hz, high_hz = self.bands[band_id]
                units_by_band[field_id] = "V^2/Hz"
                metadata_by_field[field_id] = {
                    "frequency_range_hz": (low_hz, high_hz),
                    "label": f"{band_id.title()} 平均 PSD ({low_hz:g}–{high_hz:g} Hz)",
                }
                provenance_by_field[field_id] = {"bandwidth_hz": high_hz - low_hz}
                if frequencies.size < 2 or frequencies[0] > low_hz or frequencies[-1] < high_hz:
                    failures_by_field[field_id] = {
                        "code": "TOPOMAP_BAND_RANGE_UNAVAILABLE",
                        "message": "当前 PSD 频率轴没有完整覆盖所选频带。",
                        "detail": {"band_id": band_id, "frequency_range_hz": [low_hz, high_hz]},
                    }
                    values_by_band[field_id] = []
                    continue
                # Integrate then divide by full bandwidth: V²/Hz remains a density.
                values_by_band[field_id] = [float(band_power(frequencies, row, low_hz, high_hz) / (high_hz - low_hz))
                                            if np.isfinite(row).all() else float("nan") for row in values]
        return ResultScalarInput(
            algorithm_id=self.algorithm_id, recording_id=recording_id,
            channel_order=[str(item) for item in channels],
            values_by_band=values_by_band, units_by_band=units_by_band,
            window_start_s=window_start_s, window_end_s=window_end_s,
            algorithm_version=str((payload.get("identity") or {}).get("scientific_version") or "unknown"),
            implementation_version=str((payload.get("identity") or {}).get("implementation_identity") or "unknown"),
            quality=result.get("quality"), provenance=provenance,
            quality_by_channel={str(channel): dict(by_channel[channel]) for channel in channels},
            power_type=power_type, metadata_by_field=metadata_by_field,
            provenance_by_field=provenance_by_field, failures_by_field=failures_by_field,
            unavailable=None,
        )


def resolve_rbp_field_ids(field_ids: list[str] | None) -> tuple[str, str | None]:
    if not field_ids:
        raise ResultProjectionUnavailable("TOPOMAP_FIELD_IDS_REQUIRED", "地形图必须提供显式 field_ids。")
    if len(field_ids) != len(set(field_ids)):
        raise ResultProjectionUnavailable("TOPOMAP_FIELD_ID_INVALID", "field_ids 不能重复。")
    if any(not isinstance(value, str) or not value.startswith("rbp.") for value in field_ids):
        raise ResultProjectionUnavailable("TOPOMAP_FIELD_ID_INVALID", "地形图 field_id 不属于当前算法结果。")
    parsed = []
    for value in field_ids:
        parts = value.split(".")
        if len(parts) != 3 or parts[1] not in {"relative", "absolute", "amplitude"} or parts[2] not in RbpResultScalarAdapter.metrics:
            raise ResultProjectionUnavailable("TOPOMAP_FIELD_ID_INVALID", "RBP field_id 必须是 rbp.relative.<band>、rbp.absolute.<band> 或 rbp.amplitude.<band>。")
        parsed.append((parts[1], parts[2]))
    if len(set(item[0] for item in parsed)) != 1:
        raise ResultProjectionUnavailable("TOPOMAP_FIELD_ID_INVALID", "一次请求只能选择同一功率表示。")
    power_type = parsed[0][0]
    bands = [item[1] for item in parsed]
    return power_type, bands[0] if len(bands) == 1 else None



RESULT_SCALAR_ADAPTERS: dict[str, ResultScalarAdapter] = {
    "rbp": RbpResultScalarAdapter(), "psd": PsdResultScalarAdapter(),
}
