"""FAA-owned single-window capability.

FAA cannot use the scalar spectral adapter: its scientific contract requires
paired F3/F4 sources, continuous preprocessing over the recording, and then
2-second paired epochs inside the requested window.
"""

from collections.abc import Callable

import numpy as np

from app.algorithm_runtime.contracts import AlgorithmFailure, AlgorithmInputs
from app.algorithm_runtime.on_demand import (
    DynamicSessionIdentity,
    DynamicWindowIdentity,
    DynamicWindowPlan,
    DynamicWindowResult,
)
from app.algorithm_runtime.on_demand_planning import plan_policy_window

from .config import FaaConfig
from .manifest import MANIFEST
from .official import compute_faa


class FaaWindowCapability:
    """Plan and execute one FAA window without changing the full Run path."""

    window_policy_version = "faa-dynamic-window-v1"
    manifest = MANIFEST
    config_model = FaaConfig

    def can_execute_window(self, config: FaaConfig) -> bool:
        return isinstance(config, FaaConfig) and config.mode == "dynamic"

    def plan_window(self, inputs: AlgorithmInputs, config: FaaConfig,
                    cursor_sample: int,
                    session_identity: DynamicSessionIdentity) -> DynamicWindowPlan:
        if not self.can_execute_window(config):
            raise ValueError("FAA on-demand capability requires dynamic mode")
        return plan_policy_window(inputs, config, cursor_sample, session_identity,
                                  self.manifest.dynamic_policy)

    def execute_window(self, inputs: AlgorithmInputs, config: FaaConfig,
                       plan: DynamicWindowPlan,
                       identity: DynamicWindowIdentity,
                       check_cancelled: Callable[[], None] | None = None
                       ) -> DynamicWindowResult:
        if plan.state != "planned":
            return DynamicWindowResult(
                identity=identity,
                state="insufficient_data",
                output_kind="scalar_series",
                quality="insufficient_data",
                failure={
                    "code": "FAA_WINDOW_INSUFFICIENT_DATA",
                    "message": plan.reason or "window unavailable",
                },
            )
        if check_cancelled is not None:
            check_cancelled()
        try:
            f3, f4, sfreq, source = inputs.payload.load_faa_signals(
                start_s=plan.start_s,
                end_s=plan.end_s,
                f3_channel=inputs.channel,
                f4_channel=config.f4_channel,
                low_hz=config.low_hz,
                high_hz=config.high_hz,
                notch_hz=config.notch_hz,
            )
            if check_cancelled is not None:
                check_cancelled()
            if inputs.execution_context is not None:
                inputs.execution_context.source_preparation_completed()
            report = compute_faa(f3, f4, sfreq)
            if check_cancelled is not None:
                check_cancelled()
            quality = {
                "clean_segments": report["clean_epochs"],
                "total_segments": report["total_epochs"],
                "clean_ratio": report["clean_ratio"],
                "gate_failed": report["reason"] or None,
                "rejected_reasons": [report["reason"]] if report["reason"] else [],
            }
            evidence = {
                "source_quality": quality,
                "extensions": {"faa_evidence": {
                    **source,
                    **report,
                    "source_channels": {
                        "left": source["channels"][0],
                        "right": source["channels"][1],
                    },
                    "formula": "ln(P_right_alpha) - ln(P_left_alpha)",
                }},
                "calculation_trace": {
                    "formula": "ln(P_right_alpha) - ln(P_left_alpha)",
                    "inputs": [
                        {"label": f"左来源 {source['channels'][0]} Alpha 功率",
                         "value": report["p_f3"] * 1e12 if report["p_f3"] is not None else None,
                         "unit": "uV^2"},
                        {"label": f"右来源 {source['channels'][1]} Alpha 功率",
                         "value": report["p_f4"] * 1e12 if report["p_f4"] is not None else None,
                         "unit": "uV^2"},
                    ],
                },
            }
            if report["faa"] is None:
                failure = AlgorithmFailure(
                    code="FAA_QUALITY_GATE_FAILED",
                    message="当前范围未达到 FAA 成对 epoch 质量要求",
                    detail={"reason": report["reason"], **quality, "evidence": evidence},
                )
                return DynamicWindowResult(
                    identity=identity,
                    state="rejected",
                    output_kind="scalar_series",
                    axes={"time_s": np.asarray([plan.end_s], dtype=float)},
                    axis_units={"time_s": "s"},
                    arrays={"faa": np.asarray([np.nan], dtype=float)},
                    array_units={"faa": "dimensionless"},
                    quality="gate_failed",
                    failure={"code": failure.code, "message": failure.message,
                             "details": failure.detail},
                    evidence=evidence,
                    provenance=self._provenance(plan, config),
                )
            return DynamicWindowResult(
                identity=identity,
                state="ready",
                output_kind="scalar_series",
                axes={"time_s": np.asarray([plan.end_s], dtype=float)},
                axis_units={"time_s": "s"},
                arrays={"faa": np.asarray([float(report["faa"])], dtype=float)},
                array_units={"faa": "dimensionless"},
                quality="clean",
                evidence=evidence,
                provenance=self._provenance(plan, config),
            )
        except (ValueError, OSError) as exc:
            return DynamicWindowResult(
                identity=identity,
                state="unavailable",
                output_kind="scalar_series",
                quality="unavailable",
                failure={"code": "FAA_WINDOW_UNAVAILABLE", "message": str(exc)},
            )

    @staticmethod
    def _provenance(plan: DynamicWindowPlan, config: FaaConfig) -> dict[str, object]:
        return {
            "preprocessing_scope": "whole_recording_before_window_slice",
            "analysis_state": "Partial" if plan.end_s - plan.start_s <
            (config.window_s or MANIFEST.dynamic_policy.default_window_s) - 1e-9
            else "Complete",
            "paired_sources": True,
            "epoch_s": 2.0,
            "epoch_step_s": 1.0,
            "minimum_clean_epochs": 10,
        }
