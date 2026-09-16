"""Numerical execution for persisted analysis runs.

Run lifecycle, caching, persistence, and artifacts remain in ``RunService``.
This class deliberately owns only the pre-existing analysis dispatch paths.
"""

from __future__ import annotations

from typing import Any

import numpy as np

from app.eeg_core.definition_engine import execute_graph
from app.eeg_core.primitives.types import Scalar
from app.eeg_core.quality import SpectralQualityGateError
from app.eeg_core.spectral import SpectralEstimate
from app.algorithm_runtime.contracts import AlgorithmResult, AlgorithmSeriesResult
from app.algorithm_runtime.executor import AlgorithmRuntime
from app.algorithm_runtime.registry import AlgorithmRegistry
from app.models.analysis_config import AnalysisConfigRequest
from app.models.definition_metric_run import DefinitionMetricConfig
from app.models.official_algorithm_run import OfficialAlgorithmRunConfig
from app.processing.offline_analysis import analyze_recording


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
        return SpectralEstimate(
            np.asarray(payload["frequencies_hz"], dtype=float),
            np.asarray([payload["psd"][name] for name in ordered], dtype=float) * 1e-12,
            float(payload["quality"]["clean_ratio"]),
            int(payload["quality"]["clean_segments"]),
            int(payload["quality"]["total_segments"]),
            payload["quality"]["gate_failed"],
            tuple(payload["quality"].get("rejected_reasons", [])),
        )


def _serialize_algorithm_result(result: AlgorithmResult, algorithm_id: str, label: str) -> dict[str, object]:
    quality = {"status": result.quality, "reasons": [result.failure.code] if result.failure else []}
    return {
        "output": {"id": algorithm_id, "label": label, "value": result.value, "unit": result.unit, "quality": quality},
        "channel": result.channel,
        "actual_range": result.actual_range,
        "requested_range": result.requested_range,
        "source_quality": result.evidence.get("source_quality", {}),
        "official": {"algorithm_id": algorithm_id, **result.evidence},
        "chart": {"kind": "none"},
        "value": result.value,
        "quality": quality,
        "failure": result.failure.model_dump(mode="json") if result.failure else None,
    }


