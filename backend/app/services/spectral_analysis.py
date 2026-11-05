"""Offline spectral use cases built on a recording data loader.

The service owns analysis-only preprocessing cache and response envelopes.
File catalog and waveform responsibilities stay separate.
"""

from __future__ import annotations

import hashlib
import json
import logging
from collections.abc import Callable
from typing import Any

import numpy as np
from dataclasses import replace

from app.scientific.contracts.analysis import ANALYSIS_CONTRACT
from app.scientific.contracts.spectrogram import STFT_PREPROCESSING_VERSION
from app.core.provenance import implementation_version
from app.scientific.quality import SpectralQualityGateError
from app.scientific.primitives import DEFAULT_SPECTRAL_GATEWAY, ScientificSpectralGateway
from app.models.analysis_config import AnalysisConfigRequest
from app.models.recording import RecordingSummary
from app.services.analysis_preprocess_cache import AnalysisPreprocessCache, PreprocessedRecording
from app.services.prepared_source_contract import PreparedSourceContract
from app.services.analysis_input_validation import validate_recording_analysis_input


FREQUENCY_BANDS = {
    "delta": (1.0, 4.0),
    "theta": (4.0, 8.0),
    "alpha": (8.0, 13.0),
    "beta": (13.0, 30.0),
}
logger = logging.getLogger(__name__)
POWERLINE_NOTCH_HZ = float(ANALYSIS_CONTRACT["notch_hz"])
POWERLINE_NOTCH_Q = float(ANALYSIS_CONTRACT["notch_quality_factor"])


def _spectral_cache_identity(low_hz: float, high_hz: float, notch_hz: float | None = None) -> str:
    notch = "off" if notch_hz is None else f"{notch_hz:g}"
    return f"{ANALYSIS_CONTRACT['algorithm_version']}:bandpass={low_hz:g}-{high_hz:g}Hz:notch={notch}"


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
        bounds = payload["filter_frequency_range_hz"]
        filter_contract = {
            "bandpass_hz": [bounds["low_hz"], bounds["high_hz"]],
            "preprocessing_phase": "zero_phase", "notch_hz": payload["notch_hz"],
            "preprocessing_scope": "bounded_window_with_finite_context",
            "preprocessing": payload["preprocessing"],
        }
    return {
        "contract_version": "analysis-provenance-v1", "status": "completed", "analysis_type": mode,
        "definition_version": None, "scientific_algorithm_version": payload.get("analysis_algorithm_version", payload.get("baseline_algorithm_version", payload.get("algorithm_version"))),
        "implementation_version": implementation_version(), "config_sha256": config_hash, "mode": mode,
        "requested_range": requested_time, "actual_range": {"start_s": actual_start, "end_s": actual_end},
        "channel": list(payload["channels"])[0] if payload["channels"] else None,
        "channel_mapping": {"channels": list(payload["channels"]), "signal_source": payload.get("signal_source")}, "analysis_reference": payload.get("analysis_reference"),
        "sfreq_hz": payload.get("sfreq_hz"), "filter": filter_contract, "welch": welch,
        "frequency": {"low_hz": frequencies[0] if frequencies else None, "high_hz": frequencies[-1] if frequencies else None, "point_count": len(frequencies)},
        "quality": quality, "extensions": [],
    }


