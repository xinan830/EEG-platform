"""STFT-owned single-window capability for the on-demand runtime."""

from __future__ import annotations

from collections.abc import Callable
from typing import Any

from app.algorithm_runtime.contracts import AlgorithmInputs
from app.algorithm_runtime.on_demand import (
    DynamicSessionIdentity,
    DynamicWindowIdentity,
    DynamicWindowPlan,
    DynamicWindowResult,
)
from app.algorithm_runtime.on_demand_planning import plan_policy_window
from .config import StftConfig
from .frame import execute_stft_frame
from .manifest import MANIFEST


class StftWindowCapability:
    """Plan and execute one STFT outer window without changing the STFT math."""

    window_policy_version = "stft-dynamic-window-v3-bounded"
    manifest = MANIFEST
    config_model = StftConfig

    def can_execute_window(self, config: StftConfig) -> bool:
        return isinstance(config, StftConfig) and config.mode == "dynamic"

    def plan_window(
        self,
        inputs: AlgorithmInputs,
        config: StftConfig,
        cursor_sample: int,
        session_identity: DynamicSessionIdentity,
    ) -> DynamicWindowPlan:
        if not self.can_execute_window(config):
            raise ValueError("STFT on-demand capability requires dynamic mode")
        return plan_policy_window(inputs, config, cursor_sample, session_identity, MANIFEST.dynamic_policy)

    def execute_window(
        self,
        inputs: AlgorithmInputs,
        config: StftConfig,
        plan: DynamicWindowPlan,
        identity: DynamicWindowIdentity,
        check_cancelled: Callable[[], None] | None = None,
    ) -> DynamicWindowResult:
        if plan.state != "planned":
            return DynamicWindowResult(
                identity=identity,
                state="insufficient_data",
                output_kind="time_frequency",
                quality="insufficient_data",
                failure={
                    "code": "STFT_WINDOW_INSUFFICIENT_DATA",
                    "message": plan.reason or "window unavailable",
                },
            )
        frame = execute_stft_frame(inputs, config, plan.start_s, plan.end_s, check_cancelled)
        failure = frame.window_failure
        if failure is not None:
            return DynamicWindowResult(
                identity=identity, state=frame.window_state, output_kind="time_frequency",
                quality="unavailable" if frame.window_state == "unavailable" else "gate_failed",
                failure={"code": failure.code, "message": failure.message, "details": failure.detail},
                evidence=frame.evidence, provenance=frame.provenance,
            )
        return DynamicWindowResult(
            identity=identity, state="ready", output_kind="time_frequency",
            axes={"time_center_s": frame.times_s - frame.actual_range["start_s"],
                  "frequency_hz": frame.frequencies_hz},
            axis_units={"time_center_s": "s", "frequency_hz": "Hz"},
            arrays=frame.arrays,
            array_units={"power_linear": "V^2/Hz", "power_db": "dB re 1 uV^2/Hz"},
            quality="clean", evidence=frame.evidence, provenance=frame.provenance,
        )
