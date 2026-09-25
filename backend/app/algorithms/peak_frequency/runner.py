from __future__ import annotations

from collections.abc import Callable
from typing import Any

from app.algorithm_runtime.contracts import AlgorithmConfigBase, AlgorithmExecutionSnapshot, AlgorithmInputs, AlgorithmResult, AlgorithmSeriesResult
from app.algorithm_runtime.parameter_schema import AlgorithmParameter, ParameterOption, ParameterSchema
from app.algorithm_runtime.windows import build_dynamic_analysis_frames
from app.algorithms.spectral_snapshot import spectral_execution_snapshot

from .compute import compute_peak_frequency
from .config import PeakFrequencyConfig
from .manifest import MANIFEST


class PeakFrequencyAlgorithm:
    manifest = MANIFEST
    config_model = PeakFrequencyConfig
    output_schema = MANIFEST.output_schema

    def parameter_schema(self) -> ParameterSchema:
        return ParameterSchema(parameters=[
            AlgorithmParameter(key="channel", label_zh="分析通道", value_type="string", description_zh="从原始记录通道中选择一个通道。"),
            AlgorithmParameter(key="mode", label_zh="分析模式", value_type="enum", options=[ParameterOption(value="static", label_zh="静态"), ParameterOption(value="dynamic", label_zh="动态")]),
            AlgorithmParameter(key="start_s", label_zh="分析开始", value_type="number", unit="s", minimum=0, step=0.001),
            AlgorithmParameter(key="end_s", label_zh="分析结束", value_type="number", unit="s", minimum=0, step=0.001),
            AlgorithmParameter(key="low_hz", label_zh="频段下限", value_type="number", unit="Hz", minimum=0, step=0.001),
            AlgorithmParameter(key="high_hz", label_zh="频段上限", value_type="number", unit="Hz", minimum=0, step=0.001),
            AlgorithmParameter(key="window_s", label_zh="动态分析窗口", value_type="number", unit="s", minimum=4, maximum=120, step=1, required=False),
            AlgorithmParameter(key="step_s", label_zh="刷新步长", value_type="number", unit="s", minimum=0.1, maximum=10, step=0.1, required=False),
        ])

    def requested_channels(self, config: AlgorithmConfigBase) -> list[str]:
        return [config.channel]

    def execution_snapshot(self, config: AlgorithmConfigBase) -> AlgorithmExecutionSnapshot:
        snapshot = spectral_execution_snapshot(config)
        snapshot.window["frequency_band_hz"] = [config.low_hz, config.high_hz]
        snapshot.window["peak_policy"] = "inclusive_grid_max_lowest_tie_no_interpolation"
        return snapshot

    def resolve_inputs(self, recording: Any, config: PeakFrequencyConfig) -> AlgorithmInputs:
        labels = list(getattr(recording, "channel_names", getattr(recording, "channels", [])))
        selected = next((str(label) for label in labels if str(label).casefold() == config.channel.casefold()), None)
        if selected is None:
            raise ValueError(f"未知原始通道: {config.channel}")
        if config.high_hz >= float(recording.sfreq_hz) / 2:
            raise ValueError("频段上限必须低于 Nyquist 频率")
        return AlgorithmInputs(recording_id=str(getattr(recording, "id", "unknown")), channel=selected,
                               sfreq_hz=float(recording.sfreq_hz), duration_s=float(recording.duration_s), payload=recording)

    @staticmethod
    def _load(recording: Any, *, channel: str, start_s: float, window_s: float) -> Any:
        loader: Callable[..., Any] = getattr(recording, "load_spectrum")
        return loader(start_s=start_s, window_s=window_s, channels=[channel])

    def execute_static(self, inputs: AlgorithmInputs, config: PeakFrequencyConfig) -> AlgorithmResult:
        duration = config.end_s - config.start_s
        return compute_peak_frequency(self._load(inputs.payload, channel=inputs.channel, start_s=config.start_s, window_s=duration), channel=inputs.channel,
                                      low_hz=config.low_hz, high_hz=config.high_hz,
                                      requested_range={"start_s": config.start_s, "end_s": config.end_s}, actual_range={"start_s": config.start_s, "end_s": config.end_s})

    def execute_dynamic(self, inputs: AlgorithmInputs, config: PeakFrequencyConfig) -> AlgorithmSeriesResult:
        window_s, step_s = config.window_s or 10.0, config.step_s or 1.0
        frames = build_dynamic_analysis_frames(config.start_s, config.end_s, duration_s=inputs.duration_s, window_s=window_s, step_s=step_s,
                                               minimum_window_s=self.manifest.dynamic_policy.minimum_window_s, allow_warmup=self.manifest.dynamic_policy.allow_warmup)
        results = [compute_peak_frequency(self._load(inputs.payload, channel=inputs.channel, start_s=frame.window_start_s, window_s=frame.actual_window_s), channel=inputs.channel,
                                          low_hz=config.low_hz, high_hz=config.high_hz,
                                          requested_range={"start_s": frame.window_start_s, "end_s": frame.window_end_s}, actual_range={"start_s": frame.window_start_s, "end_s": frame.window_end_s}) for frame in frames]
        return AlgorithmSeriesResult(values=[item.value for item in results], time_s=[frame.time_s for frame in frames], unit="Hz", channel=inputs.channel,
                                     windows=[{"start_s": frame.window_start_s, "end_s": frame.window_end_s} for frame in frames], quality=[item.quality for item in results],
                                     failures=[item.failure for item in results], warmups=[frame.warmup for frame in frames], point_evidence=[item.evidence for item in results],
                                     evidence={"points": len(results), "band_hz": [config.low_hz, config.high_hz]})
