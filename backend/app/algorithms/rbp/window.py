"""RBP-owned single-window adaptation of the frozen five-band contract."""

from collections.abc import Callable

import numpy as np

from app.algorithm_runtime.contracts import AlgorithmInputs
from app.algorithm_runtime.on_demand import (
    DynamicSessionIdentity, DynamicWindowIdentity, DynamicWindowPlan, DynamicWindowResult,
)
from app.algorithm_runtime.on_demand_planning import plan_policy_window

from .config import RbpConfig
from .official import RBP_BANDS, RBP_FILTER_LOW_HZ, RBP_FILTER_HIGH_HZ, RBP_NOTCH_HZ


class RbpWindowCapability:
    window_policy_version = "rbp-dynamic-window-v1"

    def can_execute_window(self, config) -> bool:
        return isinstance(config, RbpConfig) and config.mode == "dynamic"

    def plan_window(self, inputs: AlgorithmInputs, config: RbpConfig, cursor_sample: int,
                    session_identity: DynamicSessionIdentity) -> DynamicWindowPlan:
        if not self.can_execute_window(config):
            raise ValueError("RBP on-demand capability requires dynamic mode")
        return plan_policy_window(inputs, config, cursor_sample, session_identity, self.manifest.dynamic_policy)

    def execute_window(self, inputs: AlgorithmInputs, config: RbpConfig, plan: DynamicWindowPlan,
                       identity: DynamicWindowIdentity,
                       check_cancelled: Callable[[], None] | None = None) -> DynamicWindowResult:
        if plan.state != "planned":
            return DynamicWindowResult(identity=identity, state="insufficient_data",
                output_kind="multi_metric_series", quality="insufficient_data",
                failure={"code": "RBP_WINDOW_INSUFFICIENT_DATA", "message": plan.reason or "window unavailable"})
        if check_cancelled is not None:
            check_cancelled()
        result = self._execute_frame(inputs, plan.start_s, plan.end_s)
        if check_cancelled is not None:
            check_cancelled()
        state = "ready" if result.failure is None else "rejected" if result.quality == "gate_failed" else "unavailable"
        return DynamicWindowResult(
            identity=identity, state=state, output_kind="multi_metric_series",
            axes={"time_s": np.array([plan.end_s])}, axis_units={"time_s": "s"},
            arrays={name: np.array([result.output_values[name] if state == "ready" else np.nan])
                    for name, *_ in RBP_BANDS},
            array_units={name: "ratio" for name, *_ in RBP_BANDS}, quality=result.quality,
            failure=None if result.failure is None else {
                "code": result.failure.code, "message": result.failure.message, "details": result.failure.detail},
            evidence=result.evidence,
            provenance={"preprocessing_scope": "whole_recording_before_window_slice",
                        "analysis_state": "Partial" if plan.end_s - plan.start_s < config.window_s - 1e-9 else "Complete",
                        "filter_bandpass_hz": [RBP_FILTER_LOW_HZ, RBP_FILTER_HIGH_HZ],
                        "notch_hz": RBP_NOTCH_HZ, "notch_quality_factor": 30.0},
        )
