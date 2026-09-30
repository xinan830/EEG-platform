"""Offline spectral use cases built on a recording data loader.

The service owns analysis-only preprocessing cache and response envelopes.
``RecordingService`` remains the public compatibility facade for existing
routes, while file catalog and waveform responsibilities stay separate.
"""

from __future__ import annotations

import hashlib
import json
from collections.abc import Callable
from typing import Any

import numpy as np

from app.scientific.contracts.analysis import ANALYSIS_CONTRACT
from app.core.provenance import implementation_version
from app.scientific.quality import SpectralQualityGateError
from app.scientific.primitives import DEFAULT_SPECTRAL_GATEWAY, ScientificSpectralGateway
from app.scientific.primitives import preprocess_offline as preprocess_offline
from app.models.analysis_config import AnalysisConfigRequest
from app.models.recording import RecordingSummary
from app.services.analysis_preprocess_cache import AnalysisPreprocessCache, PreprocessedRecording
from app.services.analysis_input_validation import validate_recording_analysis_input


FREQUENCY_BANDS = {
    "delta": (1.0, 4.0),
    "theta": (4.0, 8.0),
    "alpha": (8.0, 13.0),
    "beta": (13.0, 30.0),
}
POWERLINE_NOTCH_HZ = 50.0
POWERLINE_NOTCH_Q = 30.0
_AUTO_NOTCH = object()


def _notch_for_requested_range(high_hz: float) -> float | None:
    """Use the mains notch only for ranges that expose the power-line band."""
    return POWERLINE_NOTCH_HZ if high_hz >= 45.0 else None


def _spectral_cache_identity(low_hz: float, high_hz: float, notch_hz: float | None = None) -> str:
    notch = "off" if notch_hz is None else f"{notch_hz:g}"
    return f"{ANALYSIS_CONTRACT['algorithm_version']}:bandpass={low_hz:g}-{high_hz:g}Hz:notch={notch}"


class _ServiceSpectralGateway(ScientificSpectralGateway):
    """Default adapter retaining the historical preprocess monkeypatch seam."""

    def preprocess(
        self,
        data: np.ndarray,
        sfreq_hz: float,
        *,
        low_hz: float | None = None,
        high_hz: float | None = None,
        notch_hz: float | None | object = _AUTO_NOTCH,
        notch_q: float = POWERLINE_NOTCH_Q,
    ) -> np.ndarray:
        # Keep the established two-argument monkeypatch seam for the frozen
        # default contract. Non-default ranges must use the explicit contract.
        default_low, default_high = (float(value) for value in ANALYSIS_CONTRACT["bandpass_hz"])
        if low_hz == default_low and high_hz == default_high and notch_hz is None:
            return preprocess_offline(data, sfreq_hz)
        return preprocess_offline(
            data, sfreq_hz, low_hz=low_hz, high_hz=high_hz,
            notch_hz=notch_hz, notch_q=notch_q,
        )


def _configured_provenance(payload: dict[str, object], *, mode: str, requested_time: dict[str, float], actual_start: float, actual_end: float, config_hash: str, quality: dict[str, object]) -> dict[str, object]:
    frequencies = list(payload["frequencies_hz"])
    welch_source = payload.get("welch_contract")
    if isinstance(welch_source, dict):
        welch = {
            "segment_s": welch_source.get("welch_segment_s"), "window": welch_source.get("welch_window"),
            "overlap_fraction": welch_source.get("welch_segment_overlap"), "step_s": welch_source.get("welch_step_s"),
        }
        filter_contract = payload.get("filter_contract")
    else:
        welch = {"segment_s": payload.get("segment_s"), "window": "hann", "overlap_fraction": None, "step_s": payload.get("step_s")}
        filter_contract = {"bandpass_hz": [1.0, 30.0], "preprocessing_phase": "zero_phase"}
    return {
        "contract_version": "analysis-provenance-v1", "status": "completed", "analysis_type": mode,
        "definition_version": None, "scientific_algorithm_version": payload.get("analysis_algorithm_version", payload.get("baseline_algorithm_version", payload.get("algorithm_version"))),
        "implementation_version": implementation_version(), "config_sha256": config_hash, "mode": mode,
        "requested_range": requested_time, "actual_range": {"start_s": actual_start, "end_s": actual_end},
        "channel": list(payload["channels"])[0] if payload["channels"] else None,
        "channel_mapping": {"channels": list(payload["channels"])}, "analysis_reference": payload.get("analysis_reference"),
        "sfreq_hz": payload.get("sfreq_hz"), "filter": filter_contract, "welch": welch,
        "frequency": {"low_hz": frequencies[0] if frequencies else None, "high_hz": frequencies[-1] if frequencies else None, "point_count": len(frequencies)},
        "quality": quality, "extensions": [],
    }


