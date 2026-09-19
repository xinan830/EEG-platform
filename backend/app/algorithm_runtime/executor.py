"""Generic runtime dispatch; algorithm-specific science stays in modules."""

from __future__ import annotations

from typing import Any

from .contracts import AlgorithmEvidence, AlgorithmModule, AlgorithmResult, AlgorithmSeriesResult
from .errors import UnsupportedAlgorithmModeError


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
    ) -> AlgorithmResult | AlgorithmSeriesResult:
        module: AlgorithmModule = self.registry.get(algorithm_id, scientific_version)
        return self.execute_module(module=module, recording=recording, config=config)

    def execute_module(
        self,
        *,
        module: AlgorithmModule,
        recording: Any,
        config: dict[str, Any],
    ) -> AlgorithmResult | AlgorithmSeriesResult:
        """Execute a concrete module selected by a trusted catalog boundary."""
        typed_config = self.validate_config(module=module, config=config)
        inputs = module.resolve_inputs(recording, typed_config)
        if typed_config.mode == "static":
            result = module.execute_static(inputs, typed_config)
            result.evidence = AlgorithmEvidence.model_validate(result.evidence).model_dump(mode="json")
            return result
        result = module.execute_dynamic(inputs, typed_config)
        result.point_evidence = [
            AlgorithmEvidence.model_validate(item).model_dump(mode="json")
            for item in result.point_evidence
        ]
        return result

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
