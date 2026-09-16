"""Adapt an immutable user Definition graph to the common algorithm runtime."""

from __future__ import annotations

from typing import Any

from app.algorithm_runtime.contracts import (
    AlgorithmConfigBase,
    AlgorithmFailure,
    AlgorithmInputs,
    AlgorithmManifest,
    AlgorithmResult,
    AlgorithmSeriesResult,
)
from app.algorithm_runtime.parameter_schema import AlgorithmParameter, ParameterOption, ParameterSchema
from app.algorithm_runtime.windows import build_dynamic_analysis_frames
from app.eeg_core.definition_engine import execute_graph
from app.eeg_core.primitives.types import Scalar
from app.eeg_core.quality import SpectralQualityGateError
from app.models.algorithm_definition import DefinitionVersionDraft
from app.services.definition_metric_runner import DefinitionMetricRunner


class UserDefinitionAlgorithm:
    """One immutable Definition version exposed through the runtime protocol."""

    config_model = AlgorithmConfigBase

    def __init__(self, definition_id: str, definition: DefinitionVersionDraft, metric_runner: DefinitionMetricRunner) -> None:
        self.definition_id = definition_id
        self.definition = definition
        self.metric_runner = metric_runner
        self.manifest = AlgorithmManifest(
            algorithm_id=definition_id,
            display_name_zh=definition.outputs.get(next(iter(definition.outputs), "output"), {}).get("label", definition_id),
            abbreviation=definition_id,
            purpose_zh="用户定义的受控科研计算图",
            scientific_version=definition.semver,
            implementation_identity="user-definition-runtime-v1",
            supported_modes=["static", "dynamic"],
            output_unit=self._output_unit(),
        )

    def _output_unit(self) -> str:
        output = next(iter(self.definition.outputs.values()), {})
        return str(output.get("unit", "dimensionless")) if isinstance(output, dict) else "dimensionless"

    def parameter_schema(self) -> ParameterSchema:
        return ParameterSchema(parameters=[
            AlgorithmParameter(key="channel", label_zh="分析通道", value_type="string", description_zh="从原始记录中选择一个通道。"),
            AlgorithmParameter(key="mode", label_zh="分析模式", value_type="enum", options=[ParameterOption(value="static", label_zh="静态"), ParameterOption(value="dynamic", label_zh="动态")]),
            AlgorithmParameter(key="start_s", label_zh="分析开始", value_type="number", unit="s"),
            AlgorithmParameter(key="end_s", label_zh="分析结束", value_type="number", unit="s"),
            AlgorithmParameter(key="window_s", label_zh="动态分析窗口", value_type="number", unit="s", required=False),
            AlgorithmParameter(key="step_s", label_zh="刷新步长", value_type="number", unit="s", required=False),
        ])

    def resolve_inputs(self, recording: Any, config: AlgorithmConfigBase) -> AlgorithmInputs:
        labels = list(getattr(recording, "channels", []))
        selected = next((label for label in labels if str(label).casefold() == config.channel.casefold()), None)
        if selected is None:
            raise ValueError(f"未知原始通道: {config.channel}")
        return AlgorithmInputs(
            recording_id=str(getattr(recording, "id", "unknown")),
            channel=str(selected),
            sfreq_hz=float(getattr(recording, "sfreq", 0.0)),
            duration_s=float(getattr(recording, "duration_s", 0.0)),
            payload=recording,
        )

    def _compute(self, inputs: AlgorithmInputs, start_s: float, end_s: float) -> tuple[str, Scalar, Any]:
        resolution = self.metric_runner.resolve_window(inputs.payload, self.definition, inputs.channel, start_s, end_s)
        outputs = execute_graph(self.definition.graph, resolution.inputs)
        if len(outputs) != 1:
            raise ValueError("definition metric requires exactly one scalar output")
        output_id, output = next(iter(outputs.items()))
        if not isinstance(output, Scalar):
            raise ValueError("definition metric output must be a scalar")
        return output_id, output, resolution

    def _result(self, inputs: AlgorithmInputs, start_s: float, end_s: float) -> AlgorithmResult:
        output_id, output, resolution = self._compute(inputs, start_s, end_s)
        metadata = self.definition.outputs.get(output_id, {})
        label = metadata.get("label", output_id) if isinstance(metadata, dict) else output_id
        quality = {
            "status": output.quality.status,
            "reasons": list(output.quality.reasons),
            "rejected_reasons": list(output.quality.rejected_reasons),
        }
        failure = None if output.value is not None else AlgorithmFailure(
            code="METRIC_OUTPUT_UNAVAILABLE",
            message="当前分析窗口无法得到有效算法输出",
            detail={"reasons": quality["reasons"]},
        )
        return AlgorithmResult(
            value=float(output.value) if output.value is not None else None,
            unit=output.unit.value,
            channel=inputs.channel,
            requested_range={"start_s": start_s, "end_s": end_s},
            actual_range=resolution.actual_range,
            quality="clean" if failure is None else "gate_failed",
            failure=failure,
            evidence={
                "output_id": output_id,
                "output_label": label,
                "inputs": resolution.snapshot,
                "source_quality": resolution.quality,
                "spectral_evidence": resolution.spectral_evidence,
                "provenance": [{"node": item.node, "parameters": dict(item.parameters)} for item in output.provenance],
                "calculation_trace": {
                    "formula": "用户定义的受控算法图",
                    "inputs": [{"label": str(key), **value} for key, value in resolution.snapshot.items()],
                },
            },
        )

    def execute_static(self, inputs: AlgorithmInputs, config: AlgorithmConfigBase) -> AlgorithmResult:
        return self._result(inputs, config.start_s, config.end_s)

    def execute_dynamic(self, inputs: AlgorithmInputs, config: AlgorithmConfigBase) -> AlgorithmSeriesResult:
        frames = build_dynamic_analysis_frames(
            config.start_s,
            config.end_s,
            duration_s=inputs.duration_s,
            window_s=config.window_s or 10.0,
            step_s=config.step_s or 1.0,
        )
        results: list[AlgorithmResult] = []
        for frame in frames:
            try:
                results.append(self._result(inputs, frame.window_start_s, frame.window_end_s))
            except SpectralQualityGateError as exc:
                results.append(AlgorithmResult(
                    value=None,
                    unit=self._output_unit(),
                    channel=inputs.channel,
                    requested_range={"start_s": frame.window_start_s, "end_s": frame.window_end_s},
                    actual_range={"start_s": frame.window_start_s, "end_s": frame.window_end_s},
                    quality="gate_failed",
                    failure=AlgorithmFailure(
                        code="PSD_QUALITY_GATE_FAILED",
                        message="当前窗口未通过 PSD 质量门",
                        detail={"quality": exc.quality},
                    ),
                    evidence={"source_quality": dict(exc.quality), "spectral_evidence": {}},
                ))
        return AlgorithmSeriesResult(
            values=[result.value for result in results],
            time_s=[frame.time_s for frame in frames],
            unit=results[0].unit if results else self._output_unit(),
            channel=inputs.channel,
            windows=[{"start_s": frame.window_start_s, "end_s": frame.window_end_s} for frame in frames],
            quality=[result.quality for result in results],
            failures=[result.failure for result in results],
            warmups=[frame.warmup for frame in frames],
            point_evidence=[result.evidence for result in results],
            evidence={"definition_id": self.definition_id, "definition_version": self.definition.semver},
        )
