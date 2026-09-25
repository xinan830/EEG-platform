from __future__ import annotations

from collections.abc import Callable
from typing import Any

import numpy as np

from app.algorithm_runtime.contracts import (
    AlgorithmConfigBase,
    AlgorithmExecutionSnapshot,
    AlgorithmFailure,
    AlgorithmInputs,
    AlgorithmStructuredResult,
    AlgorithmStructuredSeriesResult,
    StructuredSeriesWindow,
)
from app.algorithm_runtime.parameter_schema import AlgorithmParameter, ParameterOption, ParameterSchema
from app.algorithms.spectral_snapshot import spectral_execution_snapshot
from app.algorithm_runtime.windows import build_dynamic_analysis_frames
from app.scientific.contracts.types import SampleRange
from app.scientific.quality import SpectralQualityGateError

from .config import PsdConfig
from .manifest import MANIFEST


class PsdAlgorithm:
    manifest = MANIFEST
    config_model = PsdConfig
    output_schema = MANIFEST.output_schema

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
        return spectral_execution_snapshot(config)

    def resolve_inputs(self, recording: Any, config: PsdConfig) -> AlgorithmInputs:
        labels = list(getattr(recording, "channel_names", getattr(recording, "channels", [])))
        selected = next((str(label) for label in labels if str(label).casefold() == config.channel.casefold()), None)
        if selected is None:
            raise ValueError(f"未知原始通道: {config.channel}")
        if config.end_s > float(recording.duration_s):
            raise ValueError("PSD 分析范围超出记录时长")
        if config.end_s - config.start_s < 4.0:
            raise ValueError("PSD 分析区间至少需要 4 秒")
        return AlgorithmInputs(
            recording_id=str(getattr(recording, "id", "unknown")), channel=selected,
            sfreq_hz=float(recording.sfreq_hz), duration_s=float(recording.duration_s), payload=recording,
        )

    @staticmethod
    def _load(recording: Any, *, channel: str, start_s: float, window_s: float) -> Any:
        loader: Callable[..., Any] = getattr(recording, "load_spectrum")
        return loader(start_s=start_s, window_s=window_s, channels=[channel])

    def execute_static(self, inputs: AlgorithmInputs, config: PsdConfig) -> AlgorithmStructuredResult:
        spectrum = self._load(
            inputs.payload, channel=inputs.channel, start_s=config.start_s,
            window_s=config.end_s - config.start_s,
        )
        requested = {"start_s": config.start_s, "end_s": config.end_s}
        source_quality = {
            "clean_segments": spectrum.clean_epochs,
            "total_segments": spectrum.total_epochs,
            "clean_ratio": spectrum.signal_quality,
            "gate_failed": spectrum.gate_failed,
            "rejected_reasons": list(spectrum.rejected_reasons),
        }
        spectral_evidence = dict(getattr(spectrum, "evidence", {}))
        evidence = {
            "source_quality": source_quality,
            "spectral_evidence": spectral_evidence,
            "calculation_trace": {"algorithm": "welch_psd"},
        }
        if spectrum.gate_failed:
            failure = AlgorithmFailure(
                code="PSD_QUALITY_GATE_FAILED",
                message="当前分析区间未通过 PSD 质量门",
                detail=source_quality,
            )
            return AlgorithmStructuredResult(
                output_kind="frequency_series", channel_order=[inputs.channel],
                axes={"frequency_hz": np.asarray([], dtype=float)}, axis_units={"frequency_hz": "Hz"},
                arrays={"psd": np.empty((0,), dtype=float)}, array_units={"psd": "V^2/Hz"},
                requested_range=requested, actual_range=requested, quality="gate_failed",
                failure=failure, evidence=evidence,
            )
        values = np.asarray(spectrum.psd[0], dtype=float)
        return AlgorithmStructuredResult(
            output_kind="frequency_series", channel_order=[inputs.channel],
            axes={"frequency_hz": np.asarray(spectrum.freqs, dtype=float)}, axis_units={"frequency_hz": "Hz"},
            arrays={"psd": values}, array_units={"psd": "V^2/Hz"},
            requested_range=requested, actual_range=requested, quality="clean", evidence=evidence,
        )

    def execute_dynamic(self, inputs: AlgorithmInputs, config: PsdConfig) -> AlgorithmStructuredSeriesResult:
        window_s = config.window_s or self.manifest.dynamic_policy.default_window_s
        step_s = config.step_s or self.manifest.dynamic_policy.refresh_step_s
        frames = build_dynamic_analysis_frames(
            config.start_s, config.end_s, duration_s=inputs.duration_s,
            window_s=window_s, step_s=step_s,
            minimum_window_s=self.manifest.dynamic_policy.minimum_window_s,
            allow_warmup=self.manifest.dynamic_policy.allow_warmup,
        )
        if not frames:
            raise ValueError("PSD 动态分析区间没有可执行窗口")

        rows: list[np.ndarray] = []
        windows: list[StructuredSeriesWindow] = []
        frequencies: np.ndarray | None = None
        for frame in frames:
            sample_range = SampleRange.from_seconds(frame.window_start_s, frame.window_end_s, inputs.sfreq_hz)
            failure = None
            quality = "clean"
            state = frame.state
            evidence: dict[str, Any] = {}
            try:
                spectrum = self._load(
                    inputs.payload, channel=inputs.channel,
                    start_s=frame.window_start_s, window_s=frame.actual_window_s,
                )
                current_frequencies = np.asarray(spectrum.freqs, dtype=float)
                if frequencies is None:
                    frequencies = current_frequencies
                elif not np.array_equal(frequencies, current_frequencies):
                    raise RuntimeError("PSD 动态窗口的频率轴不一致")
                row = np.asarray(spectrum.psd[0], dtype=float)
                evidence = {
                    "source_quality": {
                        "clean_segments": spectrum.clean_epochs,
                        "total_segments": spectrum.total_epochs,
                        "clean_ratio": spectrum.signal_quality,
                        "gate_failed": spectrum.gate_failed,
                        "rejected_reasons": list(spectrum.rejected_reasons),
                    },
                    "spectral_evidence": dict(getattr(spectrum, "evidence", {})),
                }
            except SpectralQualityGateError as exc:
                frequencies = frequencies if frequencies is not None else self._frequency_axis(inputs.sfreq_hz)
                row = np.full(frequencies.shape, np.nan, dtype=float)
                quality = "gate_failed"
                state = "Rejected"
                evidence = {"source_quality": dict(exc.quality), "spectral_evidence": dict(exc.quality.get("evidence", {}))}
                failure = AlgorithmFailure(
                    code="PSD_WINDOW_QUALITY_GATE_FAILED",
                    message="当前动态窗口未通过 PSD 质量门",
                    detail=dict(exc.quality),
                )
            except ValueError as exc:
                frequencies = frequencies if frequencies is not None else self._frequency_axis(inputs.sfreq_hz)
                row = np.full(frequencies.shape, np.nan, dtype=float)
                quality = "unavailable"
                state = "Unavailable"
                failure = AlgorithmFailure(code="PSD_WINDOW_UNAVAILABLE", message=str(exc))
            rows.append(row)
            windows.append(StructuredSeriesWindow(
                start_sample=sample_range.start_sample, end_sample=sample_range.end_sample,
                start_s=frame.window_start_s, end_s=frame.window_end_s,
                state=state, quality=quality, failure=failure, evidence=evidence,
            ))

        assert frequencies is not None
        state_values = {window.state for window in windows}
        overall = "clean" if state_values == {"Complete"} else "partial"
        return AlgorithmStructuredSeriesResult(
            output_kind="frequency_series", channel_order=[inputs.channel],
            axes={"frequency_hz": frequencies}, axis_units={"frequency_hz": "Hz"},
            arrays={"psd": np.stack(rows)}, array_units={"psd": "V^2/Hz"},
            windows=windows,
            requested_range={"start_s": config.start_s, "end_s": config.end_s},
            actual_range={"start_s": windows[0].start_s, "end_s": windows[-1].end_s},
            quality=overall,
            evidence={
                "source_quality": {}, "spectral_evidence": {},
                "calculation_trace": {"algorithm": "welch_psd", "window_s": window_s, "step_s": step_s},
            },
        )

    @staticmethod
    def _frequency_axis(sfreq_hz: float) -> np.ndarray:
        full = np.fft.rfftfreq(int(round(4.0 * sfreq_hz)), 1.0 / sfreq_hz)
        return full[(full >= 1.0) & (full <= 30.0)]
