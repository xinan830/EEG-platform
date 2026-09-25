"""Numerical execution for persisted analysis runs.

Run lifecycle, caching, persistence, and artifacts remain in ``RunService``.
This class deliberately owns only the pre-existing analysis dispatch paths.
"""

from __future__ import annotations

import json
from typing import Any

import numpy as np

from app.scientific.primitives import SpectralEstimate
from app.algorithm_runtime.contracts import (
    DYNAMIC_ANALYSIS_RESULT_CONTRACT_VERSION,
    AlgorithmEvidence,
    AlgorithmResult,
    AlgorithmStructuredResult,
    AlgorithmStructuredSeriesResult,
)
from app.algorithm_runtime.executor import AlgorithmRuntime
from app.algorithm_runtime.registry import AlgorithmRegistry
from app.models.analysis_config import AnalysisConfigRequest
from app.models.official_algorithm_run import OfficialAlgorithmRunConfig


class _RecordingAlgorithmContext:
    """Bounded signal access passed to a canonical algorithm module."""

    def __init__(self, recordings: Any, recording: Any) -> None:
        self._recordings = recordings
        self._recording = recording
        self.id = recording.id
        self.channel_names = list(recording.channels)
        self.sfreq_hz = float(recording.sfreq or 0.0)
        self.duration_s = float(recording.duration_s or 0.0)

    def load_spectrum(self, *, start_s: float, window_s: float, channels: list[str]) -> SpectralEstimate:
        payload = self._recordings.load_spectrum(self._recording, start_s, window_s, channels)
        ordered = list(payload["channels"])
        selected = ordered[0]
        evidence = {
            "sfreq_hz": float(payload["sfreq_hz"]),
            "analysis_reference": payload["analysis_reference"],
            "algorithm_version": payload["algorithm_version"],
            "filter_contract": payload["filter_contract"],
            "welch_contract": payload["welch_contract"],
            "units": payload["units"],
            "frequencies_hz": list(payload["frequencies_hz"]),
            "channels": ordered,
            "psd_uV2_per_hz": list(payload["psd"][selected]),
            "band_power": dict(payload["band_power"][selected]),
            "relative_band_power": dict(payload["relative_band_power"][selected]),
            "quality": dict(payload["quality"]),
        }
        return SpectralEstimate(
            np.asarray(payload["frequencies_hz"], dtype=float),
            np.asarray([payload["psd"][name] for name in ordered], dtype=float) * 1e-12,
            float(payload["quality"]["clean_ratio"]),
            int(payload["quality"]["clean_segments"]),
            int(payload["quality"]["total_segments"]),
            payload["quality"]["gate_failed"],
            tuple(payload["quality"].get("rejected_reasons", [])),
            evidence,
        )

    def load_spectrogram(self, *, start_s: float, window_s: float, channels: list[str]) -> dict[str, object]:
        """Load the frozen spectrogram contract through the recording service boundary."""
        return self._recordings.load_spectrogram(
            self._recording,
            start_s=start_s,
            window_s=window_s,
            channels=channels,
        )

    def load_faa_signals(self, *, start_s: float, end_s: float, f3_channel: str, f4_channel: str) -> tuple[np.ndarray, np.ndarray, float, dict[str, Any]]:
        """Load the exact raw pair consumed by the frozen FAA implementation."""
        data, sfreq, names, _events = self._recordings.load_data(self._recording)
        lookup = {str(name).casefold(): (index, str(name)) for index, name in enumerate(names)}
        try:
            f3_index, f3_name = lookup[f3_channel.casefold()]
            f4_index, f4_name = lookup[f4_channel.casefold()]
        except KeyError as exc:
            raise ValueError(f"FAA source channel does not exist: {exc.args[0]}") from exc
        values = np.asarray(data, dtype=float)
        start_index = max(0, int(np.floor(start_s * sfreq)))
        end_index = min(len(values), int(round(end_s * sfreq)))
        if end_index <= start_index:
            raise ValueError("FAA analysis range is outside recording duration")
        evidence = {
            "sfreq_hz": float(sfreq), "channels": [f3_name, f4_name],
            "analysis_reference": "original_recording_no_software_rereference",
            "time_scope": "exact_requested_absolute_range",
            "requested_range_s": {"start_s": float(start_s), "end_s": float(end_s)},
            "actual_range_s": {"start_s": float(start_index / sfreq), "end_s": float(end_index / sfreq)},
            "faa_contract": {
                "method": "paired_epoch_rfft_density",
                "epoch_s": 2.0,
                "overlap_fraction": 0.5,
                "step_s": 1.0,
                "window": "hann",
                "detrend": "per_epoch_mean_removal",
                "software_bandpass": "not_applied",
                "minimum_clean_epochs": 10,
                "artifact_peak_uv": 150.0,
                "alpha_band_hz": [8.0, 13.0],
                "frequency_resolution_hz": 1.0 / 2.0,
            },
        }
        return values[start_index:end_index, f3_index], values[start_index:end_index, f4_index], float(sfreq), evidence