class SpectralAnalysisService:
    """Compute frozen PSD and spectrogram contracts from one recording loader."""

    def __init__(self, recordings: Any, preprocess_cache: AnalysisPreprocessCache, spectral_gateway: ScientificSpectralGateway | None = None):
        self.recordings = recordings
        self.preprocess_cache = preprocess_cache
        self.spectral_gateway = spectral_gateway or _ServiceSpectralGateway()

    def load_spectrum(
        self,
        recording: RecordingSummary,
        start_s: float = 0.0,
        window_s: float = 30.0,
        channels: list[str] | None = None,
        *,
        low_hz: float = 1.0,
        high_hz: float = 30.0,
        notch_hz: float | None | object = _AUTO_NOTCH,
        filter_low_hz: float | None = None,
        filter_high_hz: float | None = None,
        output_low_hz: float | None = None,
        output_high_hz: float | None = None,
    ) -> dict[str, object]:
        """Return frozen v3 PSD, absolute band power, and relative power."""
        filter_low_hz = float(low_hz if filter_low_hz is None else filter_low_hz)
        filter_high_hz = float(high_hz if filter_high_hz is None else filter_high_hz)
        output_low_hz = float(filter_low_hz if output_low_hz is None else output_low_hz)
        output_high_hz = float(filter_high_hz if output_high_hz is None else output_high_hz)
        if (not all(np.isfinite(value) for value in (filter_low_hz, filter_high_hz, output_low_hz, output_high_hz))
                or filter_low_hz <= 0.0 or filter_high_hz <= filter_low_hz
                or output_low_hz < 0.0 or output_high_hz <= output_low_hz):
            raise ValueError("PSD 频率范围无效")
        version = str(ANALYSIS_CONTRACT["algorithm_version"])
        # Filtering is part of the scientific contract. Never reuse a 1-30 Hz
        # preprocessed signal for a requested 0.5-50 Hz PSD (or vice versa).
        if notch_hz is _AUTO_NOTCH:
            notch_hz = _notch_for_requested_range(filter_high_hz)
        elif notch_hz is not None and float(notch_hz) not in (50.0, 60.0):
            raise ValueError("陷波仅支持关闭、50Hz 或 60Hz")
        cache_identity = _spectral_cache_identity(filter_low_hz, filter_high_hz, notch_hz)
        cached = self.preprocess_cache.get(recording.id, cache_identity)
        if cached is None:
            data, sfreq, names, _events = self.recordings.load_data(recording)
            filtered_all = self.spectral_gateway.preprocess(
                np.asarray(data, dtype=float), sfreq, low_hz=filter_low_hz, high_hz=filter_high_hz,
                notch_hz=notch_hz, notch_q=POWERLINE_NOTCH_Q,
            )
            cached = PreprocessedRecording(filtered_all, sfreq, tuple(names))
            self.preprocess_cache.put(recording.id, cache_identity, cached)
        filtered_all, sfreq, names = cached.data, cached.sfreq, list(cached.channel_names)
        available = {name.casefold(): name for name in names}
        requested = names if channels is None else [available.get(item.casefold()) for item in channels]
        if any(item is None for item in requested):
            raise ValueError("频谱分析请求包含不存在的通道")
        requested_names = [item for item in requested if item is not None]
        if not requested_names:
            raise ValueError("频谱分析至少需要一个通道")
        indexes = [names.index(item) for item in requested_names]
        filtered = filtered_all[:, indexes]
        duration_s = len(filtered) / sfreq
        actual_start = max(0.0, min(float(start_s), duration_s))
        if filter_high_hz >= sfreq / 2.0 or output_high_hz >= sfreq / 2.0:
            raise ValueError(f"PSD 最高频率必须低于奈奎斯特频率（{sfreq / 2:g}Hz）")
        validate_recording_analysis_input(duration_s, sfreq, names, actual_start, duration_s, requested_names)
        start_index = int(np.floor(actual_start * sfreq))
        stop_index = min(len(filtered), start_index + int(round(float(window_s) * sfreq)))
        window = filtered[start_index:stop_index]
        if len(window) < int(round(float(ANALYSIS_CONTRACT["welch_segment_s"]) * sfreq)):
            raise ValueError("频谱分析窗口至少需要 4 秒")
        # Keep the standard 1-30 Hz bands available as evidence even when the
        # requested PSD display range is narrower. The final returned axis is
        # still clipped to exactly [low_hz, high_hz].
        band_floor = min(edge[0] for edge in FREQUENCY_BANDS.values())
        band_ceiling = max(edge[1] for edge in FREQUENCY_BANDS.values())
        spectrum = self.spectral_gateway.welch(
            window, sfreq, low_hz=min(filter_low_hz, output_low_hz, band_floor),
            high_hz=max(filter_high_hz, output_high_hz, band_ceiling),
        )
        if spectrum.gate_failed:
            raise SpectralQualityGateError({
                "clean_segments": spectrum.clean_epochs,
                "total_segments": spectrum.total_epochs,
                "clean_ratio": spectrum.signal_quality,
                "gate_failed": spectrum.gate_failed,
                "rejected_reasons": list(spectrum.rejected_reasons),
                "evidence": dict(spectrum.evidence),
            })
        psd_uv = spectrum.psd * 1e12
        absolute = {
            name: {
                band: (
                    float(self.spectral_gateway.integrate_band(spectrum.freqs, spectrum.psd[index], *edges) * 1e12)
                    if edges[0] >= spectrum.freqs[0] and edges[1] <= spectrum.freqs[-1]
                    else None
                )
                for band, edges in FREQUENCY_BANDS.items()
            }
            for index, name in enumerate(requested_names)
        }
        relative = {
            name: {
                band: value / total if value is not None and total > 0 else None
                for band, value in values.items()
            }
            for name, values in absolute.items()
            for total in [sum(value for value in values.values() if value is not None)]
        }
        mask = (spectrum.freqs >= output_low_hz) & (spectrum.freqs <= output_high_hz)
        if not np.any(mask):
            raise ValueError("请求的 PSD 频率范围没有可用频率点")
        frequencies = spectrum.freqs[mask]
        psd_values = psd_uv[:, mask]
        return {
            "recording_id": recording.id,
            "window_start_s": actual_start,
            "window_duration_s": len(window) / sfreq,
            "sfreq_hz": sfreq,
            "channels": requested_names,
            "analysis_reference": ANALYSIS_CONTRACT["reference"],
            "algorithm_version": ANALYSIS_CONTRACT["algorithm_version"],
            "filter_contract": {**{key: ANALYSIS_CONTRACT[key] for key in (
                "bandpass_type", "bandpass_prototype_order", "bandpass_hz", "preprocessing_phase", "filter_form",
            )}, "bandpass_hz": [filter_low_hz, filter_high_hz], "notch_hz": notch_hz,
            "notch_quality_factor": POWERLINE_NOTCH_Q if notch_hz is not None else None},
            "welch_contract": {key: ANALYSIS_CONTRACT[key] for key in (
                "welch_segment_s", "welch_segment_overlap", "welch_step_s", "welch_window", "welch_scaling",
            )},
            "units": {"psd": "uV^2/Hz", "absolute_power": "uV^2", "relative_power": "ratio"},
            "frequencies_hz": frequencies.tolist(),
            "psd": {name: psd_values[index].tolist() for index, name in enumerate(requested_names)},
            "frequency_range_hz": {"low_hz": output_low_hz, "high_hz": output_high_hz},
            "filter_frequency_range_hz": {"low_hz": filter_low_hz, "high_hz": filter_high_hz},
            "band_power": absolute,
            "relative_band_power": relative,
            "quality": {
                "clean_segments": spectrum.clean_epochs,
                "total_segments": spectrum.total_epochs,
                "clean_ratio": spectrum.signal_quality,
                "gate_failed": spectrum.gate_failed,
                "rejected_reasons": list(spectrum.rejected_reasons),
            },
        }

    def load_spectrogram(
        self,
        recording: RecordingSummary,
        start_s: float = 0.0,
        window_s: float = 30.0,
        channels: list[str] | None = None,
        *,
        low_hz: float = 1.0,
        high_hz: float = 50.0,
        notch_hz: float | None = None,
        output_low_hz: float | None = None,
        output_high_hz: float | None = None,
    ) -> dict[str, object]:
        """Return spectrogram-v2 from the continuous v3-preprocessed signal."""
        output_low = float(output_low_hz if output_low_hz is not None else low_hz)
        output_high = float(output_high_hz if output_high_hz is not None else high_hz)
        payload = self.load_spectrum(
            recording, start_s=start_s, window_s=window_s, channels=channels,
            low_hz=low_hz, high_hz=high_hz, notch_hz=notch_hz,
            filter_low_hz=low_hz, filter_high_hz=high_hz,
            output_low_hz=output_low, output_high_hz=output_high,
        )
        names = list(payload["channels"])
        default_low, default_high = float(low_hz), float(high_hz)
        cached = self.preprocess_cache.get(recording.id, _spectral_cache_identity(default_low, default_high, notch_hz))
        if cached is None:
            raise RuntimeError("STFT 预处理缓存未命中：滤波范围或陷波参数与当前请求不一致")
        indexes = [list(cached.channel_names).index(name) for name in names]
        start_index = int(np.floor(float(start_s) * cached.sfreq))
        stop_index = min(len(cached.data), start_index + int(round(float(window_s) * cached.sfreq)))
        times, freqs, values, quality = self.spectral_gateway.spectrogram(
            cached.data[start_index:stop_index, :][:, indexes], cached.sfreq,
            low_hz=output_low, high_hz=output_high,
        )
        frequency_mask = (freqs >= output_low) & (freqs <= output_high)
        if not np.any(frequency_mask):
            raise ValueError("请求的 STFT 频率范围没有可用频率点")
        freqs = freqs[frequency_mask]
        values = values[:, :, frequency_mask]
        power_uv = values * 1e12
        power_db = 10.0 * np.log10(np.maximum(power_uv, np.finfo(float).tiny))
        linear_values = {name: power_uv[:, index, :].tolist() for index, name in enumerate(names)}
        db_values = {name: power_db[:, index, :].tolist() for index, name in enumerate(names)}
        band_series = {
            name: {
                band: [
                    float(self.spectral_gateway.integrate_band(freqs, row, low, high) * 1e12) if np.isfinite(row).all() else float("nan")
                    for row in values[:, index, :]
                ]
                for band, (low, high) in FREQUENCY_BANDS.items()
            }
            for index, name in enumerate(names)
        }
        for item in quality:
            item["center_s"] = float(item["center_s"]) + float(start_s)
            item["start_s"] = float(item["start_s"]) + float(start_s)
            item["end_s"] = float(item["end_s"]) + float(start_s)
        return {
            "recording_id": recording.id,
            "window_start_s": float(start_s),
            "window_duration_s": (stop_index - start_index) / cached.sfreq,
            "sfreq_hz": float(cached.sfreq),
            "channels": names,
            "times_s": (times + float(start_s)).tolist(),
            "frequencies_hz": freqs.tolist(),
            "power": linear_values,
            "power_linear": linear_values,
            "power_db": db_values,
            "band_power_timeseries": band_series,
            "units": "uV^2/Hz",
            "power_linear_units": "uV^2/Hz",
            "power_db_units": "dB re 1 uV^2/Hz",
            "band_power_timeseries_units": "uV^2",
            "analysis_algorithm_version": "offline-spectral-v3",
            "spectrogram_contract_version": "spectrogram-v2",
            "algorithm_version": "spectrogram-v2",
            "frequency_range_hz": {"low_hz": output_low, "high_hz": output_high},
            "filter_frequency_range_hz": {"low_hz": float(low_hz), "high_hz": float(high_hz)},
            "notch_hz": notch_hz,
            "segment_s": 4.0,
            "step_s": 1.0,
            "quality": {
                "windows": quality,
                "clean_windows": sum(item["status"] == "clean" for item in quality),
                "total_windows": len(quality),
                "bad_windows": sum(item["status"] == "bad" for item in quality),
                "evidence": {
                    "schema_version": "spectrogram-window-evidence-v1",
                    "transform_padding": {"used": False, "kind": "none", "samples": 0},
                    "gap_detected_windows": sum(bool(item["gap"]["detected"]) for item in quality),
                },
            },
        }

    def load_configured_spectrum(
        self,
        recording: RecordingSummary,
        config: AnalysisConfigRequest,
        spectrum_loader: Callable[..., dict[str, object]] | None = None,
    ) -> dict[str, object]:
        """Apply timing metadata around frozen PSD math without changing it."""
        requested = config.model_dump(mode="json")
        requested_duration = config.time.end_s - config.time.start_s
        loader = spectrum_loader or self.load_spectrum
        payload = loader(recording, config.time.start_s, requested_duration, config.channels)
        actual_start = float(payload["window_start_s"])
        actual_end = actual_start + float(payload["window_duration_s"])
        sample_tolerance = 1.5 / float(payload["sfreq_hz"])
        if config.mode == "static" and config.time.end_s > actual_end + sample_tolerance:
            raise ValueError(f"静态频谱分析区间超出文件范围，文件实际结束时间为 {actual_end:.3f} s")
        execution = {
            "mode": config.mode,
            "channels": payload["channels"],
            "requested_time": requested["time"],
            "actual_time": {"start_s": actual_start, "end_s": actual_end, "duration_s": actual_end - actual_start},
            "dynamic_window_s": config.dynamic_window_s,
            "refresh_step_s": config.refresh_step_s,
            "preprocessing": payload["filter_contract"],
            "welch": payload["welch_contract"],
        }
        canonical = json.dumps(execution, sort_keys=True, separators=(",", ":"), ensure_ascii=True)
        config_hash = hashlib.sha256(canonical.encode("utf-8")).hexdigest()[:12].upper()
        payload.update({
            "algorithm_version": "offline-spectral-v4-configurable",
            "baseline_algorithm_version": "offline-spectral-v3",
            "requested_config": requested,
            "execution_config": execution,
            "requested_start_s": config.time.start_s,
            "requested_end_s": config.time.end_s,
            "requested_window_s": requested_duration,
            "actual_start_s": actual_start,
            "actual_end_s": actual_end,
            "actual_duration_s": actual_end - actual_start,
            "analysis_config_hash": config_hash,
            "warmup": config.mode == "dynamic" and payload["window_duration_s"] < config.dynamic_window_s,
        })
        payload["analysis_provenance"] = _configured_provenance(payload, mode=config.mode, requested_time=requested["time"], actual_start=actual_start, actual_end=actual_end, config_hash=config_hash, quality=dict(payload["quality"]))
        return payload

    def load_configured_spectrogram(
        self,
        recording: RecordingSummary,
        config: AnalysisConfigRequest,
        spectrogram_loader: Callable[..., dict[str, object]] | None = None,
    ) -> dict[str, object]:
        """Add the configurable spectrogram-v2 traceability envelope."""
        if config.mode != "spectrogram":
            raise ValueError("时频图配置的 mode 必须为 spectrogram")
        requested = config.model_dump(mode="json")
        requested_duration = config.time.end_s - config.time.start_s
        loader = spectrogram_loader or self.load_spectrogram
        payload = loader(recording, config.time.start_s, requested_duration, config.channels)
        actual_start = float(payload["window_start_s"])
        actual_end = actual_start + float(payload["window_duration_s"])
        sample_tolerance = 1.5 / float(payload.get("sfreq_hz", 1.0))
        if config.time.end_s > actual_end + sample_tolerance:
            raise ValueError(f"时频图分析区间超出文件范围，文件实际结束时间为 {actual_end:.3f} s")
        custom_range = config.custom_frequency_range
        if custom_range is not None:
            freqs = np.asarray(payload["frequencies_hz"], dtype=float)
            source = payload.get("power_linear", payload["power"])
            custom_series: dict[str, list[float]] = {}
            for name in payload["channels"]:
                rows = np.asarray(source[name], dtype=float)
                custom_series[name] = [
                    float(self.spectral_gateway.integrate_band(freqs, row, custom_range.low_hz, custom_range.high_hz))
                    if np.isfinite(row).all() else float("nan")
                    for row in rows
                ]
            payload["custom_band_power_timeseries"] = custom_series
            payload["custom_band"] = {
                "low_hz": custom_range.low_hz,
                "high_hz": custom_range.high_hz,
                "unit": "uV^2",
                "integration": "trapezoid_with_interpolated_boundaries",
                "frequency_resolution_hz": float(freqs[1] - freqs[0]) if len(freqs) > 1 else None,
                "frequency_points_hz": freqs[(freqs >= custom_range.low_hz) & (freqs <= custom_range.high_hz)].tolist(),
                "algorithm_version": "spectrogram-custom-band-v1",
            }
        execution = {
            "mode": "spectrogram",
            "channels": payload["channels"],
            "requested_time": requested["time"],
            "actual_time": {"start_s": actual_start, "end_s": actual_end, "duration_s": actual_end - actual_start},
            "segment_s": payload["segment_s"],
            "step_s": payload["step_s"],
            "frequency_range_hz": [1.0, 30.0],
            "custom_frequency_range": requested.get("custom_frequency_range"),
            "time_axis": "window_center",
            "matrix_shape": [len(payload["times_s"]), len(payload["frequencies_hz"])],
            "quality_gate": "shared_peak_threshold_per_window",
        }
        canonical = json.dumps(execution, sort_keys=True, separators=(",", ":"), ensure_ascii=True)
        time_bins = len(payload["times_s"])
        frequency_bins = len(payload["frequencies_hz"])
        config_hash = hashlib.sha256(canonical.encode("utf-8")).hexdigest()[:12].upper()
        payload.update({
            "algorithm_version": "spectrogram-v2-configurable",
            "analysis_algorithm_version": "offline-spectral-v3",
            "spectrogram_contract_version": "spectrogram-v2",
            "baseline_algorithm_version": "spectrogram-v2",
            "requested_config": requested,
            "execution_config": execution,
            "requested_start_s": config.time.start_s,
            "requested_end_s": config.time.end_s,
            "requested_window_s": requested_duration,
            "actual_start_s": actual_start,
            "actual_end_s": actual_end,
            "actual_duration_s": actual_end - actual_start,
            "analysis_config_hash": config_hash,
            "warmup": False,
            "time_bins": time_bins,
            "frequency_bins": frequency_bins,
            "matrix_shape": [time_bins, frequency_bins],
            "first_center_s": payload["times_s"][0] if time_bins else None,
            "last_center_s": payload["times_s"][-1] if time_bins else None,
        })
        payload["analysis_provenance"] = _configured_provenance(payload, mode="spectrogram", requested_time=requested["time"], actual_start=actual_start, actual_end=actual_end, config_hash=config_hash, quality=dict(payload.get("quality", {})))
        return payload
