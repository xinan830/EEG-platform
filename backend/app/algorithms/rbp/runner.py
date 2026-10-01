from __future__ import annotations

from typing import Any

from app.algorithm_runtime.contracts import AlgorithmConfigBase, AlgorithmExecutionSnapshot, AlgorithmFailure, AlgorithmInputs, AlgorithmResult, AlgorithmSeriesResult
from app.algorithm_runtime.parameter_schema import AlgorithmParameter, ParameterOption, ParameterSchema
from app.algorithms.spectral_snapshot import spectral_execution_snapshot
from .official import RBP_BANDS, RBP_FILTER_LOW_HZ, RBP_FILTER_HIGH_HZ, RBP_NOTCH_HZ, five_band_shares
from app.algorithm_runtime.windows import build_dynamic_analysis_frames
from app.scientific.contracts.types import SampleRange
from app.scientific.quality import SpectralQualityGateError

from .config import RbpConfig
from .manifest import MANIFEST


class RbpAlgorithm:
    manifest = MANIFEST
    config_model = RbpConfig

    def parameter_schema(self) -> ParameterSchema:
        return ParameterSchema(parameters=[
            AlgorithmParameter(key="channel", label_zh="分析通道", value_type="string", description_zh="从原始记录通道中选择一个通道。"),
            AlgorithmParameter(key="mode", label_zh="分析模式", value_type="enum", options=[ParameterOption(value="static", label_zh="静态"), ParameterOption(value="dynamic", label_zh="动态")]),
            AlgorithmParameter(key="start_s", label_zh="分析开始", value_type="number", unit="s", minimum=0, step=0.001),
            AlgorithmParameter(key="end_s", label_zh="分析结束", value_type="number", unit="s", minimum=0, step=0.001),
        ])

    def requested_channels(self, config: AlgorithmConfigBase) -> list[str]:
        return [config.channel]

    def execution_snapshot(self, config: AlgorithmConfigBase) -> AlgorithmExecutionSnapshot:
        snapshot = spectral_execution_snapshot(config)
        return snapshot.model_copy(update={"filters": snapshot.filters | {
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
        return AlgorithmInputs(recording_id=str(getattr(recording, "id", "unknown")), channel=channel, sfreq_hz=float(getattr(recording, "sfreq_hz", 1.0)), duration_s=float(getattr(recording, "duration_s", config.end_s)), payload=recording)

    @staticmethod
    def _load_spectrum(payload: Any, *, start_s: float, window_s: float, channel: str) -> Any:
        return payload.load_spectrum(
            start_s=start_s, window_s=window_s, channels=[channel],
            filter_low_hz=RBP_FILTER_LOW_HZ, filter_high_hz=RBP_FILTER_HIGH_HZ,
            output_low_hz=RBP_FILTER_LOW_HZ, output_high_hz=RBP_FILTER_HIGH_HZ,
            notch_hz=RBP_NOTCH_HZ,
        )

    def execute_static(self, inputs: AlgorithmInputs, config: RbpConfig) -> AlgorithmResult:
        duration = config.end_s - config.start_s
        spectrum = self._load_spectrum(inputs.payload, start_s=config.start_s, window_s=duration, channel=inputs.channel)
        source_quality = {"clean_segments": spectrum.clean_epochs, "total_segments": spectrum.total_epochs, "clean_ratio": spectrum.signal_quality, "gate_failed": spectrum.gate_failed, "rejected_reasons": list(spectrum.rejected_reasons)}
        if spectrum.gate_failed:
            failure = AlgorithmFailure(code="PSD_QUALITY_GATE_FAILED", message="当前窗口未通过 PSD 质量门", detail={"reason": spectrum.gate_failed})
            return AlgorithmResult(value=None, unit="ratio", channel=inputs.channel, requested_range={"start_s": config.start_s, "end_s": config.end_s}, actual_range={"start_s": config.start_s, "end_s": config.end_s}, quality="gate_failed", failure=failure, output_values={name: None for name, *_ in RBP_BANDS}, evidence={"source_quality": source_quality, "spectral_evidence": dict(spectrum.evidence)})
        try:
            powers, values = five_band_shares(spectrum)
        except ValueError as exc:
            failure = AlgorithmFailure(code="RBP_DENOMINATOR_INVALID", message=str(exc))
            return AlgorithmResult(value=None, unit="ratio", channel=inputs.channel, requested_range={"start_s": config.start_s, "end_s": config.end_s}, actual_range={"start_s": config.start_s, "end_s": config.end_s}, quality="gate_failed", failure=failure, output_values={name: None for name, *_ in RBP_BANDS}, evidence={"source_quality": source_quality, "spectral_evidence": dict(spectrum.evidence)})
        spectral_evidence = dict(spectrum.evidence)
        spectral_evidence["band_power"] = powers
        spectral_evidence["relative_band_power"] = values
        spectral_evidence["rbp_denominator_uv2"] = sum(powers.values())
        return AlgorithmResult(value=None, unit="ratio", channel=inputs.channel, requested_range={"start_s": config.start_s, "end_s": config.end_s}, actual_range={"start_s": config.start_s, "end_s": config.end_s}, quality="clean", output_values=values, evidence={"source_quality": source_quality, "spectral_evidence": spectral_evidence, "calculation_trace": {"formula": "各频段功率 ÷ Delta、Theta、Alpha、Beta、Gamma 五频段功率之和", "denominator_uv2": sum(powers.values()), "bands_hz": {name: [low, high] for name, low, high in RBP_BANDS}, "inputs": [{"label": name.title() + " 功率", "value": power, "unit": "uV^2"} for name, power in powers.items()]}})

    def execute_dynamic(self, inputs: AlgorithmInputs, config: RbpConfig) -> AlgorithmSeriesResult:
        window_s = config.window_s or self.manifest.dynamic_policy.default_window_s
        step_s = config.step_s or self.manifest.dynamic_policy.refresh_step_s
        frames = build_dynamic_analysis_frames(
            config.start_s, config.end_s, duration_s=inputs.duration_s,
            window_s=window_s, step_s=step_s,
            minimum_window_s=self.manifest.dynamic_policy.minimum_window_s,
            allow_warmup=self.manifest.dynamic_policy.allow_warmup,
        )
        if not frames:
            raise ValueError("RBP 动态分析区间没有可执行窗口")

        values: list[float | None] = []
        output_values: list[dict[str, float | None]] = []
        qualities: list[str] = []
        failures: list[AlgorithmFailure | None] = []
        windows: list[dict[str, float]] = []
        warmups: list[bool] = []
        evidence: list[dict[str, Any]] = []
        for frame in frames:
            sample_range = SampleRange.from_seconds(frame.window_start_s, frame.window_end_s, inputs.sfreq_hz)
            windows.append({"start_s": frame.window_start_s, "end_s": frame.window_end_s})
            warmups.append(frame.warmup)
            bands: dict[str, float | None] = {name: None for name, *_ in RBP_BANDS}
            failure: AlgorithmFailure | None = None
            quality = "clean"
            source_quality: dict[str, Any] = {}
            spectral_evidence: dict[str, Any] = {}
            try:
                spectrum = self._load_spectrum(inputs.payload, start_s=frame.window_start_s, window_s=frame.actual_window_s, channel=inputs.channel)
                source_quality = {"clean_segments": spectrum.clean_epochs, "total_segments": spectrum.total_epochs, "clean_ratio": spectrum.signal_quality, "gate_failed": spectrum.gate_failed, "rejected_reasons": list(spectrum.rejected_reasons)}
                spectral_evidence = dict(spectrum.evidence)
                if spectrum.gate_failed:
                    raise SpectralQualityGateError(source_quality)
                powers, bands = five_band_shares(spectrum)
                spectral_evidence["band_power"] = powers
                spectral_evidence["relative_band_power"] = bands
                spectral_evidence["rbp_denominator_uv2"] = sum(powers.values())
            except SpectralQualityGateError as exc:
                quality = "gate_failed"
                failure = AlgorithmFailure(code="RBP_WINDOW_QUALITY_GATE_FAILED", message="当前动态窗口未通过 RBP 质量门", detail=dict(exc.quality))
            except (ValueError, OSError) as exc:
                quality = "unavailable"
                failure = AlgorithmFailure(code="RBP_WINDOW_UNAVAILABLE", message=str(exc))
            if failure is not None:
                values.append(None)
            else:
                values.append(None)
            qualities.append(quality)
            failures.append(failure)
            output_values.append(bands)
            evidence.append({"source_quality": source_quality, "spectral_evidence": spectral_evidence})
        return AlgorithmSeriesResult(
            values=values, time_s=[frame.time_s for frame in frames], unit="ratio", channel=inputs.channel,
            windows=windows, quality=qualities, failures=failures, warmups=warmups,
            states=["Rejected" if failure and quality == "gate_failed" else "Unavailable" if failure else "Partial" if frame.warmup else "Complete" for frame, failure, quality in zip(frames, failures, qualities)],
            point_evidence=evidence, output_values=output_values,
            evidence={"bands": [name for name, *_ in RBP_BANDS], "window_s": window_s, "step_s": step_s},
        )
