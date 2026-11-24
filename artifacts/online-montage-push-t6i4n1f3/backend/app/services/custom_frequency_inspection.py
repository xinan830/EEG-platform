"""Project custom-frequency values from paired saved PSD and RBP windows."""

from __future__ import annotations

import json
from typing import Any

import numpy as np

from app.models.custom_frequency_inspection import (
    CustomFrequencyBinChannel,
    CustomFrequencyBinInterval,
    CustomFrequencyBinsResponse,
    CustomFrequencyProvenance,
    CustomFrequencyRangeChannel,
    CustomFrequencyRangeRequest,
    CustomFrequencyRangeResponse,
    CustomFrequencyWindowPair,
)
from app.services.result_scalar_inputs import ResultProjectionUnavailable
from app.algorithms.rbp.custom_frequency import project_bins, project_interval, saved_denominators_uv2
from app.scientific.primitives.spectral import band_power


class CustomFrequencyInspectionService:
    def __init__(self, dynamic_window_service: Any):
        self.dynamic_window_service = dynamic_window_service

    def read_bins(self, pair: CustomFrequencyWindowPair) -> CustomFrequencyBinsResponse:
        psd, rbp, channels, frequencies, psd_values, rbp_values, quality, base = self._sources(pair)
        intervals = _bin_intervals(frequencies)
        denominators = saved_denominators_uv2(rbp["result"].get("arrays", {}), len(channels))
        denominators = [value if quality[channel][1][0] in {"clean", "warning"} else None
                        for channel, value in zip(channels, denominators)]
        powers, relative, amplitudes = project_bins(frequencies, rbp_values, denominators,
            [(interval.low_hz, interval.high_hz) for interval in intervals])
        rows = []
        for index, channel in enumerate(channels):
            psd_quality, rbp_quality = quality[channel]
            density = _valid_row(psd_values[index], psd_quality)
            rows.append(CustomFrequencyBinChannel(
                channel=channel,
                psd_quality=psd_quality[0], psd_quality_reasons=psd_quality[1],
                rbp_quality=rbp_quality[0], rbp_quality_reasons=rbp_quality[1],
                rbp_denominator_uv2=denominators[index],
                psd_density_uv2_per_hz=_scaled_density(density, len(frequencies)),
                rbp_absolute_power_uv2=_scaled_density(powers[index], len(frequencies), scale=1),
                rbp_relative_power_percent=_scaled_density(relative[index], len(frequencies), scale=1),
                amplitude_rms_uv=_scaled_density(amplitudes[index], len(frequencies), scale=1),
            ))
        return CustomFrequencyBinsResponse(
            **base, frequency_hz=frequencies.tolist(), bin_intervals=intervals,
            channels=rows, psd_provenance=_provenance(psd), rbp_provenance=_provenance(rbp),
        )

    def read_range(self, request: CustomFrequencyRangeRequest) -> CustomFrequencyRangeResponse:
        psd, rbp, channels, frequencies, psd_values, rbp_values, quality, base = self._sources(request)
        if request.low_hz < frequencies[0] or request.high_hz > frequencies[-1]:
            raise ResultProjectionUnavailable(
                "CUSTOM_FREQUENCY_RANGE_UNSUPPORTED", "所选范围超出当前 PSD/RBP 频率网格。",
            )
        arrays = rbp["result"].get("arrays", {})
        denominators = saved_denominators_uv2(arrays, len(channels))
        rows = []
        for index, channel in enumerate(channels):
            psd_quality, rbp_quality = quality[channel]
            density = _valid_row(psd_values[index], psd_quality)
            rbp_spectrum = _valid_row(rbp_values[index], rbp_quality)
            psd_power = _integral_v2(frequencies, density, request.low_hz, request.high_hz)
            denominator = denominators[index] if rbp_quality[0] in {"clean", "warning"} else None
            rbp_power, percent, amplitude = project_interval(
                frequencies, rbp_spectrum, denominator, request.low_hz, request.high_hz)
            rows.append(CustomFrequencyRangeChannel(
                channel=channel,
                psd_quality=psd_quality[0], psd_quality_reasons=psd_quality[1],
                rbp_quality=rbp_quality[0], rbp_quality_reasons=rbp_quality[1],
                rbp_denominator_uv2=denominator,
                psd_density_uv2_per_hz=(psd_power * 1e12 / (request.high_hz - request.low_hz)
                    if psd_power is not None else None),
                rbp_absolute_power_uv2=rbp_power,
                rbp_relative_power_percent=percent, amplitude_rms_uv=amplitude,
            ))
        return CustomFrequencyRangeResponse(
            **base, low_hz=request.low_hz, high_hz=request.high_hz,
            channels=rows,
            psd_provenance=_provenance(psd), rbp_provenance=_provenance(rbp),
        )

    def _sources(self, pair: CustomFrequencyWindowPair):
        psd = self.dynamic_window_service.get(pair.psd_window_id, project_id=pair.project_id,
                                              include_projection_source=True)
        rbp = self.dynamic_window_service.get(pair.rbp_window_id, project_id=pair.project_id,
                                              include_projection_source=True)
        psd_identity = psd.get("identity") or {}
        rbp_identity = rbp.get("identity") or {}
        required_identity = ("algorithm_id", "recording_id", "source_sha256", "start_sample", "end_sample",
                             "scientific_version", "implementation_identity", "configuration_sha256", "window_policy_version")
        if any(key not in identity or identity[key] is None for identity in (psd_identity, rbp_identity)
               for key in required_identity):
            raise ResultProjectionUnavailable("CUSTOM_FREQUENCY_SOURCE_MISMATCH", "分析窗口缺少完整来源身份。")
        if psd_identity.get("algorithm_id") != "psd" or rbp_identity.get("algorithm_id") != "rbp":
            raise ResultProjectionUnavailable("CUSTOM_FREQUENCY_SOURCE_MISMATCH", "必须提供当前 PSD 与 RBP 窗口。")
        if psd.get("state") != "ready" or rbp.get("state") != "ready":
            raise ResultProjectionUnavailable("CUSTOM_FREQUENCY_WINDOW_UNAVAILABLE", "当前 PSD 或 RBP 窗口不可用。")
        if (psd_identity.get("recording_id") != rbp_identity.get("recording_id") or
            psd_identity.get("source_sha256") != rbp_identity.get("source_sha256") or
            psd_identity.get("start_sample") != rbp_identity.get("start_sample") or
            psd_identity.get("end_sample") != rbp_identity.get("end_sample")):
            raise ResultProjectionUnavailable("CUSTOM_FREQUENCY_SOURCE_MISMATCH", "PSD/RBP 记录来源或实际样本窗口不匹配。")

        psd_context = _scientific_context(psd)
        rbp_context = _scientific_context(rbp)
        for key in ("sampling_rate_hz", "signal_mapping", "reference", "filters"):
            if key not in psd_context or key not in rbp_context or psd_context[key] != rbp_context[key]:
                raise ResultProjectionUnavailable("CUSTOM_FREQUENCY_SOURCE_MISMATCH", f"PSD/RBP 科学配置 {key} 不匹配。")
        for key in ("welch_segment_s", "welch_overlap", "dynamic_window_s", "refresh_step_s", "alignment"):
            if (key not in psd_context.get("window", {}) or key not in rbp_context.get("window", {}) or
                psd_context["window"][key] != rbp_context["window"][key]):
                raise ResultProjectionUnavailable("CUSTOM_FREQUENCY_SOURCE_MISMATCH", f"PSD/RBP 分析窗口设置 {key} 不匹配。")

        channels = psd_context.get("signal_mapping", {}).get("channels")
        rbp_channels = (((rbp.get("result") or {}).get("evidence") or {}).get("extensions", {})
                        .get("channel_order", {}).get("channels"))
        if (not isinstance(channels, list) or not channels or channels != rbp_channels or
            any(not isinstance(channel, str) or not channel.strip() for channel in channels) or
            len(set(channel.casefold() for channel in channels)) != len(channels)):
            raise ResultProjectionUnavailable("CUSTOM_FREQUENCY_SOURCE_MISMATCH", "PSD/RBP 导联顺序不匹配或缺失。")
        psd_result, rbp_result = psd.get("result") or {}, rbp.get("result") or {}
        if (psd_result.get("axis_units", {}).get("frequency_hz") != "Hz" or
            rbp_result.get("axis_units", {}).get("frequency_hz") != "Hz" or
            psd_result.get("array_units", {}).get("psd") != "V^2/Hz" or
            rbp_result.get("array_units", {}).get("spectral_psd") != "V^2/Hz" or
            any(rbp_result.get("array_units", {}).get(f"{name}_absolute_power") != "uV^2"
                for name in ("delta", "theta", "alpha", "beta", "gamma"))):
            raise ResultProjectionUnavailable("CUSTOM_FREQUENCY_SOURCE_MISMATCH", "PSD/RBP 频谱或功率单位不匹配或缺失。")
        try:
            psd_frequencies = np.asarray(psd_result["axes"]["frequency_hz"], dtype=float)
            rbp_frequencies = np.asarray(rbp_result["axes"]["frequency_hz"], dtype=float)
            psd_values = np.asarray(psd_result["arrays"]["psd"], dtype=float)
            rbp_values = np.asarray(rbp_result["arrays"]["spectral_psd"], dtype=float)
        except (KeyError, TypeError, ValueError) as exc:
            raise ResultProjectionUnavailable(
                "CUSTOM_FREQUENCY_SOURCE_UNAVAILABLE", "当前窗口没有所需的 PSD 或 RBP 频谱产物。",
            ) from exc
        if (any(axis.ndim != 1 or len(axis) < 2 or not np.isfinite(axis).all() or
                np.any(np.diff(axis) <= 0) for axis in (psd_frequencies, rbp_frequencies)) or
            psd_values.shape != (len(channels), len(psd_frequencies)) or
            rbp_values.shape != (len(channels), len(rbp_frequencies))):
            raise ResultProjectionUnavailable("CUSTOM_FREQUENCY_SOURCE_MISMATCH", "PSD/RBP 频率网格或频谱维度不匹配。")
        lower = max(1.0, psd_frequencies[0], rbp_frequencies[0])
        upper = min(50.0, psd_frequencies[-1], rbp_frequencies[-1])
        psd_mask = (psd_frequencies >= lower) & (psd_frequencies <= upper)
        rbp_mask = (rbp_frequencies >= lower) & (rbp_frequencies <= upper)
        frequencies = psd_frequencies[psd_mask]
        if len(frequencies) < 2 or not np.array_equal(frequencies, rbp_frequencies[rbp_mask]):
            raise ResultProjectionUnavailable("CUSTOM_FREQUENCY_SOURCE_MISMATCH", "PSD/RBP 支持范围没有一致的频率网格。")
        psd_values, rbp_values = psd_values[:, psd_mask], rbp_values[:, rbp_mask]

        quality = {channel: (
            _channel_quality(psd_result, channel), _channel_quality(rbp_result, channel),
        ) for channel in channels}
        base = {
            "recording_id": psd_identity["recording_id"],
            "start_sample": psd_identity["start_sample"],
            "end_sample": psd_identity["end_sample"],
        }
        return psd, rbp, channels, frequencies, psd_values, rbp_values, quality, base