def _public_evidence(evidence: dict[str, Any]) -> dict[str, Any]:
    """Expose common evidence plus named extensions without leaking a generic bag."""
    parsed = AlgorithmEvidence.model_validate(evidence)
    return {
        "source_quality": parsed.source_quality,
        "spectral_evidence": parsed.spectral_evidence,
        "calculation_trace": parsed.calculation_trace,
        **parsed.extensions,
    }


def _serialize_algorithm_result(result: AlgorithmResult, algorithm_id: str, label: str) -> dict[str, object]:
    evidence = _public_evidence(result.evidence)
    quality = {"status": result.quality, "reasons": [result.failure.code] if result.failure else []}
    payload: dict[str, object] = {
        "output": {"id": algorithm_id, "label": label, "value": result.value, "unit": result.unit, "quality": quality},
        "channel": result.channel,
        "actual_range": result.actual_range,
        "requested_range": result.requested_range,
        "source_quality": evidence["source_quality"],
        "spectral_evidence": evidence["spectral_evidence"],
        "calculation_trace": evidence["calculation_trace"],
        "official": {"algorithm_id": algorithm_id, **evidence},
        "chart": {"kind": "none"},
        "value": result.value,
        "quality": quality,
        "failure": result.failure.model_dump(mode="json") if result.failure else None,
    }
    if result.output_values is not None:
        payload["band_values"] = result.output_values
        payload["chart"] = {"kind": "band_share", "values": result.output_values, "unit": result.unit}
    return payload


def _serialize_structured_result(result: AlgorithmStructuredResult, algorithm_id: str, label: str) -> tuple[dict[str, object], dict[str, np.ndarray]]:
    """Keep structured axes/arrays in the artifact boundary, not JSON."""
    evidence = _public_evidence(result.evidence)
    arrays = {name: np.asarray(value) for name, value in result.arrays.items()}
    array_units = dict(result.array_units)
    axis_metadata: dict[str, object] = {}
    for name, value in result.axes.items():
        axis = np.asarray(value)
        key = f"axis_{name}"
        arrays[key] = axis
        array_units[key] = result.axis_units[name]
        axis_metadata[name] = {
            "array_key": key,
            "unit": result.axis_units[name],
            "length": int(axis.size),
        }
    quality = {"status": result.quality, "reasons": [result.failure.code] if result.failure else []}
    return {
        "output": {"id": algorithm_id, "label": label, "kind": result.output_kind, "quality": quality},
        "channel_order": result.channel_order,
        "requested_range": result.requested_range,
        "actual_range": result.actual_range,
        "axes": axis_metadata,
        "arrays": {
            name: {"unit": array_units[name], "shape": list(value.shape)}
            for name, value in arrays.items()
        },
        "source_quality": evidence["source_quality"],
        "spectral_evidence": evidence["spectral_evidence"],
        "calculation_trace": evidence["calculation_trace"],
        "official": {"algorithm_id": algorithm_id, **evidence},
        "quality": quality,
        "failure": result.failure.model_dump(mode="json") if result.failure else None,
    }, arrays


