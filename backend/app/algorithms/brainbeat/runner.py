from __future__ import annotations

from collections.abc import Callable
from typing import Any

from app.algorithm_runtime.contracts import (
    AlgorithmConfigBase, AlgorithmExecutionSnapshot, AlgorithmFailure,
    AlgorithmInputs, AlgorithmResult, AlgorithmSeriesResult,
)
from app.algorithm_runtime.parameter_schema import AlgorithmParameter, ParameterOption, ParameterSchema
from app.algorithm_runtime.windows import build_dynamic_analysis_frames
from app.scientific.quality import SpectralQualityGateError
from app.algorithms.spectral_snapshot import spectral_execution_snapshot

from .compute import compute_brainbeat
from .config import BrainbeatConfig
from .manifest import MANIFEST


class BrainbeatAlgorithm:
    manifest = MANIFEST
    config_model = BrainbeatConfig

    def parameter_schema(self) -> ParameterSchema:
        return ParameterSchema(parameters=[
            AlgorithmParameter(key="channel", label_zh="Fz 来源通道", value_type="string", description_zh="选择用于前额 Theta 功率的原始通道。"),
            AlgorithmParameter(key="secondary_channel", label_zh="Pz 来源通道", value_type="string", description_zh="选择用于顶区 Alpha 功率的原始通道。"),
            AlgorithmParameter(key="mode", label_zh="分析模式", value_type="enum", options=[ParameterOption(value="static", label_zh="静态"), ParameterOption(value="dynamic", label_zh="动态")]),
            AlgorithmParameter(key="start_s", label_zh="分析开始", value_type="number", unit="s", minimum=0, step=0.001),
            AlgorithmParameter(key="end_s", label_zh="分析结束", value_type="number", unit="s", minimum=0, step=0.001),
            AlgorithmParameter(key="window_s", label_zh="动态分析窗口", value_type="number", unit="s", minimum=4, maximum=120, step=1, required=False),
            AlgorithmParameter(key="step_s", label_zh="刷新步长", value_type="number", unit="s", minimum=0.1, maximum=10, step=0.1, required=False),
        ])

    def requested_channels(self, config: AlgorithmConfigBase) -> list[str]:
        assert isinstance(config, BrainbeatConfig)
        return [config.channel, config.pz_channel]

    def execution_snapshot(self, config: AlgorithmConfigBase) -> AlgorithmExecutionSnapshot:
        snapshot = spectral_execution_snapshot(config)
        snapshot.window.update({"brainbeat_windows_are_independent": True, "cross_window_ema": False})
        snapshot.quality_rules.update({"required_channels": ["Fz", "Pz"], "iapf_required": True})
        return snapshot

    def resolve_inputs(self, recording: Any, config: BrainbeatConfig) -> AlgorithmInputs:
        labels = list(getattr(recording, "channel_names", getattr(recording, "channels", [])))
        lookup = {str(item).casefold(): str(item) for item in labels}
        if config.channel.casefold() not in lookup or config.pz_channel.casefold() not in lookup:
            raise ValueError("Brainbeat Fz/Pz source channel does not exist")
        fz_channel = lookup[config.channel.casefold()]
        pz_channel = lookup[config.pz_channel.casefold()]
        # Persist the recording's canonical spelling for the downstream loader;
        # role selection remains explicit and case-insensitive at the boundary.
        config.pz_channel = pz_channel
        return AlgorithmInputs(
            recording_id=str(getattr(recording, "id", getattr(recording, "recording_id", "unknown"))),
            channel=fz_channel, sfreq_hz=float(getattr(recording, "sfreq_hz", 1.0)),
            duration_s=float(getattr(recording, "duration_s", config.end_s)), payload=recording,
        )

    @staticmethod
    def _load(recording: Any, *, fz_channel: str, pz_channel: str, start_s: float, window_s: float) -> Any:
        loader: Callable[..., Any] = getattr(recording, "load_spectrum")
        return loader(start_s=start_s, window_s=window_s, channels=[fz_channel, pz_channel])

    def execute_static(self, inputs: AlgorithmInputs, config: BrainbeatConfig) -> AlgorithmResult:
        duration = config.end_s - config.start_s
        spectrum = self._load(inputs.payload, fz_channel=inputs.channel, pz_channel=config.pz_channel, start_s=config.start_s, window_s=duration)
        return compute_brainbeat(spectrum, fz_channel=inputs.channel, pz_channel=config.pz_channel,
            requested_range={"start_s": config.start_s, "end_s": config.end_s},
            actual_range={"start_s": config.start_s, "end_s": config.end_s})

    def execute_dynamic(self, inputs: AlgorithmInputs, config: BrainbeatConfig) -> AlgorithmSeriesResult:
        window_s = config.window_s or self.manifest.dynamic_policy.default_window_s
        step_s = config.step_s or self.manifest.dynamic_policy.refresh_step_s
        frames = build_dynamic_analysis_frames(config.start_s, config.end_s, duration_s=inputs.duration_s,
            window_s=window_s, step_s=step_s, minimum_window_s=self.manifest.dynamic_policy.minimum_window_s,
            allow_warmup=self.manifest.dynamic_policy.allow_warmup)
        results: list[AlgorithmResult] = []
        for frame in frames:
            requested_range = {"start_s": frame.window_start_s, "end_s": frame.window_end_s}
            try:
                spectrum = self._load(inputs.payload, fz_channel=inputs.channel, pz_channel=config.pz_channel,
                    start_s=frame.window_start_s, window_s=frame.actual_window_s)
                results.append(compute_brainbeat(spectrum, fz_channel=inputs.channel, pz_channel=config.pz_channel,
                    requested_range=requested_range, actual_range=requested_range))
            except SpectralQualityGateError as exc:
                quality = dict(exc.quality)
                results.append(AlgorithmResult(value=None, unit="ratio", channel=f"{inputs.channel}/{config.pz_channel}",
                    requested_range=requested_range, actual_range=requested_range, quality="gate_failed",
                    failure=AlgorithmFailure(code="PSD_QUALITY_GATE_FAILED", message="当前 Brainbeat 动态窗口未通过 PSD 质量门", detail=quality),
                    evidence={"source_quality": quality, "spectral_evidence": dict(quality.get("evidence", {}))}))
        return AlgorithmSeriesResult(values=[item.value for item in results], time_s=[frame.time_s for frame in frames], unit="ratio",
            channel=f"{inputs.channel}/{config.pz_channel}", windows=[{"start_s": frame.window_start_s, "end_s": frame.window_end_s} for frame in frames],
            quality=[item.quality for item in results], failures=[item.failure for item in results], warmups=[frame.warmup for frame in frames],
            states=["Rejected" if item.failure is not None else "Complete" for item in results], point_evidence=[item.evidence for item in results],
            evidence={"points": len(results), "independent_windows": True, "cross_window_ema": False})
