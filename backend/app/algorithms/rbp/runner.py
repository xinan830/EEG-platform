from __future__ import annotations

from typing import Any

import numpy as np

from app.algorithm_runtime.contracts import (AlgorithmConfigBase, AlgorithmExecutionSnapshot,
    AlgorithmFailure, AlgorithmInputs, AlgorithmStructuredResult,
    AlgorithmStructuredSeriesResult, StructuredSeriesWindow)
from app.algorithm_runtime.parameter_schema import AlgorithmParameter, ParameterOption, ParameterSchema
from app.algorithms.spectral_snapshot import spectral_execution_snapshot
from .official import RBP_BANDS, RBP_FILTER_LOW_HZ, RBP_FILTER_HIGH_HZ, RBP_NOTCH_HZ, five_band_shares
from app.algorithm_runtime.windows import build_dynamic_analysis_frames, controlled_frames
from app.scientific.quality import SpectralQualityGateError

from .config import RbpConfig
from .manifest import MANIFEST
from .window import RbpWindowCapability


class RbpAlgorithm(RbpWindowCapability):
    manifest = MANIFEST
    on_demand_default_enabled = True
    config_model = RbpConfig

    def parameter_schema(self) -> ParameterSchema:
        return ParameterSchema(parameters=[
            AlgorithmParameter(key="channel", label_zh="分析通道", value_type="string", description_zh="从原始记录通道中选择一个通道。"),
            AlgorithmParameter(key="mode", label_zh="分析模式", value_type="enum", options=[ParameterOption(value="static", label_zh="静态"), ParameterOption(value="dynamic", label_zh="动态")]),
            AlgorithmParameter(key="start_s", label_zh="分析开始", value_type="number", unit="s", minimum=0, step=0.001),
            AlgorithmParameter(key="end_s", label_zh="分析结束", value_type="number", unit="s", minimum=0, step=0.001),
        ])

    def requested_channels(self, config: AlgorithmConfigBase) -> list[str]:
        return list(getattr(config, "channels", None) or [config.channel])

    def execution_snapshot(self, config: AlgorithmConfigBase) -> AlgorithmExecutionSnapshot:
        snapshot = spectral_execution_snapshot(config)
        return snapshot.model_copy(update={"filters": snapshot.filters | {
            "bandpass_hz": [RBP_FILTER_LOW_HZ, RBP_FILTER_HIGH_HZ],
            "notch_hz": RBP_NOTCH_HZ, "notch_quality_factor": 30.0,
        }})

    def resolve_inputs(self, recording: Any, config: RbpConfig) -> AlgorithmInputs:
        labels = list(getattr(recording, "channel_names", getattr(recording, "channels", [])))
        channel = next((str(item) for item in labels if str(item).casefold() == config.channel.casefold()), None)
        if channel is None:
            raise ValueError(f"未知原始通道: {config.channel}")
        if float(getattr(recording, "sfreq_hz", 0.0)) / 2.0 <= RBP_FILTER_HIGH_HZ:
            raise ValueError("RBP 需要采样率高于 100 Hz，才能完整分析 1–50 Hz")
        selected = list(config.channels or [channel])
        resolved = []
        for requested in selected:
            match = next((str(item) for item in labels if str(item).casefold() == requested.casefold()), None)
            if match is None:
                raise ValueError(f"未知原始通道: {requested}")
            resolved.append(match)
        return AlgorithmInputs(recording_id=str(getattr(recording, "id", "unknown")),
            channel=resolved[0], channels=tuple(resolved), sfreq_hz=float(getattr(recording, "sfreq_hz", 1.0)),
            duration_s=float(getattr(recording, "duration_s", config.end_s)), payload=recording)

    @staticmethod
    def _load_spectrum(payload: Any, *, start_s: float, window_s: float, channel: str) -> Any:
        return payload.load_spectrum(
            start_s=start_s, window_s=window_s, channels=[channel],
            filter_low_hz=RBP_FILTER_LOW_HZ, filter_high_hz=RBP_FILTER_HIGH_HZ,
            output_low_hz=RBP_FILTER_LOW_HZ, output_high_hz=RBP_FILTER_HIGH_HZ,
            notch_hz=RBP_NOTCH_HZ,
        )

    def execute_static(self, inputs: AlgorithmInputs, config: RbpConfig) -> AlgorithmStructuredResult:
        return self._execute_static_multi(inputs, config)

    def execute_dynamic(self, inputs: AlgorithmInputs, config: RbpConfig) -> AlgorithmStructuredSeriesResult:
        return self._execute_dynamic_multi(inputs, config)

    def _execute_static_multi(self, inputs: AlgorithmInputs, config: RbpConfig) -> AlgorithmStructuredResult:
        duration = config.end_s - config.start_s
        try:
            spectrum = inputs.payload.load_spectrum(
                start_s=config.start_s, window_s=duration, channels=list(inputs.channels),
                filter_low_hz=RBP_FILTER_LOW_HZ, filter_high_hz=RBP_FILTER_HIGH_HZ,
                output_low_hz=RBP_FILTER_LOW_HZ, output_high_hz=RBP_FILTER_HIGH_HZ, notch_hz=RBP_NOTCH_HZ,
            )
        except SpectralQualityGateError as exc:
            gate_quality = exc.quality
            spectrum = None
        rows = {name: [] for name, *_ in RBP_BANDS}
        failures = {}
        for index, channel in enumerate(inputs.channels):
            if spectrum is None or spectrum.gate_failed:
                failures[channel] = {"code": "PSD_QUALITY_GATE_FAILED", "message": "当前窗口未通过 PSD 质量门",
                                     "details": gate_quality if spectrum is None else {"reason": spectrum.gate_failed}}
                for name in rows: rows[name].append(np.nan)
                continue
            try:
                powers, shares = five_band_shares(type("ChannelSpectrum", (), {
                    "freqs": spectrum.freqs, "psd": spectrum.psd[index:index + 1],
                })())
                for name in rows: rows[name].append(shares[name])
            except ValueError as exc:
                failures[channel] = {"code": "RBP_DENOMINATOR_INVALID", "message": str(exc)}
                for name in rows: rows[name].append(np.nan)
        quality = "gate_failed" if failures and len(failures) == len(inputs.channels) else "partial" if failures else "clean"
        failure = AlgorithmFailure(code="RBP_ALL_CHANNELS_UNAVAILABLE", message="所有选定导联均不可用") if failures and len(failures) == len(inputs.channels) else None
        return AlgorithmStructuredResult(
            output_kind="algorithm_group", channel_order=list(inputs.channels),
            axes={"time_s": np.asarray([config.end_s], dtype=float),
                  "channel_index": np.arange(len(inputs.channels), dtype=float)},
            axis_units={"time_s": "s", "channel_index": "index"},
            arrays={name: np.asarray(values, dtype=float) for name, values in rows.items()},
            array_units={name: "ratio" for name, *_ in RBP_BANDS},
            requested_range={"start_s": config.start_s, "end_s": config.end_s},
            actual_range={"start_s": config.start_s, "end_s": config.end_s}, quality=quality, failure=failure,
            evidence={"extensions": {"channel_failures": {"by_channel": failures}, "channel_order": {"channels": list(inputs.channels)}},
                      "calculation_trace": {"bands": [name for name, *_ in RBP_BANDS],
                      "window_s": duration, "montage_channel_order": list(inputs.channels)}},
        )

    def _execute_dynamic_multi(self, inputs: AlgorithmInputs, config: RbpConfig) -> AlgorithmStructuredSeriesResult:
        window_s = config.window_s or self.manifest.dynamic_policy.default_window_s
        step_s = config.step_s or self.manifest.dynamic_policy.refresh_step_s
        frames = build_dynamic_analysis_frames(config.start_s, config.end_s, duration_s=inputs.duration_s,
            window_s=window_s, step_s=step_s, minimum_window_s=self.manifest.dynamic_policy.minimum_window_s,
            allow_warmup=self.manifest.dynamic_policy.allow_warmup)
        windows = []
        per_window_failures = []
        values = {name: [] for name, *_ in RBP_BANDS}
        for frame in controlled_frames(frames, inputs.execution_context):
            result, failures = self._execute_multi_frame(inputs, frame.window_start_s, frame.window_end_s)
            for name, *_ in RBP_BANDS: values[name].append(result[name])
            per_window_failures.append(failures)
            all_failed = len(failures) == len(inputs.channels)
            windows.append(StructuredSeriesWindow(start_sample=int(frame.window_start_s * inputs.sfreq_hz),
                end_sample=int(frame.window_end_s * inputs.sfreq_hz), start_s=frame.window_start_s,
                end_s=frame.window_end_s,
                state="Rejected" if all_failed and any(item.get("code") == "PSD_QUALITY_GATE_FAILED" for item in failures.values()) else "Unavailable" if all_failed else frame.state,
                quality="gate_failed" if all_failed and any(item.get("code") == "PSD_QUALITY_GATE_FAILED" for item in failures.values()) else "unavailable" if all_failed else "partial" if failures else "clean",
                failure=AlgorithmFailure(code="RBP_ALL_CHANNELS_UNAVAILABLE", message="所有选定导联均不可用", detail={"channels": failures}) if all_failed else None,
                evidence={"extensions": {"channel_failures": {"by_channel": failures}}} if failures else {}, provenance={"preprocessing_scope": "whole_recording_before_window_slice"}))
        return AlgorithmStructuredSeriesResult(output_kind="algorithm_group", channel_order=list(inputs.channels),
            axes={"time_s": np.asarray([frame.time_s for frame in frames], dtype=float),
                  "channel_index": np.arange(len(inputs.channels), dtype=float)},
            axis_units={"time_s": "s", "channel_index": "index"},
            arrays={name: np.asarray(rows, dtype=float) for name, rows in values.items()},
            array_units={name: "ratio" for name, *_ in RBP_BANDS}, windows=windows,
            requested_range={"start_s": config.start_s, "end_s": config.end_s}, quality="clean",
            evidence={"extensions": {"channel_order": {"channels": list(inputs.channels)}, "window_channel_failures": {"by_window": per_window_failures}},
                      "calculation_trace": {"bands": [name for name, *_ in RBP_BANDS], "window_s": window_s, "step_s": step_s}})

    def _execute_multi_frame(self, inputs: AlgorithmInputs, start_s: float, end_s: float) -> tuple[dict[str, np.ndarray], dict[str, dict[str, str]]]:
        try:
            spectrum = inputs.payload.load_spectrum(start_s=start_s, window_s=end_s - start_s,
                channels=list(inputs.channels), filter_low_hz=RBP_FILTER_LOW_HZ, filter_high_hz=RBP_FILTER_HIGH_HZ,
                output_low_hz=RBP_FILTER_LOW_HZ, output_high_hz=RBP_FILTER_HIGH_HZ, notch_hz=RBP_NOTCH_HZ)
        except SpectralQualityGateError as exc:
            return ({name: np.full(len(inputs.channels), np.nan) for name, *_ in RBP_BANDS},
                    {channel: {"code": "PSD_QUALITY_GATE_FAILED", "message": "当前窗口未通过 PSD 质量门", "details": exc.quality}
                     for channel in inputs.channels})
        rows = {name: [] for name, *_ in RBP_BANDS}
        failures = {}
        for index in range(len(inputs.channels)):
            if spectrum.gate_failed:
                failures[inputs.channels[index]] = {"code": "PSD_QUALITY_GATE_FAILED", "message": "当前窗口未通过 PSD 质量门"}
                shares = {name: np.nan for name, *_ in RBP_BANDS}
                for name in rows: rows[name].append(shares[name])
                continue
            try:
                _, shares = five_band_shares(type("ChannelSpectrum", (), {"freqs": spectrum.freqs, "psd": spectrum.psd[index:index + 1]})())
            except ValueError as exc:
                failures[inputs.channels[index]] = {"code": "RBP_CHANNEL_UNAVAILABLE", "message": str(exc)}
                shares = {name: np.nan for name, *_ in RBP_BANDS}
            for name in rows: rows[name].append(shares[name])
        return {name: np.asarray(values, dtype=float) for name, values in rows.items()}, failures