def _serialize_structured_series_result(
    result: AlgorithmStructuredSeriesResult,
    algorithm_id: str,
    label: str,
) -> tuple[dict[str, object], dict[str, np.ndarray]]:
    """Persist dynamic matrices while keeping window evidence out of arrays."""
    evidence = _public_evidence(result.evidence)
    arrays = {name: np.asarray(value) for name, value in result.arrays.items()}
    array_units = dict(result.array_units)
    arrays["window_evidence_json"] = np.asarray(json.dumps(
        [window.evidence for window in result.windows], ensure_ascii=False, sort_keys=True,
    ))
    array_units["window_evidence_json"] = "json"
    axis_metadata: dict[str, object] = {}
    for name, value in result.axes.items():
        axis = np.asarray(value)
        key = f"axis_{name}"
        arrays[key] = axis
        array_units[key] = result.axis_units[name]
        axis_metadata[name] = {"array_key": key, "unit": result.axis_units[name], "length": int(axis.size)}
    quality = {"status": result.quality, "reasons": [result.failure.code] if result.failure else []}
    state_counts: dict[str, int] = {}
    for window in result.windows:
        state_counts[window.state] = state_counts.get(window.state, 0) + 1
    return {
        "output": {"id": algorithm_id, "label": label, "kind": result.output_kind, "mode": "dynamic", "quality": quality},
        "channel_order": result.channel_order,
        "requested_range": result.requested_range,
        "actual_range": result.actual_range,
        "axes": axis_metadata,
        "arrays": {name: {"unit": array_units[name], "shape": list(value.shape)} for name, value in arrays.items()},
        "windows": [window.model_dump(mode="json", exclude={"evidence"}) for window in result.windows],
        "window_state_counts": state_counts,
        "source_quality": evidence["source_quality"],
        "spectral_evidence": evidence["spectral_evidence"],
        "calculation_trace": evidence["calculation_trace"],
        "official": {"algorithm_id": algorithm_id, **evidence},
        "quality": quality,
        "failure": result.failure.model_dump(mode="json") if result.failure else None,
    }, arrays