class SpectralAnalysisService:
    """Compute frozen PSD and spectrogram contracts from one recording loader."""

    def __init__(self, recordings: Any, preprocess_cache: AnalysisPreprocessCache, spectral_gateway: ScientificSpectralGateway | None = None):
        self.recordings = recordings
        self.preprocess_cache = preprocess_cache
        self.spectral_gateway = spectral_gateway or DEFAULT_SPECTRAL_GATEWAY

    def _preprocessed_recording(self, recording, low_hz, high_hz, notch_hz,
                                execution_context, channels=None):
        from app.services.mapped_preprocessing import mapped_preprocess
        from app.services.prepared_source_identity import prepared_source_identity

        selected_channels = None
        if channels and getattr(execution_context, "select_requested_channels", False):
            # An explicit projection policy may opt into a smaller source.
            # Ordinary on-demand work first shares the canonical full source
            # with waveform and other algorithms when the resource budget fits.
            available = {name.casefold(): name for name in recording.channels}
            selected_channels = tuple(available[item.casefold()] for item in channels)
        elif channels:
            policy = getattr(execution_context, "preparation_resource_policy", None)
            if policy is not None:
                sample_count = int(round(float(recording.duration_s) * float(recording.sfreq)))
                all_channel_bytes = sample_count * len(recording.channels) * np.dtype("float64").itemsize * 2
                if all_channel_bytes > policy.max_preparation_bytes:
                    available = {name.casefold(): name for name in recording.channels}
                    selected_channels = tuple(available[item.casefold()] for item in channels)
        identity = prepared_source_identity(self.recordings, recording, low_hz=low_hz,
            high_hz=high_hz, notch_hz=notch_hz, notch_q=POWERLINE_NOTCH_Q,
            channel_names=selected_channels)
        expected_contract = None
        if recording.sfreq is not None and recording.duration_s is not None and recording.channels:
            sample_count = int(round(float(recording.duration_s) * float(recording.sfreq)))
            contract_channels = selected_channels or tuple(recording.channels)
            unit_lookup = dict(zip(recording.channels, recording.channel_units or ("V",) * len(recording.channels)))
            expected_contract = PreparedSourceContract(
                schema_version="prepared-source-v1", identity=identity, status="ready",
                sample_rate_hz=float(recording.sfreq),
                prepared_start_sample=0, prepared_end_sample=sample_count,
                valid_start_sample=0, valid_end_sample=sample_count,
                channel_names=contract_channels,
                channel_units=tuple(unit_lookup[name] for name in contract_channels),
                preprocessing_manifest=identity, numerical_tolerance=1e-12,
                numerical_tolerance_evidence=(
                    "validated PSD full/on-demand comparison; "
                    "rtol=1e-12, atol=1e-24 V^2/Hz"
                ),
                filter_phase="zero_phase",
                reference_semantics=str(
                    self.recordings.analysis_signal_provenance(recording).get(
                        "analysis_reference", "as_recorded"
                    )
                ),
                gap_policy="preserve",
                boundary_policy="valid_only",
                implementation_identity=implementation_version(),
            )
        lookup = self.preprocess_cache.lookup(recording.id, identity, expected_contract)
        if not lookup.hit:
            logger.info(
                "prepared_source_cache_miss recording_id=%s identity=%s reason=%s",
                recording.id, identity, lookup.miss_reason,
            )
        def create_preprocessed() -> PreprocessedRecording:
            prepared = mapped_preprocess(
                self.recordings, recording, low_hz=low_hz, high_hz=high_hz,
                notch_hz=notch_hz, notch_q=POWERLINE_NOTCH_Q,
                channels=list(selected_channels) if selected_channels is not None else None,
                execution_context=execution_context,
            )
            if expected_contract is not None:
                prepared = replace(prepared, contract=expected_contract)
            return prepared
        cached = self.preprocess_cache.get_or_create(
            recording.id, identity, create_preprocessed, expected_contract=expected_contract,
        )
        completed = getattr(execution_context, "source_preparation_completed", None)
        if completed is not None:
            completed()
        return cached

    def load_spectrum(
        self,
        recording: RecordingSummary,
        start_s: float = 0.0,
        window_s: float = 30.0,
        channels: list[str] | None = None,
        *,
        low_hz: float = 1.0,
        high_hz: float = 50.0,
        notch_hz: float | None = POWERLINE_NOTCH_HZ,
        filter_low_hz: float | None = None,
        filter_high_hz: float | None = None,
        output_low_hz: float | None = None,
        output_high_hz: float | None = None,
        execution_context: Any = None,
        preprocessing_scope: str = "configured_continuous_before_slice",
        quality_policy: str = "strict",
        include_welch_segments: bool = False,
        _preprocessed: PreprocessedRecording | None = None,
    ) -> dict[str, object]:
        """Return the current PSD, absolute band power, and relative power contract."""
        if quality_policy not in {"strict", "per_channel_annotation"}:
            raise ValueError("Unsupported spectrum quality policy")
        filter_low_hz = float(low_hz if filter_low_hz is None else filter_low_hz)
        filter_high_hz = float(high_hz if filter_high_hz is None else filter_high_hz)
        output_low_hz = float(filter_low_hz if output_low_hz is None else output_low_hz)
        output_high_hz = float(filter_high_hz if output_high_hz is None else output_high_hz)
        if (not all(np.isfinite(value) for value in (filter_low_hz, filter_high_hz, output_low_hz, output_high_hz))
                or filter_low_hz <= 0.0 or filter_high_hz <= filter_low_hz
                or output_low_hz < 0.0 or output_high_hz <= output_low_hz):
            raise ValueError("PSD 频率范围无效")
        requested_start = float(start_s)
        requested_end = requested_start + float(window_s)
        # Filtering is part of the scientific contract. Never reuse a 1-30 Hz
        # preprocessed signal for a requested 0.5-50 Hz PSD (or vice versa).
        if notch_hz is not None and float(notch_hz) not in (50.0, 60.0):
            raise ValueError("陷波仅支持关闭、50Hz 或 60Hz")
        metadata_sfreq = getattr(recording, "sfreq", None)
        if metadata_sfreq is not None:
            nyquist_hz = float(metadata_sfreq) / 2.0
            if filter_high_hz >= nyquist_hz or output_high_hz >= nyquist_hz:
                raise ValueError(f"PSD 最高频率必须低于奈奎斯特频率（{nyquist_hz:g}Hz）")
            if notch_hz is not None and float(notch_hz) >= nyquist_hz:
                raise ValueError(f"陷波频率必须低于奈奎斯特频率（{nyquist_hz:g}Hz）")
        bounded = preprocessing_scope == "tnk_window_scoped_v1"
        bounded_evidence: dict[str, object] | None = None
        bounded_start_sample: int | None = None
        if bounded:
            from app.services.tnk_window_preprocessing import prepare_tnk_window

            sfreq = float(recording.sfreq)
            target_start = int(np.floor(requested_start * sfreq))
            target_end = target_start + int(round(float(window_s) * sfreq))
            bounded_start_sample = target_start
            prepared = prepare_tnk_window(
                self.recordings, recording, start_sample=target_start,
                end_sample=target_end, channels=channels,
                low_hz=filter_low_hz, high_hz=filter_high_hz,
                notch_hz=notch_hz, execution_context=execution_context,
            )
            filtered_all, sfreq, names = prepared.data, prepared.sfreq, list(prepared.channel_names)
            bounded_evidence = prepared.evidence
        else:
            cached = _preprocessed or self._preprocessed_recording(
                recording, filter_low_hz, filter_high_hz, notch_hz, execution_context,
                channels=channels)
            filtered_all, sfreq, names = cached.data, cached.sfreq, list(cached.channel_names)
        available = {name.casefold(): name for name in names}
        requested = names if channels is None else [available.get(item.casefold()) for item in channels]
        if any(item is None for item in requested):
            raise ValueError("频谱分析请求包含不存在的通道")
        requested_names = [item for item in requested if item is not None]
        if not requested_names:
            raise ValueError("频谱分析至少需要一个通道")
        indexes = [names.index(item) for item in requested_names]
        if filter_high_hz >= sfreq / 2.0 or output_high_hz >= sfreq / 2.0:
            raise ValueError(f"PSD 最高频率必须低于奈奎斯特频率（{sfreq / 2:g}Hz）")
        if notch_hz is not None and float(notch_hz) >= sfreq / 2.0:
            raise ValueError(f"陷波频率必须低于奈奎斯特频率（{sfreq / 2:g}Hz）")
        duration_s = float(recording.duration_s) if bounded else len(filtered_all) / sfreq
        validate_recording_analysis_input(
            duration_s,
            sfreq,
            names,
            requested_start,
            requested_end,
            requested_names,
        )
        actual_start = (
            bounded_start_sample / sfreq
            if bounded_start_sample is not None else requested_start
        )
        start_index = 0 if bounded else int(np.floor(actual_start * sfreq))
        stop_index = len(filtered_all) if bounded else min(
            len(filtered_all), start_index + int(round(float(window_s) * sfreq)),
        )
        window = filtered_all[start_index:stop_index, :][:, indexes]
        if len(window) < int(round(float(ANALYSIS_CONTRACT["welch_segment_s"]) * sfreq)):
            raise ValueError("频谱分析窗口至少需要 4 秒")
        # Keep the standard 1-30 Hz bands available as evidence even when the
        # requested PSD display range is narrower. The final returned axis is
        # still clipped to exactly [low_hz, high_hz].
        band_floor = min(edge[0] for edge in FREQUENCY_BANDS.values())
        band_ceiling = max(edge[1] for edge in FREQUENCY_BANDS.values())
        estimator = (self.spectral_gateway.welch_annotated_channels
                     if quality_policy == "per_channel_annotation" else self.spectral_gateway.welch)
        estimator_args = {
            "low_hz": min(filter_low_hz, output_low_hz, band_floor),
            "high_hz": max(filter_high_hz, output_high_hz, band_ceiling),
        }
        if include_welch_segments and quality_policy == "strict":
            spectrum = self.spectral_gateway.welch(
                window, sfreq, **estimator_args, include_segments=True,
            )
        else:
            spectrum = estimator(window, sfreq, **estimator_args)
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
                    if (edges[0] >= spectrum.freqs[0] and edges[1] <= spectrum.freqs[-1]
                        and np.isfinite(spectrum.psd[index]).all())
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
        result = {
            "recording_id": recording.id,
            "window_start_s": actual_start,
            "window_duration_s": len(window) / sfreq,
            "sfreq_hz": sfreq,
            "channels": requested_names,
            "analysis_reference": self.recordings.analysis_signal_provenance(recording)["analysis_reference"],
            "signal_source": self.recordings.analysis_signal_provenance(recording),
            "algorithm_version": (
                bounded_evidence["algorithm_version"]
                if bounded_evidence is not None else ANALYSIS_CONTRACT["algorithm_version"]
            ),
            "filter_contract": {**{key: ANALYSIS_CONTRACT[key] for key in (
                "bandpass_type", "bandpass_prototype_order", "bandpass_hz", "preprocessing_phase", "filter_form",
            )}, "bandpass_hz": [filter_low_hz, filter_high_hz], "notch_hz": notch_hz,
            "notch_quality_factor": POWERLINE_NOTCH_Q if notch_hz is not None else None,
            "preprocessing_scope": preprocessing_scope,
            "preprocessing": bounded_evidence,
            },
            "welch_contract": {key: ANALYSIS_CONTRACT[key] for key in (
                "welch_segment_s", "welch_segment_overlap", "welch_step_s", "welch_window", "welch_scaling",
            )},
            "units": {"psd": "uV^2/Hz", "absolute_power": "uV^2", "relative_power": "ratio"},
            "frequencies_hz": frequencies.tolist(),
            "psd": {name: [float(value) if np.isfinite(value) else None for value in psd_values[index]]
                    for index, name in enumerate(requested_names)},
            "frequency_range_hz": {"low_hz": output_low_hz, "high_hz": output_high_hz},
            "filter_frequency_range_hz": {"low_hz": filter_low_hz, "high_hz": filter_high_hz},
            "band_power": absolute,
            "relative_band_power": relative,
            "quality": {
                "policy": quality_policy,
                "by_channel": dict(zip(requested_names, spectrum.evidence.get("channel_quality", []))),
                "clean_segments": spectrum.clean_epochs,
                "total_segments": spectrum.total_epochs,
                "clean_ratio": spectrum.signal_quality,
                "gate_failed": spectrum.gate_failed,
                "rejected_reasons": list(spectrum.rejected_reasons),
            },
        }
        if include_welch_segments and spectrum.welch_segments_psd is not None:
            result["welch_segments"] = {
                "offsets_s": list(spectrum.welch_segment_offsets_s),
                "psd_uV2_per_hz": (spectrum.welch_segments_psd[:, :, mask] * 1e12).tolist(),
            }
        return result

    def load_spectrogram(
        self, recording: RecordingSummary, start_s: float = 0.0,
        window_s: float = 30.0, channels: list[str] | None = None, *,
        low_hz: float = 1.0, high_hz: float = 50.0,
        notch_hz: float | None = POWERLINE_NOTCH_HZ, output_low_hz: float | None = None,
        output_high_hz: float | None = None, execution_context: Any = None,
    ) -> dict[str, object]:
        """Execute current STFT without entering whole-record preprocessing."""
        from app.services.spectrogram_source import load_spectrogram

        return load_spectrogram(
            self.recordings, self.spectral_gateway, recording, start_s, window_s, channels,
            low_hz=low_hz, high_hz=high_hz, notch_hz=notch_hz,
            output_low_hz=output_low_hz, output_high_hz=output_high_hz,
            execution_context=execution_context,
        )

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
            "baseline_algorithm_version": ANALYSIS_CONTRACT["algorithm_version"],
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
        """Add the current bounded spectrogram traceability envelope."""
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
            "frequency_range_hz": [payload["frequency_range_hz"]["low_hz"], payload["frequency_range_hz"]["high_hz"]],
            "preprocessing": payload["preprocessing"],
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
            "algorithm_version": "spectrogram-v4-bounded-notch-configurable",
            "analysis_algorithm_version": STFT_PREPROCESSING_VERSION,
            "spectrogram_contract_version": "spectrogram-v4-bounded-notch",
            "baseline_algorithm_version": "spectrogram-v4-bounded-notch",
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
