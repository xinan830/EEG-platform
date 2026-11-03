from __future__ import annotations

from typing import Any

import numpy as np

from app.algorithm_runtime.contracts import (AlgorithmConfigBase, AlgorithmExecutionSnapshot,
    AlgorithmInputs, AlgorithmStructuredResult,
    AlgorithmStructuredSeriesResult, StructuredSeriesWindow)
from app.algorithm_runtime.parameter_schema import AlgorithmParameter, ParameterOption, ParameterSchema
from app.algorithms.spectral_snapshot import spectral_execution_snapshot
from .official import RBP_BANDS, RBP_FILTER_LOW_HZ, RBP_FILTER_HIGH_HZ, RBP_NOTCH_HZ
from app.algorithm_runtime.windows import build_dynamic_analysis_frames, controlled_frames

from .config import RbpConfig
from .frame import execute_rbp_frame
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
        return snapshot.model_copy(update={"quality_rules": {
            "policy": "per_channel_annotation", "artifact_policy": "retain_with_warning",
            "artifact_peak_uv": snapshot.quality_rules["artifact_peak_uv"],
            "hard_rejections": ["missing_samples", "non_finite", "invalid_total_power"],
        }, "filters": snapshot.filters | {
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

    def execute_static(self, inputs: AlgorithmInputs, config: RbpConfig) -> AlgorithmStructuredResult:
        frame = execute_rbp_frame(inputs, config.start_s, config.end_s)
        return AlgorithmStructuredResult(
            output_kind="algorithm_group", channel_order=list(inputs.channels),
            axes={"time_s": np.asarray([config.end_s]),
                  "channel_index": np.arange(len(inputs.channels), dtype=float)},
            axis_units={"time_s": "s", "channel_index": "index"},
            arrays=frame.arrays, array_units=_rbp_array_units(),
            requested_range={"start_s": config.start_s, "end_s": config.end_s},
            actual_range={"start_s": config.start_s, "end_s": config.end_s},
            quality=frame.quality, failure=frame.failure, evidence=frame.evidence,
        )

    def execute_dynamic(self, inputs: AlgorithmInputs, config: RbpConfig) -> AlgorithmStructuredSeriesResult:
        window_s = config.window_s or self.manifest.dynamic_policy.default_window_s
        step_s = config.step_s or self.manifest.dynamic_policy.refresh_step_s
        frames = build_dynamic_analysis_frames(
            config.start_s, config.end_s, duration_s=inputs.duration_s,
            window_s=window_s, step_s=step_s,
            minimum_window_s=self.manifest.dynamic_policy.minimum_window_s,
            allow_warmup=self.manifest.dynamic_policy.allow_warmup,
        )
        windows: list[StructuredSeriesWindow] = []
        per_window_failures = []
        values = {name: [] for name in _rbp_array_units()}
        for frame in controlled_frames(frames, inputs.execution_context):
            result = execute_rbp_frame(inputs, frame.window_start_s, frame.window_end_s)
            for name in values:
                values[name].append(result.arrays[name])
            per_window_failures.append(result.evidence["extensions"]["channel_failures"]["by_channel"])
            windows.append(StructuredSeriesWindow(
                start_sample=int(round(frame.window_start_s * inputs.sfreq_hz)),
                end_sample=int(round(frame.window_end_s * inputs.sfreq_hz)),
                start_s=frame.window_start_s, end_s=frame.window_end_s,
                state={"rejected": "Rejected", "unavailable": "Unavailable"}.get(result.state, frame.state),
                quality=result.quality, failure=result.failure,
                evidence=result.evidence, provenance=result.provenance,
            ))
        return AlgorithmStructuredSeriesResult(
            output_kind="algorithm_group", channel_order=list(inputs.channels),
            axes={"time_s": np.asarray([frame.time_s for frame in frames], dtype=float),
                  "channel_index": np.arange(len(inputs.channels), dtype=float)},
            axis_units={"time_s": "s", "channel_index": "index"},
            arrays={name: np.asarray(rows, dtype=float) for name, rows in values.items()},
            array_units=_rbp_array_units(), windows=windows,
            requested_range={"start_s": config.start_s, "end_s": config.end_s},
            actual_range={"start_s": frames[0].window_start_s, "end_s": frames[-1].window_end_s} if frames else None,
            quality="clean" if windows and all(window.quality == "clean" for window in windows) else "partial",
            evidence={"extensions": {
                "channel_order": {"channels": list(inputs.channels)},
                "window_channel_failures": {"by_window": per_window_failures},
            }, "calculation_trace": {
                "bands": [name for name, *_ in RBP_BANDS], "window_s": window_s, "step_s": step_s,
            }},
        )


def _rbp_array_units() -> dict[str, str]:
    return {output: "ratio" if output == band else "uV^2"
            for band, *_ in RBP_BANDS for output in (band, f"{band}_absolute_power")}