def _scientific_context(payload: dict[str, Any]) -> dict[str, Any]:
    try:
        return json.loads(payload["scientific_configuration_json"])["context"]
    except (KeyError, TypeError, json.JSONDecodeError) as exc:
        raise ResultProjectionUnavailable("CUSTOM_FREQUENCY_SOURCE_MISMATCH", "分析窗口缺少可核验的科学配置。") from exc


def _provenance(payload: dict[str, Any]) -> CustomFrequencyProvenance:
    identity = payload["identity"]
    return CustomFrequencyProvenance(
        window_id=payload["window_id"], scientific_version=identity["scientific_version"],
        implementation_identity=identity["implementation_identity"],
        configuration_sha256=identity["configuration_sha256"],
        window_policy_version=identity["window_policy_version"],
    )


def _channel_quality(result: dict[str, Any], channel: str) -> tuple[str, list[str]]:
    by_channel = (((result.get("evidence") or {}).get("extensions") or {})
                  .get("channel_quality", {}).get("by_channel", {}))
    value = by_channel.get(channel, {}) if isinstance(by_channel, dict) else {}
    failures = (((result.get("evidence") or {}).get("extensions") or {})
                .get("channel_failures", {}).get("by_channel", {}))
    if isinstance(failures, dict) and channel in failures:
        return "unavailable", [str(failures[channel].get("code", "channel_unavailable"))]
    reasons = value.get("reasons", []) if isinstance(value, dict) else []
    return str(value.get("status", "unknown")), [str(reason) for reason in reasons]


