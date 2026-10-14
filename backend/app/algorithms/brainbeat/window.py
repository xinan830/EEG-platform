"""Brainbeat-owned single-window capability.

Brainbeat is a two-source spectral metric: every window loads the explicit
Fz/Pz pair and computes its own IAPF-driven ratio.  It must not inherit the
one-channel scalar adapter or carry state from a previous window.
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

from .compute import compute_brainbeat
from .config import BrainbeatConfig
from .manifest import MANIFEST


class BrainbeatWindowCapability:
    """Plan and execute one independent Fz/Pz Brainbeat window."""

    window_policy_version = "brainbeat-dynamic-window-v1"
    manifest = MANIFEST
    config_model = BrainbeatConfig

    def can_execute_window(self, config: BrainbeatConfig) -> bool:
        return isinstance(config, BrainbeatConfig) and config.mode == "dynamic"

    def plan_window(
        self,
        inputs: AlgorithmInputs,
        config: BrainbeatConfig,
        cursor_sample: int,
        session_identity: DynamicSessionIdentity,
    ) -> DynamicWindowPlan:
        if not self.can_execute_window(config):
            raise ValueError("Brainbeat on-demand capability requires dynamic mode")
        return plan_policy_window(inputs, config, cursor_sample, session_identity, MANIFEST.dynamic_policy)

    def execute_window(
        self,
        inputs: AlgorithmInputs,
        config: BrainbeatConfig,
        plan: DynamicWindowPlan,
        identity: DynamicWindowIdentity,
        check_cancelled: Callable[[], None] | None = None,
    ) -> DynamicWindowResult:
        if plan.state != "planned":
            return DynamicWindowResult(
                identity=identity,
                state="insufficient_data",
                output_kind="scalar_series",
                quality="insufficient_data",
                failure={
                    "code": "BRAINBEAT_WINDOW_INSUFFICIENT_DATA",
                    "message": plan.reason or "window unavailable",
                },
            )
        if check_cancelled is not None:
            check_cancelled()
        try:
            spectrum = inputs.payload.load_spectrum(
                start_s=plan.start_s,
                window_s=plan.end_s - plan.start_s,
                channels=[inputs.channel, config.pz_channel],
            )
            if check_cancelled is not None:
                check_cancelled()
            result = compute_brainbeat(
                spectrum,
                fz_channel=inputs.channel,
                pz_channel=config.pz_channel,
                requested_range={"start_s": plan.start_s, "end_s": plan.end_s},
                actual_range={"start_s": plan.start_s, "end_s": plan.end_s},
            )
        except SpectralQualityGateError as exc:
            quality = dict(exc.quality)
            result = _quality_failure(inputs, config, plan, quality)
        if check_cancelled is not None:
            check_cancelled()

        state = "ready" if result.failure is None else (
            "rejected" if result.quality == "gate_failed" else "unavailable"
        )
        failure = None if result.failure is None else {
            "code": result.failure.code,
            "message": result.failure.message,
            "details": result.failure.detail or {},
        }
        numeric = float(result.value) if state == "ready" and result.value is not None else np.nan
        return DynamicWindowResult(
            identity=identity,
            state=state,
            output_kind="scalar_series",
            axes={"time_s": np.asarray([plan.end_s], dtype=float)},
            axis_units={"time_s": "s"},
            arrays={"brainbeat": np.asarray([numeric], dtype=float)},
            array_units={"brainbeat": "ratio"},
            quality=result.quality,
            failure=failure,
            evidence=result.evidence,
            provenance={
                "preprocessing_scope": "whole_recording_before_window_slice",
                "analysis_state": "Partial" if plan.end_s - plan.start_s < (
                    config.window_s or MANIFEST.dynamic_policy.default_window_s
                ) - 1e-9 else "Complete",
                "paired_sources": True,
                "source_channels": {"fz": inputs.channel, "pz": config.pz_channel},
                "independent_windows": True,
                "cross_window_ema": False,
                "iapf_dependency": True,
            },
        )


def _quality_failure(inputs: AlgorithmInputs, config: BrainbeatConfig,
                    plan: DynamicWindowPlan, quality: dict[str, object]) -> AlgorithmResult:
    return AlgorithmResult(
        value=None,
        unit="ratio",
        channel=f"{inputs.channel}/{config.pz_channel}",
        requested_range={"start_s": plan.start_s, "end_s": plan.end_s},
        actual_range={"start_s": plan.start_s, "end_s": plan.end_s},
        quality="gate_failed",
        failure=AlgorithmFailure(
            code="PSD_QUALITY_GATE_FAILED",
            message="当前 Brainbeat 动态窗口未通过 PSD 质量门",
            detail=quality,
        ),
        evidence={
            "source_quality": quality,
            "spectral_evidence": dict(quality.get("evidence", {})),
        },
    )
