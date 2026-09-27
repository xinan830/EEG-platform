from __future__ import annotations

from collections.abc import Callable
from typing import Any

from app.algorithm_runtime.contracts import AlgorithmConfigBase, AlgorithmExecutionSnapshot, AlgorithmInputs, AlgorithmFailure, AlgorithmResult, AlgorithmSeriesResult
from app.algorithm_runtime.parameter_schema import AlgorithmParameter, ParameterOption, ParameterSchema
from app.algorithm_runtime.windows import build_dynamic_analysis_frames
from app.scientific.quality import SpectralQualityGateError
from app.algorithms.spectral_snapshot import spectral_execution_snapshot

from .compute import compute_iapf
from .config import IapfConfig
from .manifest import MANIFEST


class IapfAlgorithm:
    manifest = MANIFEST
    config_model = IapfConfig

    def parameter_schema(self) -> ParameterSchema:
        return ParameterSchema(parameters=[
            AlgorithmParameter(key="channel", label_zh="分析通道", value_type="string", description_zh="从原始记录通道中选择一个通道。"),
            AlgorithmParameter(key="mode", label_zh="分析模式", value_type="enum", options=[ParameterOption(value="static", label_zh="静态"), ParameterOption(value="dynamic", label_zh="动态")]),
            AlgorithmParameter(key="start_s", label_zh="分析开始", value_type="number", unit="s", minimum=0, step=0.001),
            AlgorithmParameter(key="end_s", label_zh="分析结束", value_type="number", unit="s", minimum=0, step=0.001),
            AlgorithmParameter(key="window_s", label_zh="动态分析窗口", value_type="number", unit="s", minimum=4, maximum=120, step=1, required=False),
            AlgorithmParameter(key="step_s", label_zh="刷新步长", value_type="number", unit="s", minimum=0.1, maximum=10, step=0.1, required=False),
        ])

    def requested_channels(self, config: AlgorithmConfigBase) -> list[str]:
        return [config.channel]

    def execution_snapshot(self, config: AlgorithmConfigBase) -> AlgorithmExecutionSnapshot:
        return spectral_execution_snapshot(config)

    def resolve_inputs(self, recording: Any, config: IapfConfig) -> AlgorithmInputs:
        labels = list(getattr(recording, "channel_names", getattr(recording, "channels", [])))
        selected = next((label for label in labels if str(label).casefold() == config.channel.casefold()), None)
        if selected is None:
            raise ValueError(f"未知原始通道: {config.channel}")
        return AlgorithmInputs(
            recording_id=str(getattr(recording, "id", getattr(recording, "recording_id", "unknown"))),
            channel=str(selected),
            sfreq_hz=float(getattr(recording, "sfreq_hz", 1.0)),
            duration_s=float(getattr(recording, "duration_s", config.end_s)),
            payload=recording,
        )

    @staticmethod
    def _load(recording: Any, *, channel: str, start_s: float, window_s: float) -> Any:
        loader: Callable[..., Any] = getattr(recording, "load_spectrum")
        return loader(start_s=start_s, window_s=window_s, channels=[channel])

    def execute_static(self, inputs: AlgorithmInputs, config: IapfConfig) -> AlgorithmResult:
        duration = config.end_s - config.start_s
        spectrum = self._load(inputs.payload, channel=inputs.channel, start_s=config.start_s, window_s=duration)
        return compute_iapf(
            spectrum,
            channel=inputs.channel,
            requested_range={"start_s": config.start_s, "end_s": config.end_s},
            actual_range={"start_s": config.start_s, "end_s": config.start_s + duration},
        )

    def execute_dynamic(self, inputs: AlgorithmInputs, config: IapfConfig) -> AlgorithmSeriesResult:
        window_s = config.window_s or 10.0
        step_s = config.step_s or 1.0
        policy = self.manifest.dynamic_policy
        frames = build_dynamic_analysis_frames(
            config.start_s,
            config.end_s,
            duration_s=inputs.duration_s,
            window_s=window_s,
            step_s=step_s,
            minimum_window_s=policy.minimum_window_s,
            allow_warmup=policy.allow_warmup,
        )
        results: list[AlgorithmResult] = []
        for frame in frames:
            requested_range = {"start_s": frame.window_start_s, "end_s": frame.window_end_s}
            try:
                spectrum = self._load(
                    inputs.payload, channel=inputs.channel,
                    start_s=frame.window_start_s, window_s=frame.actual_window_s,
                )
                results.append(compute_iapf(
                    spectrum, channel=inputs.channel,
                    requested_range=requested_range, actual_range=requested_range,
                ))
            except SpectralQualityGateError as exc:
                # A rejected dynamic window is evidence for that point, not a
                # reason to discard all later windows in the same recording.
                quality = dict(exc.quality)
                results.append(AlgorithmResult(
                    value=None, unit="Hz", channel=inputs.channel,
                    requested_range=requested_range, actual_range=requested_range,
                    quality="gate_failed",
                    failure=AlgorithmFailure(
                        code="PSD_QUALITY_GATE_FAILED",
                        message="当前动态窗口未通过 PSD 质量门",
                        detail=quality,
                    ),
                    evidence={
                        "source_quality": {key: quality[key] for key in (
                            "clean_segments", "total_segments", "clean_ratio",
                            "gate_failed", "rejected_reasons",
                        ) if key in quality},
                        "spectral_evidence": dict(quality.get("evidence", {})),
                    },
                ))
        return AlgorithmSeriesResult(
            values=[result.value for result in results],
            time_s=[frame.time_s for frame in frames],
            unit="Hz",
            channel=inputs.channel,
            windows=[{"start_s": frame.window_start_s, "end_s": frame.window_end_s} for frame in frames],
            quality=[result.quality for result in results],
            failures=[result.failure for result in results],
            warmups=[frame.warmup for frame in frames],
            point_evidence=[result.evidence for result in results],
            evidence={"points": len(results)},
        )
