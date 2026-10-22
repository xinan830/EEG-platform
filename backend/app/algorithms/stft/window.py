"""STFT-owned single-window capability for the on-demand runtime."""

from __future__ import annotations

from collections.abc import Callable
from typing import Any

import numpy as np

from app.algorithm_runtime.contracts import AlgorithmInputs
from app.algorithm_runtime.on_demand import (
    DynamicSessionIdentity,
    DynamicWindowIdentity,
    DynamicWindowPlan,
    DynamicWindowResult,
)
from app.algorithm_runtime.on_demand_planning import plan_policy_window
from app.scientific.quality import SpectralQualityGateError

from .config import StftConfig
from .evidence import window_evidence, window_provenance
from .manifest import MANIFEST


class StftWindowCapability:
    """Plan and execute one STFT outer window without changing the STFT math."""

    window_policy_version = "stft-dynamic-window-v1"
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
        if check_cancelled is not None:
            check_cancelled()
        try:
            payload = inputs.payload.load_spectrogram(
                start_s=plan.start_s,
                window_s=plan.end_s - plan.start_s,
                channels=[inputs.channel],
                low_hz=config.low_hz,
                high_hz=config.high_hz,
                notch_hz=config.notch_hz,
                output_low_hz=config.low_hz,
                output_high_hz=config.high_hz,
            )
            if check_cancelled is not None:
                check_cancelled()
            times = np.asarray(payload["times_s"], dtype=float)
            frequencies = np.asarray(payload["frequencies_hz"], dtype=float)
            linear_uv = np.asarray(payload["power_linear"][inputs.channel], dtype=float)
            power_db = np.asarray(payload["power_db"][inputs.channel], dtype=float)
            if times.ndim != 1 or frequencies.ndim != 1 or times.size == 0 or frequencies.size == 0:
                raise ValueError("STFT 窗口没有有效的时频坐标")
            expected_shape = (times.size, frequencies.size)
            if linear_uv.shape != expected_shape or power_db.shape != expected_shape:
                raise ValueError("STFT 窗口矩阵 shape 与时频坐标不一致")
            linear = linear_uv * 1e-12
            if not np.isfinite(times).all() or not np.isfinite(frequencies).all() or not np.isfinite(linear).all() or not np.isfinite(power_db).all():
                raise ValueError("STFT 窗口包含非有限时频值")
            quality = dict(payload["quality"])
            if int(quality.get("bad_windows", 0)):
                raise SpectralQualityGateError(quality)
            if check_cancelled is not None:
                check_cancelled()
            expected_window = config.window_s or MANIFEST.dynamic_policy.default_window_s
            relative_times = times - float(plan.start_s)
            if not np.isfinite(relative_times).all() or np.any(np.diff(relative_times) <= 0):
                raise ValueError("STFT 窗口内层时间轴必须是严格递增的相对坐标")
            return DynamicWindowResult(
                identity=identity,
                state="ready",
                output_kind="time_frequency",
                axes={"time_center_s": relative_times, "frequency_hz": frequencies},
                axis_units={"time_center_s": "s", "frequency_hz": "Hz"},
                arrays={"power_linear": linear, "power_db": power_db},
                array_units={"power_linear": "V^2/Hz", "power_db": "dB re 1 uV^2/Hz"},
                quality="clean",
                evidence=window_evidence(payload, valid_count=int(times.size)),
                provenance=window_provenance(
                    payload,
                    start_s=plan.start_s,
                    end_s=plan.end_s,
                    source_channel=inputs.channel,
                    analysis_state=(
                        "Partial" if plan.end_s - plan.start_s < expected_window - 1e-9
                        else "Complete"
                    ),
                ),
            )
        except SpectralQualityGateError as exc:
            return DynamicWindowResult(
                identity=identity,
                state="rejected",
                output_kind="time_frequency",
                quality="gate_failed",
                evidence={
                    "source_quality": dict(exc.quality),
                    "spectral_evidence": dict(exc.quality.get("evidence", {})),
                    "calculation_trace": {"spectrogram_contract_version": "spectrogram-v2"},
                },
                failure={
                    "code": "STFT_WINDOW_QUALITY_GATE_FAILED",
                    "message": "当前动态窗口未通过 STFT 质量门",
                    "details": dict(exc.quality),
                },
            )
        except (KeyError, TypeError, ValueError) as exc:
            return DynamicWindowResult(
                identity=identity,
                state="unavailable",
                output_kind="time_frequency",
                quality="unavailable",
                failure={"code": "STFT_WINDOW_UNAVAILABLE", "message": str(exc)},
            )
