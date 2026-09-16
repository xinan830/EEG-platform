"""Generic runtime dispatch; algorithm-specific science stays in modules."""

from __future__ import annotations

from typing import Any

from .contracts import AlgorithmModule, AlgorithmResult, AlgorithmSeriesResult
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
        typed_config = module.config_model.model_validate(config)
        if typed_config.mode not in module.manifest.supported_modes:
            raise UnsupportedAlgorithmModeError(
                f"algorithm {algorithm_id!r} does not support mode {typed_config.mode!r}",
                detail={"algorithm_id": algorithm_id, "mode": typed_config.mode},
            )
        inputs = module.resolve_inputs(recording, typed_config)
        if typed_config.mode == "static":
            return module.execute_static(inputs, typed_config)
        return module.execute_dynamic(inputs, typed_config)
