from __future__ import annotations

from typing import Any

from app.algorithm_runtime.contracts import AlgorithmConfigBase, AlgorithmExecutionSnapshot, AlgorithmFailure, AlgorithmInputs, AlgorithmResult, AlgorithmSeriesResult
from app.algorithm_runtime.parameter_schema import AlgorithmParameter, ParameterOption, ParameterSchema
from app.algorithms.spectral_snapshot import spectral_execution_snapshot
from app.eeg_core.official_algorithms.rbp import RBP_BANDS
from app.eeg_core.spectral import band_power

from .config import RbpConfig
from .manifest import MANIFEST


class RbpAlgorithm:
    manifest = MANIFEST
    config_model = RbpConfig

    def parameter_schema(self) -> ParameterSchema:
        return ParameterSchema(parameters=[
            AlgorithmParameter(key="channel", label_zh="分析通道", value_type="string", description_zh="从原始记录通道中选择一个通道。"),
            AlgorithmParameter(key="mode", label_zh="分析模式", value_type="enum", options=[ParameterOption(value="static", label_zh="静态")]),
            AlgorithmParameter(key="start_s", label_zh="分析开始", value_type="number", unit="s"),
            AlgorithmParameter(key="end_s", label_zh="分析结束", value_type="number", unit="s"),
        ])

    def requested_channels(self, config: AlgorithmConfigBase) -> list[str]:
        return [config.channel]

    def execution_snapshot(self, config: AlgorithmConfigBase) -> AlgorithmExecutionSnapshot:
        return spectral_execution_snapshot(config)

    def resolve_inputs(self, recording: Any, config: RbpConfig) -> AlgorithmInputs:
        labels = list(getattr(recording, "channel_names", getattr(recording, "channels", [])))
        channel = next((str(item) for item in labels if str(item).casefold() == config.channel.casefold()), None)
        if channel is None:
            raise ValueError(f"未知原始通道: {config.channel}")
        return AlgorithmInputs(recording_id=str(getattr(recording, "id", "unknown")), channel=channel, sfreq_hz=float(getattr(recording, "sfreq_hz", 1.0)), duration_s=float(getattr(recording, "duration_s", config.end_s)), payload=recording)

    def execute_static(self, inputs: AlgorithmInputs, config: RbpConfig) -> AlgorithmResult:
        duration = config.end_s - config.start_s
        spectrum = inputs.payload.load_spectrum(start_s=config.start_s, window_s=duration, channels=[inputs.channel])
        source_quality = {"clean_segments": spectrum.clean_epochs, "total_segments": spectrum.total_epochs, "clean_ratio": spectrum.signal_quality, "gate_failed": spectrum.gate_failed, "rejected_reasons": list(spectrum.rejected_reasons)}
        if spectrum.gate_failed:
            failure = AlgorithmFailure(code="PSD_QUALITY_GATE_FAILED", message="当前窗口未通过 PSD 质量门", detail={"reason": spectrum.gate_failed})
            return AlgorithmResult(value=None, unit="ratio", channel=inputs.channel, requested_range={"start_s": config.start_s, "end_s": config.end_s}, actual_range={"start_s": config.start_s, "end_s": config.end_s}, quality="gate_failed", failure=failure, output_values={name: None for name, *_ in RBP_BANDS}, evidence={"source_quality": source_quality, "spectral_evidence": dict(spectrum.evidence)})
        powers = {name: float(band_power(spectrum.freqs, spectrum.psd[0], low, high) * 1e12) for name, low, high in RBP_BANDS}
        total = sum(powers.values())
        if total <= 0:
            failure = AlgorithmFailure(code="RBP_DENOMINATOR_INVALID", message="1–30 Hz 总功率不是正数", detail={"total_power_uv2": total})
            return AlgorithmResult(value=None, unit="ratio", channel=inputs.channel, requested_range={"start_s": config.start_s, "end_s": config.end_s}, actual_range={"start_s": config.start_s, "end_s": config.end_s}, quality="gate_failed", failure=failure, output_values={name: None for name in powers}, evidence={"source_quality": source_quality, "spectral_evidence": dict(spectrum.evidence)})
        values = {name: power / total for name, power in powers.items()}
        spectral_evidence = dict(spectrum.evidence)
        spectral_evidence["band_power"] = powers
        spectral_evidence["relative_band_power"] = values
        return AlgorithmResult(value=None, unit="ratio", channel=inputs.channel, requested_range={"start_s": config.start_s, "end_s": config.end_s}, actual_range={"start_s": config.start_s, "end_s": config.end_s}, quality="clean", output_values=values, evidence={"source_quality": source_quality, "spectral_evidence": spectral_evidence, "calculation_trace": {"formula": "各频段功率 ÷ Delta、Theta、Alpha、Beta 四频段功率之和", "inputs": [{"label": name.title() + " 功率", "value": power, "unit": "uV^2"} for name, power in powers.items()]}})

    def execute_dynamic(self, inputs: AlgorithmInputs, config: RbpConfig) -> AlgorithmSeriesResult:
        raise NotImplementedError("RBP dynamic output is not enabled")
