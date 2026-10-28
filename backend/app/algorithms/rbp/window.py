"""RBP-owned single-window adaptation of the frozen five-band contract."""

from collections.abc import Callable

import numpy as np

from app.algorithm_runtime.contracts import AlgorithmInputs
from app.algorithm_runtime.on_demand import (
    DynamicSessionIdentity, DynamicWindowIdentity, DynamicWindowPlan, DynamicWindowResult,
)
from app.algorithm_runtime.on_demand_planning import plan_policy_window
from app.scientific.quality import SpectralQualityGateError

from .config import RbpConfig
from .official import RBP_BANDS, RBP_FILTER_LOW_HZ, RBP_FILTER_HIGH_HZ, RBP_NOTCH_HZ, five_band_shares


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
                output_kind="algorithm_group", quality="insufficient_data",
                failure={"code": "RBP_WINDOW_INSUFFICIENT_DATA", "message": plan.reason or "window unavailable"})
        if check_cancelled is not None:
            check_cancelled()
        return self._execute_multi_window(inputs, config, plan, identity, check_cancelled)

    def _execute_multi_window(self, inputs: AlgorithmInputs, config: RbpConfig,
                              plan: DynamicWindowPlan, identity: DynamicWindowIdentity,
                              check_cancelled: Callable[[], None] | None) -> DynamicWindowResult:
        if check_cancelled is not None:
            check_cancelled()
        try:
            spectrum = inputs.payload.load_spectrum(start_s=plan.start_s, window_s=plan.end_s - plan.start_s,
                channels=list(inputs.channels), filter_low_hz=RBP_FILTER_LOW_HZ, filter_high_hz=RBP_FILTER_HIGH_HZ,
                output_low_hz=RBP_FILTER_LOW_HZ, output_high_hz=RBP_FILTER_HIGH_HZ, notch_hz=RBP_NOTCH_HZ)
        except SpectralQualityGateError as exc:
            return DynamicWindowResult(identity=identity, state="rejected", output_kind="algorithm_group",
                quality="gate_failed", failure={"code": "PSD_QUALITY_GATE_FAILED", "message": "当前窗口未通过 PSD 质量门", "details": exc.quality},
                evidence={"extensions": {"quality_gate": exc.quality}})
        arrays = {name: [] for name, *_ in RBP_BANDS}
        failures = {}
        for index, channel in enumerate(inputs.channels):
            if spectrum.gate_failed:
                failures[channel] = {"code": "PSD_QUALITY_GATE_FAILED", "message": "当前窗口未通过 PSD 质量门",
                                     "details": {"reason": spectrum.gate_failed}}
                shares = {name: np.nan for name, *_ in RBP_BANDS}
                for name in arrays:
                    arrays[name].append(shares[name])
                continue
            try:
                _, shares = five_band_shares(type("ChannelSpectrum", (), {
                    "freqs": spectrum.freqs, "psd": spectrum.psd[index:index + 1]})())
            except ValueError as exc:
                failures[channel] = {"code": "RBP_CHANNEL_UNAVAILABLE", "message": str(exc)}
                shares = {name: np.nan for name, *_ in RBP_BANDS}
            for name in arrays:
                arrays[name].append(shares[name])
        if check_cancelled is not None:
            check_cancelled()
        all_failed = len(failures) == len(inputs.channels)
        gate_failed = any(item.get("code") == "PSD_QUALITY_GATE_FAILED" for item in failures.values())
        state = ("rejected" if gate_failed else "unavailable") if all_failed else "ready"
        return DynamicWindowResult(identity=identity, state=state, output_kind="algorithm_group",
            axes={"time_s": np.array([plan.end_s]), "channel_index": np.arange(len(inputs.channels), dtype=float)},
            axis_units={"time_s": "s", "channel_index": "index"},
            arrays={name: np.asarray(values, dtype=float) for name, values in arrays.items()},
            array_units={name: "ratio" for name, *_ in RBP_BANDS},
            quality="gate_failed" if gate_failed and all_failed else "unavailable" if all_failed else "partial" if failures else "clean",
            failure={"code": "PSD_QUALITY_GATE_FAILED" if gate_failed else "RBP_ALL_CHANNELS_UNAVAILABLE",
                     "message": "当前窗口未通过 PSD 质量门" if gate_failed else "所有选定导联均不可用",
                     "details": {"channels": failures}} if all_failed else None,
            evidence={"extensions": {"channel_failures": {"by_channel": failures}, "channel_order": {"channels": list(inputs.channels)}}},
            provenance={"preprocessing_scope": "whole_recording_before_window_slice",
                        "analysis_state": "Partial" if plan.end_s - plan.start_s < config.window_s - 1e-9 else "Complete",
                        "filter_bandpass_hz": [RBP_FILTER_LOW_HZ, RBP_FILTER_HIGH_HZ],
                        "notch_hz": RBP_NOTCH_HZ, "notch_quality_factor": 30.0})
