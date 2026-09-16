from __future__ import annotations

from collections.abc import Callable
from typing import Any

from app.algorithm_runtime.contracts import AlgorithmInputs, AlgorithmResult, AlgorithmSeriesResult
from app.algorithm_runtime.parameter_schema import AlgorithmParameter, ParameterOption, ParameterSchema
from app.algorithm_runtime.windows import build_playback_windows

from .compute import compute_theta_beta
from .config import ThetaBetaConfig
from .manifest import MANIFEST


class ThetaBetaAlgorithm:
    manifest = MANIFEST
    config_model = ThetaBetaConfig

    def parameter_schema(self) -> ParameterSchema:
        return ParameterSchema(parameters=[
            AlgorithmParameter(key="channel", label_zh="分析通道", value_type="string", description_zh="从原始记录通道中选择一个通道。"),
            AlgorithmParameter(key="mode", label_zh="分析模式", value_type="enum", options=[ParameterOption(value="static", label_zh="静态"), ParameterOption(value="dynamic", label_zh="动态")]),
            AlgorithmParameter(key="start_s", label_zh="分析开始", value_type="number", unit="s"),
            AlgorithmParameter(key="end_s", label_zh="分析结束", value_type="number", unit="s"),
            AlgorithmParameter(key="window_s", label_zh="动态分析窗口", value_type="number", unit="s", required=False),
            AlgorithmParameter(key="step_s", label_zh="刷新步长", value_type="number", unit="s", required=False),
        ])

    def resolve_inputs(self, recording: Any, config: ThetaBetaConfig) -> AlgorithmInputs:
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

    def execute_static(self, inputs: AlgorithmInputs, config: ThetaBetaConfig) -> AlgorithmResult:
        duration = config.end_s - config.start_s
        return compute_theta_beta(
            self._load(inputs.payload, channel=inputs.channel, start_s=config.start_s, window_s=duration),
            channel=inputs.channel,
            requested_range={"start_s": config.start_s, "end_s": config.end_s},
            actual_range={"start_s": config.start_s, "end_s": config.end_s},
        )

    def execute_dynamic(self, inputs: AlgorithmInputs, config: ThetaBetaConfig) -> AlgorithmSeriesResult:
        window_s = config.window_s or 10.0
        step_s = config.step_s or 1.0
        windows = build_playback_windows(config.start_s, config.end_s, duration_s=inputs.duration_s, window_s=window_s, step_s=step_s)
        results = [
            compute_theta_beta(
                self._load(inputs.payload, channel=inputs.channel, start_s=window.start_s, window_s=window_s),
                channel=inputs.channel,
                requested_range={"start_s": window.start_s, "end_s": window.end_s},
                actual_range={"start_s": window.start_s, "end_s": window.end_s},
            )
            for window in windows
        ]
        return AlgorithmSeriesResult(
            values=[result.value for result in results],
            time_s=[window.end_s for window in windows],
            unit="dimensionless",
            channel=inputs.channel,
            windows=[{"start_s": window.start_s, "end_s": window.end_s} for window in windows],
            quality=[result.quality for result in results],
            failures=[result.failure for result in results],
            warmups=[window.warmup for window in windows],
            point_evidence=[result.evidence for result in results],
            evidence={"points": len(results)},
        )
