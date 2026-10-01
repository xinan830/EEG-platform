from __future__ import annotations

from typing import Any

from app.algorithm_runtime.contracts import AlgorithmConfigBase, AlgorithmExecutionSnapshot, AlgorithmFailure, AlgorithmInputs, AlgorithmResult, AlgorithmSeriesResult
from app.algorithm_runtime.parameter_schema import AlgorithmParameter, ParameterOption, ParameterSchema
from app.algorithm_runtime.windows import build_dynamic_analysis_frames
from .official import compute_faa

from .config import FaaConfig
from .manifest import MANIFEST


class FaaAlgorithm:
    manifest = MANIFEST
    config_model = FaaConfig

    def parameter_schema(self) -> ParameterSchema:
        return ParameterSchema(parameters=[
            AlgorithmParameter(key="channel", label_zh="F3 来源通道", value_type="string", description_zh="选择用于 FAA 左侧 Alpha 功率的原始通道。"),
            AlgorithmParameter(key="f4_channel", label_zh="F4 来源通道", value_type="string", description_zh="选择用于 FAA 右侧 Alpha 功率的原始通道。"),
            AlgorithmParameter(key="low_hz", label_zh="高通频率", value_type="number", unit="Hz", default=1.0, description_zh="连续预处理高通截止频率。"),
            AlgorithmParameter(key="high_hz", label_zh="低通频率", value_type="number", unit="Hz", default=50.0, description_zh="连续预处理低通截止频率。"),
            AlgorithmParameter(key="notch_hz", label_zh="陷波频率", value_type="enum", options=[ParameterOption(value=0.0, label_zh="关闭"), ParameterOption(value=50.0, label_zh="50 Hz"), ParameterOption(value=60.0, label_zh="60 Hz")], default=50.0, description_zh="连续预处理工频陷波。"),
            AlgorithmParameter(key="mode", label_zh="分析模式", value_type="enum", options=[ParameterOption(value="static", label_zh="静态"), ParameterOption(value="dynamic", label_zh="动态")]),
            AlgorithmParameter(key="start_s", label_zh="分析开始", value_type="number", unit="s", minimum=0, step=0.001),
            AlgorithmParameter(key="end_s", label_zh="分析结束", value_type="number", unit="s", minimum=0, step=0.001),
        ])

    def requested_channels(self, config: AlgorithmConfigBase) -> list[str]:
        assert isinstance(config, FaaConfig)
        return [config.channel, config.f4_channel]

    def execution_snapshot(self, config: AlgorithmConfigBase) -> AlgorithmExecutionSnapshot:
        return AlgorithmExecutionSnapshot(
            window={
                "mode": config.mode, "method": "paired_epoch_rfft_density",
                "epoch_s": 2.0, "epoch_overlap": 0.5, "epoch_step_s": 1.0,
                "window": "hann", "alignment": "window_end" if config.mode == "dynamic" else "range",
            },
            filters={"operation": "continuous_preprocess_then_per_epoch_mean_removal", "software_bandpass": [config.low_hz, config.high_hz], "notch_hz": config.notch_hz},
            quality_rules={
                "paired_epoch": True, "minimum_clean_epochs": 10,
                "artifact_peak_uv": 150.0, "reasons": ["non_finite", "amplitude_threshold"],
            },
        )

    def resolve_inputs(self, recording: Any, config: FaaConfig) -> AlgorithmInputs:
        labels = list(getattr(recording, "channel_names", getattr(recording, "channels", [])))
        lookup = {str(item).casefold(): str(item) for item in labels}
        if config.channel.casefold() not in lookup or config.f4_channel.casefold() not in lookup:
            raise ValueError("FAA source channel does not exist")
        return AlgorithmInputs(recording_id=str(getattr(recording, "id", "unknown")), channel=lookup[config.channel.casefold()], sfreq_hz=float(getattr(recording, "sfreq_hz", 1.0)), duration_s=float(getattr(recording, "duration_s", config.end_s)), payload=recording)

    def execute_static(self, inputs: AlgorithmInputs, config: FaaConfig) -> AlgorithmResult:
        f3, f4, sfreq, source = inputs.payload.load_faa_signals(start_s=config.start_s, end_s=config.end_s, f3_channel=inputs.channel, f4_channel=config.f4_channel, low_hz=config.low_hz, high_hz=config.high_hz, notch_hz=config.notch_hz)
        report = compute_faa(f3, f4, sfreq)
        quality = {"clean_segments": report["clean_epochs"], "total_segments": report["total_epochs"], "clean_ratio": report["clean_ratio"], "gate_failed": report["reason"] or None, "rejected_reasons": [report["reason"]] if report["reason"] else []}
        left_channel, right_channel = source["channels"]
        evidence = {
            "source_quality": quality,
            "extensions": {"faa_evidence": {
                **source,
                **report,
                "source_channels": {"left": left_channel, "right": right_channel},
                "formula": "ln(P_right_alpha) - ln(P_left_alpha)",
            }},
            "calculation_trace": {
                "formula": "ln(P_right_alpha) - ln(P_left_alpha)",
                "inputs": [
                    {"label": f"左来源 {left_channel} Alpha 功率", "value": report["p_f3"] * 1e12 if report["p_f3"] is not None else None, "unit": "uV^2"},
                    {"label": f"右来源 {right_channel} Alpha 功率", "value": report["p_f4"] * 1e12 if report["p_f4"] is not None else None, "unit": "uV^2"},
                ],
            },
        }
        if report["faa"] is None:
            failure = AlgorithmFailure(code="FAA_QUALITY_GATE_FAILED", message="当前范围未达到 FAA 成对 epoch 质量要求", detail={"reason": report["reason"]})
            return AlgorithmResult(value=None, unit="dimensionless", channel=f"{inputs.channel}/{config.f4_channel}", requested_range={"start_s": config.start_s, "end_s": config.end_s}, actual_range={"start_s": config.start_s, "end_s": config.end_s}, quality="gate_failed", failure=failure, evidence=evidence)
        return AlgorithmResult(value=float(report["faa"]), unit="dimensionless", channel=f"{inputs.channel}/{config.f4_channel}", requested_range={"start_s": config.start_s, "end_s": config.end_s}, actual_range={"start_s": config.start_s, "end_s": config.end_s}, quality="clean", evidence=evidence)

    def execute_dynamic(self, inputs: AlgorithmInputs, config: FaaConfig) -> AlgorithmSeriesResult:
        window_s = config.window_s or self.manifest.dynamic_policy.default_window_s
        step_s = config.step_s or self.manifest.dynamic_policy.refresh_step_s
        frames = build_dynamic_analysis_frames(
            config.start_s, config.end_s, duration_s=inputs.duration_s,
            window_s=window_s, step_s=step_s,
            minimum_window_s=self.manifest.dynamic_policy.minimum_window_s,
            allow_warmup=False,
        )
        if not frames:
            raise ValueError("FAA 动态分析区间没有可执行窗口")
        results = []
        for frame in frames:
            frame_config = config.model_copy(update={"start_s": frame.window_start_s, "end_s": frame.window_end_s})
            results.append(self.execute_static(inputs, frame_config))
        return AlgorithmSeriesResult(
            values=[item.value for item in results],
            time_s=[frame.time_s for frame in frames],
            unit="dimensionless", channel=f"{inputs.channel}/{config.f4_channel}",
            windows=[{"start_s": frame.window_start_s, "end_s": frame.window_end_s} for frame in frames],
            quality=[item.quality for item in results],
            failures=[item.failure for item in results],
            warmups=[False for _ in frames],
            states=["Rejected" if item.failure is not None else "Complete" for item in results],
            point_evidence=[item.evidence for item in results],
            evidence={"window_s": window_s, "step_s": step_s, "paired_epoch": True},
        )