def _valid_row(row: np.ndarray, quality: tuple[str, list[str]]) -> np.ndarray | None:
    return row if quality[0] in {"clean", "warning"} and np.isfinite(row).all() and (row >= 0).all() else None


def _bin_intervals(frequencies: np.ndarray) -> list[CustomFrequencyBinInterval]:
    lower = max(1.0, float(frequencies[0]))
    upper = min(50.0, float(frequencies[-1]))
    edges = np.empty(frequencies.size + 1, dtype=float)
    edges[0], edges[-1] = lower, upper
    edges[1:-1] = (frequencies[:-1] + frequencies[1:]) / 2.0
    return [CustomFrequencyBinInterval(low_hz=float(edges[i]), high_hz=float(edges[i + 1]))
            for i in range(frequencies.size)]


def _integral_v2(frequencies: np.ndarray, row: np.ndarray | None, low_hz: float, high_hz: float) -> float | None:
    if row is None:
        return None
    try:
        value = float(band_power(frequencies, row, low_hz, high_hz))
    except ValueError:
        return None
    return value if np.isfinite(value) and value >= 0 else None


def _scaled_density(row: np.ndarray | None, count: int, *, scale: float = 1e12) -> list[float | None]:
    return ([float(value * scale) if np.isfinite(value) and value >= 0 else None for value in row]
            if row is not None else [None] * count)