class RunAnalysisExecutor:
    def __init__(
        self,
        recordings: Any,
        algorithm_registry: AlgorithmRegistry | None = None,
    ):
        self.recordings = recordings
        self.algorithm_runtime = AlgorithmRuntime(algorithm_registry) if algorithm_registry is not None else None

    def execute(self, analysis_type: str, recording: Any, resolved: dict[str, Any]):
        if analysis_type == "official_algorithm":
            return self._execute_official_algorithm(recording, resolved)

        config = AnalysisConfigRequest.model_validate(resolved["config"])
        if analysis_type == "spectrum":
            payload = self.recordings.load_configured_spectrum(recording, config)
            frequencies = np.asarray(payload.pop("frequencies_hz"), dtype=float)
            psd = payload.pop("psd")
            arrays = {"frequency_hz": frequencies}
            for index, name in enumerate(payload["channels"]):
                arrays[f"psd_channel_{index}"] = np.asarray(psd[name], dtype=float)
            payload["artifact_channel_order"] = list(payload["channels"])
            return payload, arrays, "uV^2/Hz"

        payload = self.recordings.load_configured_spectrogram(recording, config)
        frequencies = np.asarray(payload.pop("frequencies_hz"), dtype=float)
        times = np.asarray(payload.pop("times_s"), dtype=float)
        linear = payload.pop("power_linear")
        payload.pop("power", None)
        db = payload.pop("power_db")
        arrays = {"frequency_hz": frequencies, "time_center_s": times}
        for index, name in enumerate(payload["channels"]):
            arrays[f"power_linear_channel_{index}"] = np.asarray(linear[name], dtype=float)
            arrays[f"power_db_channel_{index}"] = np.asarray(db[name], dtype=float)
        payload["artifact_channel_order"] = list(payload["channels"])
        return payload, arrays, "uV^2/Hz and dB re 1 uV^2/Hz"

    def _execute_official_algorithm(self, recording: Any, resolved: dict[str, Any]):
        config = OfficialAlgorithmRunConfig.model_validate(resolved["config"])
        if self.algorithm_runtime is None:
            raise ValueError("algorithm runtime is not configured")
        context = _RecordingAlgorithmContext(self.recordings, recording)
        runtime_config = config.runtime_config()
        result = self.algorithm_runtime.execute(
            algorithm_id=config.algorithm_id,
            scientific_version=resolved["scientific_version"],
            recording=context,
            config=runtime_config,
        )
        module = self.algorithm_runtime.registry.get(config.algorithm_id, resolved["scientific_version"])
        label = module.manifest.display_name_zh
        if isinstance(result, AlgorithmStructuredSeriesResult):
            structured, arrays = _serialize_structured_series_result(result, config.algorithm_id, label)
            return {"structured": structured}, arrays, None
        if isinstance(result, AlgorithmStructuredResult):
            structured, arrays = _serialize_structured_result(result, config.algorithm_id, label)
            return {"structured": structured}, arrays, None
        if isinstance(result, AlgorithmResult):
            point = _serialize_algorithm_result(result, config.algorithm_id, label)
            arrays = {"metric_value": np.asarray([np.nan if result.value is None else result.value], dtype=float)}
            if result.output_values is not None:
                arrays["band_share_values"] = np.asarray(list(result.output_values.values()), dtype=float)
            return {"metric": point}, arrays, result.unit
        points = []
        values = []
        for index, (value, time_s, window, quality, failure) in enumerate(zip(result.values, result.time_s, result.windows, result.quality, result.failures)):
            evidence = _public_evidence(result.point_evidence[index]) if index < len(result.point_evidence) else _public_evidence({})
            warmup = result.warmups[index] if index < len(result.warmups) else False
            analysis_state = result.states[index] if index < len(result.states) else (
                "Rejected" if failure is not None and quality == "gate_failed"
                else "Unavailable" if failure is not None
                else "Partial" if warmup else "Complete"
            )
            point = {"result_contract_version": DYNAMIC_ANALYSIS_RESULT_CONTRACT_VERSION,
                     "time_s": time_s, "window_start_s": window["start_s"], "window_end_s": window["end_s"], "value": value,
                     "quality": {"status": quality, "reasons": [failure.code] if failure else []},
                     "output": {"id": config.algorithm_id, "label": label, "value": value, "unit": result.unit, "quality": {"status": quality, "reasons": [failure.code] if failure else []}},
                     "channel": result.channel, "source_quality": evidence.get("source_quality", {}),
                     "spectral_evidence": evidence.get("spectral_evidence", {}), "warmup": warmup, "analysis_state": analysis_state,
                     "calculation_trace": evidence.get("calculation_trace", {}),
                     "official": {"algorithm_id": config.algorithm_id, **evidence}, "chart": {"kind": "none"}}
            points.append(point)
            values.append(np.nan if value is None else float(value))
        first = points[0] if points else {"output": {"id": config.algorithm_id, "label": label, "unit": result.unit}, "channel": result.channel}
        latest_evidence = _public_evidence(result.point_evidence[-1]) if result.point_evidence else _public_evidence({})
        return {"metric": {"result_contract_version": DYNAMIC_ANALYSIS_RESULT_CONTRACT_VERSION, "mode": "dynamic", "output": first["output"], "channel": result.channel, "actual_range": {"start_s": config.time.start_s, "end_s": config.time.end_s}, "dynamic_contract": {"window_s": config.dynamic_window_s, "step_s": config.refresh_step_s, "alignment": "window_end"}, "series": points, "source_quality": latest_evidence.get("source_quality", {}), "spectral_evidence": latest_evidence.get("spectral_evidence", {}), "official": {"algorithm_id": config.algorithm_id}, "chart": {"kind": "metric_trend", "x_axis": {"label": "时间", "unit": "s", "field": "time_s"}, "y_axis": {"label": label, "unit": result.unit}}}}, {"metric_time_s": np.asarray(result.time_s, dtype=float), "metric_values": np.asarray(values, dtype=float)}, result.unit
