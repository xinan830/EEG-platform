from __future__ import annotations

from collections.abc import Callable
from typing import Any

from app.algorithm_runtime.contracts import AlgorithmConfigBase, AlgorithmExecutionSnapshot, AlgorithmInputs, AlgorithmResult, AlgorithmSeriesResult
from app.algorithm_runtime.parameter_schema import AlgorithmParameter, ParameterOption, ParameterSchema
from app.algorithm_runtime.windows import build_dynamic_analysis_frames
from app.algorithms.spectral_snapshot import spectral_execution_snapshot

from .compute import compute_band_ratio
from .config import BandRatioConfig
from .manifest import MANIFEST


class BandRatioAlgorithm:
    manifest = MANIFEST
    config_model = BandRatioConfig
    output_schema = MANIFEST.output_schema

    def parameter_schema(self) -> ParameterSchema:
        number = lambda key, label: AlgorithmParameter(key=key, label_zh=label, value_type="number", unit="Hz", minimum=0, step=0.001)
        return ParameterSchema(parameters=[
            AlgorithmParameter(key="channel", label_zh="分析通道", value_type="string", description_zh="从原始记录通道中选择一个通道。"),
            AlgorithmParameter(key="mode", label_zh="分析模式", value_type="enum", options=[ParameterOption(value="static", label_zh="静态"), ParameterOption(value="dynamic", label_zh="动态")]),
            AlgorithmParameter(key="start_s", label_zh="分析开始", value_type="number", unit="s", minimum=0, step=0.001),
            AlgorithmParameter(key="end_s", label_zh="分析结束", value_type="number", unit="s", minimum=0, step=0.001),
            number("numerator_low_hz", "分子频段下限"), number("numerator_high_hz", "分子频段上限"), number("denominator_low_hz", "分母频段下限"), number("denominator_high_hz", "分母频段上限"),
            AlgorithmParameter(key="window_s", label_zh="动态分析窗口", value_type="number", unit="s", minimum=4, maximum=120, step=1, required=False),
            AlgorithmParameter(key="step_s", label_zh="刷新步长", value_type="number", unit="s", minimum=0.1, maximum=10, step=0.1, required=False),
        ])

    def requested_channels(self, config: AlgorithmConfigBase) -> list[str]:
        return [config.channel]

    def execution_snapshot(self, config: AlgorithmConfigBase) -> AlgorithmExecutionSnapshot:
        snapshot = spectral_execution_snapshot(config)
        snapshot.window["numerator_band_hz"] = [config.numerator_low_hz, config.numerator_high_hz]
        snapshot.window["denominator_band_hz"] = [config.denominator_low_hz, config.denominator_high_hz]
        snapshot.window["ratio_policy"] = "numerator_power_divided_by_positive_denominator_power"
        return snapshot

    def resolve_inputs(self, recording: Any, config: BandRatioConfig) -> AlgorithmInputs:
        labels = list(getattr(recording, "channel_names", getattr(recording, "channels", [])))
        selected = next((str(label) for label in labels if str(label).casefold() == config.channel.casefold()), None)
        if selected is None:
            raise ValueError(f"未知原始通道: {config.channel}")
        if max(config.numerator_high_hz, config.denominator_high_hz) >= float(recording.sfreq_hz) / 2:
            raise ValueError("频段上限必须低于 Nyquist 频率")
        return AlgorithmInputs(recording_id=str(getattr(recording, "id", "unknown")), channel=selected, sfreq_hz=float(recording.sfreq_hz), duration_s=float(recording.duration_s), payload=recording)

    @staticmethod
    def _load(recording: Any, *, channel: str, start_s: float, window_s: float) -> Any:
        loader: Callable[..., Any] = getattr(recording, "load_spectrum")
        return loader(start_s=start_s, window_s=window_s, channels=[channel])

    def _calculate(self, inputs: AlgorithmInputs, config: BandRatioConfig, start_s: float, end_s: float) -> AlgorithmResult:
        return compute_band_ratio(self._load(inputs.payload, channel=inputs.channel, start_s=start_s, window_s=end_s - start_s), channel=inputs.channel,
                                  numerator_low_hz=config.numerator_low_hz, numerator_high_hz=config.numerator_high_hz,
                                  denominator_low_hz=config.denominator_low_hz, denominator_high_hz=config.denominator_high_hz,
                                  requested_range={"start_s": start_s, "end_s": end_s}, actual_range={"start_s": start_s, "end_s": end_s})

    def execute_static(self, inputs: AlgorithmInputs, config: BandRatioConfig) -> AlgorithmResult:
        return self._calculate(inputs, config, config.start_s, config.end_s)

    def execute_dynamic(self, inputs: AlgorithmInputs, config: BandRatioConfig) -> AlgorithmSeriesResult:
        window_s, step_s = config.window_s or 10.0, config.step_s or 1.0
        frames = build_dynamic_analysis_frames(config.start_s, config.end_s, duration_s=inputs.duration_s, window_s=window_s, step_s=step_s,
                                               minimum_window_s=self.manifest.dynamic_policy.minimum_window_s, allow_warmup=self.manifest.dynamic_policy.allow_warmup)
        results = [self._calculate(inputs, config, frame.window_start_s, frame.window_end_s) for frame in frames]
        return AlgorithmSeriesResult(values=[item.value for item in results], time_s=[frame.time_s for frame in frames], unit="ratio", channel=inputs.channel,
                                     windows=[{"start_s": frame.window_start_s, "end_s": frame.window_end_s} for frame in frames], quality=[item.quality for item in results],
                                     failures=[item.failure for item in results], warmups=[frame.warmup for frame in frames], point_evidence=[item.evidence for item in results],
                                     evidence={"points": len(results), "numerator_band_hz": [config.numerator_low_hz, config.numerator_high_hz], "denominator_band_hz": [config.denominator_low_hz, config.denominator_high_hz]})
