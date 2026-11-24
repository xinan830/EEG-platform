from __future__ import annotations

from typing import Any

import numpy as np

from app.algorithm_runtime.contracts import (
    AlgorithmConfigBase,
    AlgorithmExecutionSnapshot,
    AlgorithmInputs,
    AlgorithmStructuredResult,
    AlgorithmStructuredSeriesResult,
    StructuredSeriesWindow,
)
from app.algorithm_runtime.parameter_schema import AlgorithmParameter, ParameterOption, ParameterSchema
from app.algorithms.spectral_snapshot import spectral_execution_snapshot
from app.algorithm_runtime.windows import build_dynamic_analysis_frames, controlled_frames
from app.scientific.contracts.types import SampleRange
from app.scientific.contracts.spectrogram import stft_context_s
from app.scientific.contracts.analysis import ANALYSIS_CONTRACT

from .config import StftConfig
from .frame import execute_stft_frame, frequency_axis, relative_time_axis
from .manifest import MANIFEST
from .window import StftWindowCapability


class StftAlgorithm(StftWindowCapability):
    manifest = MANIFEST
    on_demand_default_enabled = True
    config_model = StftConfig
    output_schema = MANIFEST.output_schema

    def can_reuse_offline_window(self, result: Any, *, sampling_rate_hz: float,
                                 config: dict[str, Any] | None = None) -> bool:
        """Opt in only for complete windows on the current STFT coordinate grid."""
        if result.state != "ready" or result.failure is not None or result.quality != "clean":
            return False
        provenance = result.provenance
        if provenance.get("analysis_state") != "Complete" or provenance.get("inner_time_axis") != "relative_window_time_center_s":
            return False
        if (provenance.get("preprocessing_scope") != "bounded_window_with_finite_context"
                or provenance.get("spectrogram_contract_version") != MANIFEST.scientific_version):
            return False
        if config is None:
            return False
        window_s = float(config.get("window_s") or config.get("dynamic_window_s")
                         or MANIFEST.dynamic_policy.default_window_s)
        expected_times = relative_time_axis(sampling_rate_hz, window_s)
        expected_frequencies = frequency_axis(sampling_rate_hz, float(config["low_hz"]), float(config["high_hz"]))
        times = np.asarray(result.axes.get("time_center_s", []), dtype=float)
        frequencies = np.asarray(result.axes.get("frequency_hz", []), dtype=float)
        linear = np.asarray(result.arrays.get("power_linear", []), dtype=float)
        power_db = np.asarray(result.arrays.get("power_db", []), dtype=float)
        return (
            np.array_equal(times, expected_times)
            and np.array_equal(frequencies, expected_frequencies)
            and linear.shape == (expected_times.size, expected_frequencies.size)
            and power_db.shape == linear.shape
            and np.isfinite(linear).all() and np.isfinite(power_db).all()
            and np.all(linear >= 0)
        )

    def parameter_schema(self) -> ParameterSchema:
        return ParameterSchema(parameters=[
            AlgorithmParameter(key="channel", label_zh="分析通道", value_type="string", description_zh="从原始记录通道中选择一个通道。"),
            AlgorithmParameter(key="mode", label_zh="分析模式", value_type="enum", options=[ParameterOption(value="static", label_zh="静态"), ParameterOption(value="dynamic", label_zh="动态")]),
            AlgorithmParameter(key="start_s", label_zh="分析开始", value_type="number", unit="s", minimum=0, step=0.001),
            AlgorithmParameter(key="end_s", label_zh="分析结束", value_type="number", unit="s", minimum=0, step=0.001),
            AlgorithmParameter(key="window_s", label_zh="动态窗口", value_type="number", unit="s", minimum=4, step=1),
            AlgorithmParameter(key="step_s", label_zh="动态步长", value_type="number", unit="s", minimum=1, step=1),
        ])

    def requested_channels(self, config: AlgorithmConfigBase) -> list[str]:
        return [config.channel]

    def execution_snapshot(self, config: AlgorithmConfigBase) -> AlgorithmExecutionSnapshot:
        snapshot = spectral_execution_snapshot(config, low_hz=ANALYSIS_CONTRACT["bandpass_hz"][0],
                                               high_hz=ANALYSIS_CONTRACT["bandpass_hz"][1], notch_hz=config.notch_hz)
        snapshot.window.update({"segment_s": 4.0, "step_s": 1.0, "time_axis": "window_center"})
        snapshot.filters.update({
            "preprocessing_scope": "bounded_window_with_finite_context",
            "context_s": stft_context_s(float(ANALYSIS_CONTRACT["bandpass_hz"][0])),
            "boundary_policy": "clamp_to_recording",
        })
        snapshot.quality_rules["spectrogram_contract_version"] = MANIFEST.scientific_version
        snapshot.quality_rules["frequency_range_hz"] = [float(config.low_hz), float(config.high_hz)]
        snapshot.quality_rules["notch_hz"] = config.notch_hz
        return snapshot

    def resolve_inputs(self, recording: Any, config: StftConfig) -> AlgorithmInputs:
        labels = list(getattr(recording, "channel_names", getattr(recording, "channels", [])))
        selected = next((str(label) for label in labels if str(label).casefold() == config.channel.casefold()), None)
        if selected is None:
            raise ValueError(f"未知原始通道: {config.channel}")
        if config.end_s > float(recording.duration_s):
            raise ValueError("STFT 分析范围超出记录时长")
        if config.end_s - config.start_s < 4.0:
            raise ValueError("STFT 分析区间至少需要 4 秒")
        return AlgorithmInputs(
            recording_id=str(getattr(recording, "id", "unknown")), channel=selected,
            sfreq_hz=float(recording.sfreq_hz), duration_s=float(recording.duration_s), payload=recording,
        )

    def execute_static(self, inputs: AlgorithmInputs, config: StftConfig) -> AlgorithmStructuredResult:
        frame = execute_stft_frame(inputs, config, config.start_s, config.end_s)
        return AlgorithmStructuredResult(
            output_kind="time_frequency", channel_order=[inputs.channel],
            axes={"time_center_s": frame.times_s, "frequency_hz": frame.frequencies_hz},
            axis_units={"time_center_s": "s", "frequency_hz": "Hz"},
            arrays=frame.arrays,
            array_units={"power_linear": "V^2/Hz", "power_db": "dB re 1 uV^2/Hz"},
            requested_range={"start_s": config.start_s, "end_s": config.end_s},
            actual_range=frame.actual_range,
            quality=frame.quality, failure=frame.failure, evidence=frame.evidence,
        )

    def execute_dynamic(self, inputs: AlgorithmInputs, config: StftConfig) -> AlgorithmStructuredSeriesResult:
        window_s = config.window_s or self.manifest.dynamic_policy.default_window_s
        step_s = config.step_s or self.manifest.dynamic_policy.refresh_step_s
        frames = build_dynamic_analysis_frames(
            config.start_s, config.end_s, duration_s=inputs.duration_s,
            window_s=window_s, step_s=step_s,
            minimum_window_s=self.manifest.dynamic_policy.minimum_window_s,
            allow_warmup=self.manifest.dynamic_policy.allow_warmup,
        )
        if not frames:
            raise ValueError("STFT 动态分析区间没有可执行窗口")
        inner_times = relative_time_axis(inputs.sfreq_hz, window_s)
        frequencies = frequency_axis(inputs.sfreq_hz, config.low_hz, config.high_hz)
        rows: dict[str, list[np.ndarray]] = {"power_linear": [], "power_db": []}
        windows: list[StructuredSeriesWindow] = []
        context = inputs.execution_context
        for planned in controlled_frames(frames, context):
            sample_range = SampleRange.from_seconds(planned.window_start_s, planned.window_end_s, inputs.sfreq_hz)
            cached = context.cached_window(sample_range.start_sample, sample_range.end_sample) if (
                context is not None and hasattr(context, "cached_window")
            ) else None
            if cached is not None and self.can_reuse_offline_window(
                cached, sampling_rate_hz=inputs.sfreq_hz, config=config.model_dump(),
            ) and planned.state == "Complete":
                arrays = cached.arrays
                quality, failure, state = cached.quality, None, planned.state
                evidence, provenance = dict(cached.evidence), dict(cached.provenance)
            else:
                frame = execute_stft_frame(inputs, config, planned.window_start_s, planned.window_end_s)
                failure = frame.window_failure
                arrays = frame.arrays if failure is None else {}
                state = planned.state if failure is None else (
                    "Unavailable" if frame.window_state == "unavailable" else "Rejected"
                )
                quality = "clean" if failure is None else (
                    "unavailable" if state == "Unavailable" else "gate_failed"
                )
                evidence, provenance = frame.evidence, frame.provenance
            if context is not None:
                context.check_cancelled()
            for name in rows:
                padded = np.full((inner_times.size, frequencies.size), np.nan)
                if name in arrays:
                    values = np.asarray(arrays[name], dtype=float)
                    padded[:len(values)] = values
                rows[name].append(padded)
            windows.append(StructuredSeriesWindow(
                start_sample=sample_range.start_sample, end_sample=sample_range.end_sample,
                start_s=planned.window_start_s, end_s=planned.window_end_s,
                state=state, quality=quality, failure=failure, evidence=evidence, provenance=provenance,
            ))
        return AlgorithmStructuredSeriesResult(
            output_kind="time_frequency", channel_order=[inputs.channel],
            axes={"time_center_s": inner_times, "frequency_hz": frequencies},
            axis_units={"time_center_s": "s", "frequency_hz": "Hz"},
            arrays={name: np.stack(values) for name, values in rows.items()},
            array_units={"power_linear": "V^2/Hz", "power_db": "dB re 1 uV^2/Hz"},
            windows=windows,
            requested_range={"start_s": config.start_s, "end_s": config.end_s},
            actual_range={"start_s": windows[0].start_s, "end_s": windows[-1].end_s},
            quality=("gate_failed" if all(window.quality == "gate_failed" for window in windows)
                     else "partial" if any(window.quality != "clean" for window in windows) else "clean"),
            failure=(next((window.failure for window in windows if window.failure is not None), None)
                     if all(window.quality == "gate_failed" for window in windows) else None),
            evidence={"source_quality": {}, "spectral_evidence": {}, "calculation_trace": {
                "spectrogram_contract_version": MANIFEST.scientific_version, "window_s": window_s,
                "step_s": step_s, "inner_time_padding": "nan_unavailable",
            }},
        )
