"""Generic runtime dispatch; algorithm-specific science stays in modules."""

from __future__ import annotations

from typing import Any

from .contracts import (
    AlgorithmEvidence,
    AlgorithmModule,
    AlgorithmResult,
    AlgorithmSeriesResult,
    AlgorithmStructuredResult,
    AlgorithmStructuredSeriesResult,
)
from .errors import UnsupportedAlgorithmModeError
from app.scientific.contracts import SampleRange


class AlgorithmRuntime:
    def __init__(self, registry) -> None:
        self.registry = registry

    def execute(
        self,
        *,
        algorithm_id: str,
        recording: Any,
        config: dict[str, Any],
        scientific_version: str | None = None,
    ) -> AlgorithmResult | AlgorithmSeriesResult | AlgorithmStructuredResult | AlgorithmStructuredSeriesResult:
        module: AlgorithmModule = self.registry.get(algorithm_id, scientific_version)
        return self.execute_module(module=module, recording=recording, config=config)

    def execute_module(
        self,
        *,
        module: AlgorithmModule,
        recording: Any,
        config: dict[str, Any],
    ) -> AlgorithmResult | AlgorithmSeriesResult | AlgorithmStructuredResult | AlgorithmStructuredSeriesResult:
        """Execute a concrete module selected by a trusted catalog boundary."""
        typed_config = self.validate_config(module=module, config=config)
        self.validate_parameter_schema(module=module, config=typed_config)
        inputs = module.resolve_inputs(recording, typed_config)
        if typed_config.mode == "static":
            result = module.execute_static(inputs, typed_config)
            result.evidence = self._with_sample_coordinate(
                result.evidence, result.actual_range, inputs.sfreq_hz,
            )
            result.evidence = AlgorithmEvidence.model_validate(result.evidence).model_dump(mode="json")
            return result
        result = module.execute_dynamic(inputs, typed_config)
        if isinstance(result, AlgorithmStructuredSeriesResult):
            result.evidence = AlgorithmEvidence.model_validate(
                self._with_sample_coordinate(result.evidence, result.actual_range, inputs.sfreq_hz)
            ).model_dump(mode="json")
            for window in result.windows:
                window.evidence = AlgorithmEvidence.model_validate(
                    self._with_sample_coordinate(
                        window.evidence,
                        {"start_s": window.start_s, "end_s": window.end_s},
                        inputs.sfreq_hz,
                    )
                ).model_dump(mode="json")
            return result
        # Keep the legacy warmup flags for historical readers, but make the
        # versioned state enum the canonical runtime representation. Older
        # modules may omit states, so only those results are derived from the
        # compatibility flags.
        if len(result.states) != len(result.values):
            result.states = [
                "Rejected" if failure is not None and quality == "gate_failed"
                else "Unavailable" if failure is not None
                else "Partial" if warmup
                else "Complete"
                for quality, failure, warmup in zip(result.quality, result.failures, result.warmups)
            ]
        result.point_evidence = [
            AlgorithmEvidence.model_validate(
                self._with_sample_coordinate(
                    item,
                    result.windows[index] if index < len(result.windows) else None,
                    inputs.sfreq_hz,
                )
            ).model_dump(mode="json")
            for index, item in enumerate(result.point_evidence)
        ]
        return result

    @staticmethod
    def _with_sample_coordinate(
        evidence: dict[str, Any], time_range: dict[str, float] | None, sfreq_hz: float,
    ) -> dict[str, Any]:
        """Attach the canonical scientific coordinate without changing display seconds."""
        copied = dict(evidence)
        extensions = dict(copied.get("extensions", {}))
        existing = extensions.get("sample_coordinate")
        if isinstance(existing, dict) and existing.get("coordinate_system") == "recording_relative_sample":
            copied["extensions"] = extensions
            return copied
        if time_range is None or "start_s" not in time_range or "end_s" not in time_range:
            coordinate: dict[str, Any] = {
                "coordinate_system": "recording_relative_seconds",
                "sample_range": None,
            }
        else:
            sample_range = SampleRange.from_seconds(
                float(time_range["start_s"]), float(time_range["end_s"]), float(sfreq_hz),
            )
            coordinate = {
                "coordinate_system": "recording_relative_sample",
                "sample_range": sample_range.as_dict(),
                "sfreq_hz": float(sfreq_hz),
            }
        extensions["sample_coordinate"] = coordinate
        copied["extensions"] = extensions
        return copied

    @staticmethod
    def validate_config(*, module: AlgorithmModule, config: dict[str, Any]):
        """Validate module-owned duration policy before work is queued or run."""
        typed_config = module.config_model.model_validate(config)
        if typed_config.mode not in module.manifest.supported_modes:
            raise UnsupportedAlgorithmModeError(
                f"algorithm {module.manifest.algorithm_id!r} does not support mode {typed_config.mode!r}",
                detail={"algorithm_id": module.manifest.algorithm_id, "mode": typed_config.mode},
            )
        if typed_config.mode == "static":
            return typed_config
        policy = module.manifest.dynamic_policy
        if typed_config.window_s not in policy.window_options_s:
            raise ValueError(
                f"{module.manifest.display_name_zh}动态分析窗口仅支持 "
                f"{', '.join(f'{value:g}' for value in policy.window_options_s)} 秒"
            )
        if typed_config.step_s != policy.refresh_step_s:
            raise ValueError(
                f"{module.manifest.display_name_zh}动态分析每 {policy.refresh_step_s:g} 秒更新"
            )
        return typed_config

    @staticmethod
    def validate_parameter_schema(*, module: AlgorithmModule, config: Any) -> None:
        values = config.model_dump(exclude_none=True)
        for parameter in module.parameter_schema().parameters:
            if parameter.key in values:
                try:
                    parameter.validate_value(values[parameter.key])
                except ValueError as exc:
                    raise ValueError(str(exc)) from exc
