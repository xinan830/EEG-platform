"""Map standalone RBP's shared frame outcome into the public window contract."""

from collections.abc import Callable

import numpy as np

from app.algorithm_runtime.contracts import AlgorithmInputs
from app.algorithm_runtime.on_demand import (
    DynamicSessionIdentity, DynamicWindowIdentity, DynamicWindowPlan, DynamicWindowResult,
)
from app.algorithm_runtime.on_demand_planning import plan_policy_window

from .config import RbpConfig
from .frame import execute_rbp_frame
from .official import RBP_BANDS


class RbpWindowCapability:
    window_policy_version = "rbp-dynamic-window-v3-channel-quality"

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
        if check_cancelled:
            check_cancelled()
        if plan.state != "planned":
            return DynamicWindowResult(identity=identity, state="insufficient_data",
                output_kind="algorithm_group", quality="insufficient_data",
                failure={"code": "RBP_WINDOW_INSUFFICIENT_DATA", "message": plan.reason or "window unavailable"})
        frame = execute_rbp_frame(inputs, plan.start_s, plan.end_s, check_cancelled)
        configured_window_s = config.window_s or self.manifest.dynamic_policy.default_window_s
        return DynamicWindowResult(
            identity=identity, state=frame.state, output_kind="algorithm_group",
            axes={"time_s": np.asarray([plan.end_s]),
                  "channel_index": np.arange(len(inputs.channels), dtype=float)},
            axis_units={"time_s": "s", "channel_index": "index"},
            arrays=frame.arrays, array_units={name: "ratio" for name, *_ in RBP_BANDS},
            quality=frame.quality,
            failure={"code": frame.failure.code, "message": frame.failure.message,
                     "details": frame.failure.detail} if frame.failure else None,
            evidence=frame.evidence,
            provenance={**frame.provenance,
                        "analysis_state": "Partial" if plan.end_s - plan.start_s < configured_window_s - 1e-9 else "Complete"},
        )