class RunAnalysisExecutor:
    def __init__(self, recordings: Any, definition_service: Any, metric_runner: Any, algorithm_registry: AlgorithmRegistry | None = None):
        self.recordings = recordings
        self.definition_service = definition_service
        self.metric_runner = metric_runner
        self.algorithm_runtime = AlgorithmRuntime(algorithm_registry) if algorithm_registry is not None else None

    def execute(self, analysis_type: str, recording: Any, resolved: dict[str, Any]):
        if analysis_type == "legacy_analysis":
            if recording.mapping is None:
                raise ValueError("saved semantic channel mapping is required")
            data, sfreq, channel_names, events = self.recordings.load_data(recording)
            result = analyze_recording(data, sfreq, recording.mapping, channel_names, events).to_dict()
            waveform = result.pop("waveform")
            metrics = result.pop("metrics")
            arrays: dict[str, np.ndarray] = {
                "waveform_elapsed_s": np.asarray(waveform["elapsed_s"], dtype=float),
            }
            for index, name in enumerate(waveform["channels"]):
                arrays[f"waveform_channel_{index}"] = np.asarray(waveform["channels"][name], dtype=float)
            arrays["metric_elapsed_s"] = np.asarray([item["elapsed_s"] for item in metrics], dtype=float)
            result.update({
                "metric_count": len(metrics),
                "waveform_channels": list(waveform["channels"]),
                "waveform_unit": waveform["unit"],
            })
            return result, arrays, "mixed; see result_summary"

        if analysis_type == "definition_metric":
            return self._execute_definition_metric(recording, resolved)

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

    def _official_spectrum(self, recording: Any, channels: list[str], start_s: float, end_s: float) -> tuple[SpectralEstimate, dict[str, object]]:
        payload = self.recordings.load_spectrum(recording, start_s, end_s - start_s, channels)
        ordered = list(payload["channels"])
        psd_uv = np.asarray([payload["psd"][name] for name in ordered], dtype=float)
        estimate = SpectralEstimate(
            np.asarray(payload["frequencies_hz"], dtype=float), psd_uv * 1e-12,
            float(payload["quality"]["clean_ratio"]), int(payload["quality"]["clean_segments"]),
            int(payload["quality"]["total_segments"]), payload["quality"]["gate_failed"],
            tuple(payload["quality"].get("rejected_reasons", [])),
        )
        evidence = {
            "sfreq_hz": float(payload["sfreq_hz"]), "analysis_reference": payload["analysis_reference"],
            "algorithm_version": payload["algorithm_version"], "filter_contract": payload["filter_contract"],
            "welch_contract": payload["welch_contract"], "units": payload["units"],
            "frequencies_hz": list(payload["frequencies_hz"]), "channels": ordered,
            "psd_uV2_per_hz": list(payload["psd"][ordered[0]]),
            "psd_uV2_per_hz_by_channel": {name: list(payload["psd"][name]) for name in ordered},
            "band_power": {name: dict(payload["band_power"][name]) for name in ordered},
            "relative_band_power": {name: dict(payload["relative_band_power"][name]) for name in ordered},
            "quality": dict(payload["quality"]),
        }
        return estimate, evidence

    def _execute_official_algorithm(self, recording: Any, resolved: dict[str, Any]):
        config = OfficialAlgorithmRunConfig.model_validate(resolved["config"])
        if self.algorithm_runtime is None:
            raise ValueError("algorithm runtime is not configured")
        context = _RecordingAlgorithmContext(self.recordings, recording)
        runtime_config = {
            "channel": config.channel, "mode": config.mode,
            "start_s": float(config.time.start_s), "end_s": float(config.time.end_s),
            "window_s": float(config.dynamic_window_s), "step_s": float(config.refresh_step_s),
        }
        result = self.algorithm_runtime.execute(
            algorithm_id=config.algorithm_id, recording=context, config=runtime_config,
        )
        module = self.algorithm_runtime.registry.get(config.algorithm_id)
        label = module.manifest.display_name_zh
        if isinstance(result, AlgorithmResult):
            point = _serialize_algorithm_result(result, config.algorithm_id, label)
            return {"metric": point}, {"metric_value": np.asarray([np.nan if result.value is None else result.value], dtype=float)}, result.unit
        points = []
        values = []
        for index, (value, center, window, quality, failure) in enumerate(zip(result.values, result.time_centers_s, result.windows, result.quality, result.failures)):
            point = {"time_s": center, "window_start_s": window["start_s"], "window_end_s": window["end_s"], "value": value,
                     "quality": {"status": quality, "reasons": [failure.code] if failure else []},
                     "output": {"id": config.algorithm_id, "label": label, "value": value, "unit": result.unit, "quality": {"status": quality, "reasons": [failure.code] if failure else []}},
                     "channel": result.channel, "official": {"algorithm_id": config.algorithm_id}, "chart": {"kind": "none"}}
            points.append(point)
            values.append(np.nan if value is None else float(value))
        first = points[0] if points else {"output": {"id": config.algorithm_id, "label": label, "unit": result.unit}, "channel": result.channel}
        return {"metric": {"mode": "dynamic", "output": first["output"], "channel": result.channel, "actual_range": {"start_s": config.time.start_s, "end_s": config.time.end_s}, "dynamic_contract": {"window_s": config.dynamic_window_s, "step_s": config.refresh_step_s, "alignment": "window_center"}, "series": points, "official": {"algorithm_id": config.algorithm_id}, "chart": {"kind": "metric_trend", "x_axis": {"label": "时间", "unit": "s", "field": "time_s"}, "y_axis": {"label": label, "unit": result.unit}}}}, {"metric_time_s": np.asarray(result.time_centers_s, dtype=float), "metric_values": np.asarray(values, dtype=float)}, result.unit

    def _execute_definition_metric(self, recording: Any, resolved: dict[str, Any]):
        config = DefinitionMetricConfig.model_validate(resolved["config"])
        definition_id = str(resolved["definition_id"])
        definition_version = str(resolved["definition_version"])
        version = self.definition_service.repository.get_version(definition_id, definition_version)
        if version is None:
            raise ValueError("definition metric version does not exist")
        if config.mode == "dynamic":
            return self._execute_dynamic_definition_metric(recording, version, config)
        resolution = self.metric_runner.resolve_inputs(recording, version, config)
        output_id, output = self._execute_metric_graph(version, resolution.inputs)
        if output.value is None:
            raise SpectralQualityGateError({**resolution.quality, "metric_output": None,
                                            "metric_rejected_reasons": list(output.quality.reasons)})
        output_metadata = version.outputs.get(output_id, {})
        output_label = output_metadata.get("label", output_id) if isinstance(output_metadata, dict) else output_id
        input_items = [{"key": key, "label": key, **value} for key, value in resolution.snapshot.items()]
        input_units = {str(item["unit"]) for item in input_items if item.get("value") is not None}
        chart = {
            "kind": "input_comparison" if len(input_items) >= 2 and len(input_units) == 1 else "none",
            "x_axis": {"field": "input_label"},
            "y_axis": {"unit": next(iter(input_units), None)},
            "values": input_items if len(input_units) == 1 else [],
        }
        metric = {
            "output": {"id": output_id, "label": output_label, **self._scalar_output(output)},
            "inputs": resolution.snapshot, "channel": config.channel, "actual_range": resolution.actual_range,
            "source_quality": resolution.quality, "spectral_evidence": resolution.spectral_evidence, "chart": chart,
        }
        arrays = {"metric_value": np.asarray([output.value], dtype=float)}
        arrays.update({f"input_{index}_value": np.asarray([value["value"]], dtype=float) for index, value in enumerate(resolution.snapshot.values())})
        return {"metric": metric}, arrays, output.unit.value

    @staticmethod
    def _execute_metric_graph(version: Any, inputs: dict[str, Scalar]) -> tuple[str, Scalar]:
        outputs = execute_graph(version.graph, inputs)
        if len(outputs) != 1:
            raise ValueError("definition metric requires exactly one scalar output")
        output_id, output = next(iter(outputs.items()))
        if not isinstance(output, Scalar):
            raise ValueError("definition metric output must be a scalar")
        return output_id, output

    @staticmethod
    def _scalar_output(value: Any) -> dict[str, object]:
        return {
            "value": value.value, "unit": value.unit.value,
            "quality": {"status": value.quality.status, "reasons": list(value.quality.reasons), "rejected_reasons": list(value.quality.rejected_reasons)},
            "provenance": [{"node": item.node, "parameters": dict(item.parameters)} for item in value.provenance],
        }

    def _execute_dynamic_definition_metric(self, recording: Any, version: Any, config: DefinitionMetricConfig):
        output_id: str | None = None
        output_label: str | None = None
        output_unit: str | None = None
        points: list[dict[str, object]] = []
        values: list[float] = []
        start, end = float(config.time.start_s), float(config.time.end_s)
        window, step = float(config.dynamic_window_s), float(config.refresh_step_s)
        warmup = end - start < window
        point_end = end if warmup else start + window
        while point_end <= end + 1e-9:
            point_start = start if warmup else point_end - window
            try:
                resolution = self.metric_runner.resolve_window(recording, version, config.channel, point_start, point_end)
                current_output_id, output = self._execute_metric_graph(version, resolution.inputs)
                output_id = output_id or current_output_id
                metadata = version.outputs.get(current_output_id, {})
                output_label = output_label or (metadata.get("label", current_output_id) if isinstance(metadata, dict) else current_output_id)
                output_unit = output_unit or output.unit.value
                value = float(output.value) if output.value is not None else None
                quality = self._scalar_output(output)["quality"]
                if value is None:
                    quality = {**quality, "status": "bad"}
                point = {"time_s": round(point_end, 9), "window_start_s": round(point_start, 9), "window_end_s": round(point_end, 9), "value": value, "quality": quality, "inputs": resolution.snapshot, "source_quality": resolution.quality, "spectral_evidence": resolution.spectral_evidence}
                if warmup:
                    point["warmup"] = True
                points.append(point)
                values.append(np.nan if value is None else value)
            except SpectralQualityGateError as exc:
                point = {"time_s": round(point_end, 9), "window_start_s": round(point_start, 9), "window_end_s": round(point_end, 9), "value": None, "quality": {"status": "bad", "reasons": list(exc.quality.get("reasons", [])), "source_quality": exc.quality}}
                if warmup:
                    point["warmup"] = True
                points.append(point)
                values.append(np.nan)
            point_end += step
        if not points:
            raise ValueError("dynamic metric analysis produced no windows")
        if output_id is None or output_unit is None:
            raise SpectralQualityGateError({"dynamic_metric": "no_clean_windows"})
        return {
            "metric": {"mode": "dynamic", "output": {"id": output_id, "label": output_label or output_id, "unit": output_unit}, "channel": config.channel, "actual_range": {"start_s": start, "end_s": end}, "dynamic_contract": {"window_s": config.dynamic_window_s, "step_s": config.refresh_step_s, "alignment": "window_end"}, "series": points, "chart": {"kind": "metric_trend", "x_axis": {"label": "时间", "unit": "s", "field": "window_end_s"}, "y_axis": {"label": output_label or output_id, "unit": output_unit}}}
        }, {"metric_time_s": np.asarray([point["time_s"] for point in points], dtype=float), "metric_values": np.asarray(values, dtype=float)}, output_unit
