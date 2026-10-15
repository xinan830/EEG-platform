"""Shared window plumbing for scalar spectral algorithms.

This module owns only policy planning and the scalar-to-window result envelope.
The algorithm module supplies ``_execute_frame`` with its existing numerical
implementation, so a window migration cannot silently create a second formula.
"""

from collections.abc import Callable

import numpy as np

from app.algorithm_runtime.contracts import AlgorithmFailure, AlgorithmInputs, AlgorithmResult
from app.algorithm_runtime.on_demand import (
    DynamicSessionIdentity,
    DynamicWindowIdentity,
    DynamicWindowPlan,
    DynamicWindowResult,
)
from app.algorithm_runtime.on_demand_planning import plan_policy_window
from app.scientific.quality import SpectralQualityGateError


class ScalarDynamicWindowCapability:
    """Adapter for one scalar output whose existing frame calculation is pure."""

    window_policy_version = "scalar-dynamic-window-v1"

    def can_execute_window(self, config) -> bool:
        return isinstance(config, self.config_model) and config.mode == "dynamic"

    def plan_window(self, inputs: AlgorithmInputs, config, cursor_sample: int,
                    session_identity: DynamicSessionIdentity) -> DynamicWindowPlan:
        if not self.can_execute_window(config):
            raise ValueError("scalar on-demand capability requires dynamic mode")
        return plan_policy_window(inputs, config, cursor_sample, session_identity,
                                  self.manifest.dynamic_policy)

    def execute_window(self, inputs: AlgorithmInputs, config, plan: DynamicWindowPlan,
                       identity: DynamicWindowIdentity,
                       check_cancelled: Callable[[], None] | None = None) -> DynamicWindowResult:
        field = self.manifest.output_schema["fields"][0]
        name, unit = field["name"], field["unit"]
        if check_cancelled is not None:
            check_cancelled()
        if plan.state != "planned":
            return DynamicWindowResult(
                identity=identity, state=plan.state, output_kind="scalar_series",
                quality=plan.state, failure={"code": "WINDOW_INSUFFICIENT_DATA" if
                plan.state == "insufficient_data" else "WINDOW_UNAVAILABLE",
                "message": plan.reason or "window unavailable"},
            )
        try:
            result: AlgorithmResult = self._execute_frame(inputs, config, plan.start_s, plan.end_s)
        except SpectralQualityGateError as exc:
            quality = dict(exc.quality)
            actual_range = {"start_s": plan.start_s, "end_s": plan.end_s}
            result = AlgorithmResult(
                value=None, unit=unit, channel=inputs.channel,
                requested_range=actual_range, actual_range=actual_range, quality="gate_failed",
                failure=AlgorithmFailure(code="PSD_QUALITY_GATE_FAILED",
                    message="当前动态窗口未通过 PSD 质量门", detail=quality),
                evidence={"source_quality": {key: quality[key] for key in (
                    "clean_segments", "total_segments", "clean_ratio", "gate_failed", "rejected_reasons",
                ) if key in quality}, "spectral_evidence": dict(quality.get("evidence", {}))},
            )
        if check_cancelled is not None:
            check_cancelled()
        if result.unit != unit:
            raise ValueError("scalar frame unit does not match its module manifest")
        if result.failure is None:
            state, quality = "ready", result.quality
            failure = None
        else:
            state = "rejected" if result.quality == "gate_failed" else "unavailable"
            quality = result.quality
            failure = {"code": result.failure.code, "message": result.failure.message,
                       "details": result.failure.detail or {}}
        return self._result(identity, plan, config, name, unit, result.value, state, quality,
                            failure, evidence=result.evidence)

    def _result(self, identity, plan, config, name: str, unit: str, value: float | None,
                state: str, quality: str, failure: dict | None,
                evidence: dict | None = None) -> DynamicWindowResult:
        numeric = float(value) if state == "ready" and value is not None else np.nan
        return DynamicWindowResult(
            identity=identity, state=state, output_kind="scalar_series",
            axes={"time_s": np.asarray([plan.end_s], dtype=float)},
            axis_units={"time_s": "s"}, arrays={name: np.asarray([numeric], dtype=float)},
            array_units={name: unit}, quality=quality, failure=failure,
            evidence=evidence or {},
            provenance={"preprocessing_scope": self.window_preprocessing_scope,
                        "analysis_state": "Partial" if plan.end_s - plan.start_s <
                        (config.window_s or self.manifest.dynamic_policy.default_window_s) - 1e-9
                        else "Complete"},
        )
