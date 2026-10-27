"""Deterministic target-window replay for the public TNK group adapter."""

from __future__ import annotations

import hashlib
import json
import logging
import threading
import time
from collections import OrderedDict
from collections.abc import Callable
from copy import deepcopy
from typing import Any

import numpy as np

from app.algorithm_runtime.on_demand import (
    DynamicSessionIdentity,
    DynamicWindowIdentity,
    DynamicWindowPlan,
    DynamicWindowResult,
)
from app.algorithm_runtime.on_demand_planning import plan_policy_window
from app.algorithm_runtime.windows import build_dynamic_analysis_frames

logger = logging.getLogger("brain_platform.dynamic_window.tnk")
_MAX_CHECKPOINT_SESSIONS = 32
_MAX_CHECKPOINTS_PER_SESSION = 128
_EXECUTION_LOCK_COUNT = 64


class TnkGroupWindowCapability:
    """Replay group-owned state through the target using the full-run frame grid."""

    def __init__(self) -> None:
        self._checkpoint_lock = threading.RLock()
        # A fixed lock stripe set serializes restore/replay/save for one exact
        # session identity without retaining an unbounded lock per session.
        self._execution_locks = tuple(threading.RLock() for _ in range(_EXECUTION_LOCK_COUNT))
        self._checkpoints: OrderedDict[str, OrderedDict[int, tuple[Any, ...]]] = OrderedDict()

    @staticmethod
    def _checkpoint_key(session_identity: DynamicSessionIdentity) -> str:
        payload = json.dumps(
            session_identity.model_dump(mode="json"), sort_keys=True, separators=(",", ":"),
        ).encode("utf-8")
        return hashlib.sha256(payload).hexdigest()

    def _nearest_checkpoint(
        self, session_identity: DynamicSessionIdentity, before_index: int,
    ) -> tuple[int, tuple[Any, ...]] | None:
        key = self._checkpoint_key(session_identity)
        with self._checkpoint_lock:
            checkpoints = self._checkpoints.get(key)
            if checkpoints is None:
                return None
            eligible = [index for index in checkpoints if index < before_index]
            if not eligible:
                return None
            index = max(eligible)
            checkpoints.move_to_end(index)
            self._checkpoints.move_to_end(key)
            return index, deepcopy(checkpoints[index])

    def _save_checkpoint(
        self, session_identity: DynamicSessionIdentity, frame_index: int, children: tuple[Any, ...],
    ) -> None:
        key = self._checkpoint_key(session_identity)
        with self._checkpoint_lock:
            checkpoints = self._checkpoints.setdefault(key, OrderedDict())
            checkpoints[frame_index] = deepcopy(children)
            checkpoints.move_to_end(frame_index)
            while len(checkpoints) > _MAX_CHECKPOINTS_PER_SESSION:
                checkpoints.popitem(last=False)
            self._checkpoints.move_to_end(key)
            while len(self._checkpoints) > _MAX_CHECKPOINT_SESSIONS:
                self._checkpoints.popitem(last=False)

    def can_execute_window(self, config) -> bool:
        return isinstance(config, self.config_model) and config.mode == "dynamic"

    def plan_window(self, inputs, config, cursor_sample: int,
                    session_identity: DynamicSessionIdentity) -> DynamicWindowPlan:
        if not self.can_execute_window(config):
            raise ValueError("TNK group on-demand capability requires dynamic mode")
        plan = plan_policy_window(
            inputs, config, cursor_sample, session_identity, self.manifest.dynamic_policy,
        )
        if plan.state != "planned":
            return plan
        frames = self._replay_frames(inputs, config, plan.end_s)
        target_index = next((index for index, frame in enumerate(frames)
                             if self._same_range(frame, plan.start_s, plan.end_s, inputs.sfreq_hz)), None)
        if target_index is None:
            raise ValueError("TNK group target does not match its canonical dynamic frame grid")
        reset_sample = int(np.floor(config.start_s * inputs.sfreq_hz))
        return plan.model_copy(update={
            "context_start_sample": reset_sample,
            "context_end_sample": plan.end_sample,
            "replay_window_count": target_index + 1,
        })

    def execute_window(
        self, inputs, config, plan: DynamicWindowPlan, identity: DynamicWindowIdentity,
        check_cancelled: Callable[[], None] | None = None,
    ) -> DynamicWindowResult:
        return self.execute_window_with_replayed_frames(
            inputs, config, plan, identity, on_frame_result=None,
            check_cancelled=check_cancelled,
        )

    def execute_window_with_replayed_frames(
        self, inputs, config, plan: DynamicWindowPlan, identity: DynamicWindowIdentity,
        *, on_frame_result: Callable[[DynamicWindowIdentity, DynamicWindowResult], None] | None,
        check_cancelled: Callable[[], None] | None = None,
    ) -> DynamicWindowResult:
        """Execute the target and optionally expose each replayed frame in order."""
        key = self._checkpoint_key(plan.session_identity)
        lock = self._execution_locks[int(key[:8], 16) % len(self._execution_locks)]
        with lock:
            if check_cancelled:
                check_cancelled()
            return self._execute_window_locked(
                inputs, config, plan, identity, check_cancelled, on_frame_result,
            )

    def _execute_window_locked(
        self, inputs, config, plan: DynamicWindowPlan, identity: DynamicWindowIdentity,
        check_cancelled: Callable[[], None] | None,
        on_frame_result: Callable[[DynamicWindowIdentity, DynamicWindowResult], None] | None,
    ) -> DynamicWindowResult:
        if plan.state != "planned":
            return DynamicWindowResult(
                identity=identity, state="insufficient_data", output_kind="algorithm_group",
                quality="insufficient_data", failure={
                    "code": "TNK_WINDOW_INSUFFICIENT_DATA",
                    "message": plan.reason or "TNK 目标游标尚无符合策略的分析窗口",
                },
            )
        if check_cancelled:
            check_cancelled()
        started = time.perf_counter()
        frames = self._replay_frames(inputs, config, plan.end_s)
        target_index = next((index for index, frame in enumerate(frames)
                             if self._same_range(frame, plan.start_s, plan.end_s, inputs.sfreq_hz)), None)
        if target_index is None or target_index + 1 != plan.replay_window_count:
            raise ValueError("TNK group replay plan no longer matches the canonical target context")

        checkpoint = self._nearest_checkpoint(plan.session_identity, target_index)
        if checkpoint is None:
            children = self._fresh_children()
            for child in children:
                if hasattr(child.module, "reset"):
                    child.module.reset()
            first_frame_index = 0
            checkpoint_frame_index = None
        else:
            checkpoint_frame_index, children = checkpoint
            first_frame_index = checkpoint_frame_index + 1
        target_result = None
        diagnostics: dict[str, int] = {}
        for frame_index in range(first_frame_index, target_index + 1):
            if check_cancelled:
                check_cancelled()
            frame = frames[frame_index]
            frame_results = self._execute_children(
                inputs, config, frame.window_start_s, frame.window_end_s,
                reset_state=False, children=children, diagnostics=diagnostics, dynamic_frame=True,
            )
            self._save_checkpoint(plan.session_identity, frame_index, children)
            frame_identity = identity if frame_index == target_index else DynamicWindowIdentity(
                **plan.session_identity.model_dump(),
                generation=identity.generation,
                start_sample=int(round(frame.window_start_s * inputs.sfreq_hz)),
                end_sample=int(round(frame.window_end_s * inputs.sfreq_hz)),
            )
            frame_result = self._build_frame_result(
                config, frame_identity, frame_results, replayed_window_count=frame_index + 1,
            )
            if on_frame_result is not None:
                on_frame_result(frame_identity, frame_result.model_copy(deep=True))
            if frame_index == target_index:
                target_result = frame_result
        if check_cancelled:
            check_cancelled()
        actual_replayed_frames = target_index + 1 if checkpoint_frame_index is None else target_index - checkpoint_frame_index
        logger.info(
            "tnk_on_demand_execution target_frame=%s checkpoint_frame=%s replayed_frames=%s "
            "spectrum_loads=%s spectrum_cache_hits=%s child_calculations=%s elapsed_ms=%.1f",
            target_index, checkpoint_frame_index, actual_replayed_frames,
            diagnostics.get("spectrum_loads", 0), diagnostics.get("spectrum_cache_hits", 0),
            diagnostics.get("child_calculations", 0), (time.perf_counter() - started) * 1000,
        )
        if target_result is None:
            raise ValueError("TNK group replay produced no target result")

        return target_result

    def _build_frame_result(
        self, config, identity: DynamicWindowIdentity, child_results: dict[str, list[Any]],
        *, replayed_window_count: int,
    ) -> DynamicWindowResult:
        values, evidence = self._metric_values(child_results)
        arrays = {
            name: np.asarray([np.nan if value is None else value], dtype=float)
            for name, value in values.items()
        }
        quality = self._quality(child_results)
        return DynamicWindowResult(
            identity=identity,
            state="ready",
            output_kind="algorithm_group",
            axes={"metric_index": np.arange(len(self._metric_units), dtype=float)},
            axis_units={"metric_index": "index"},
            arrays=arrays,
            array_units=dict(self._metric_units),
            quality=quality,
            evidence=self._point_evidence(config, evidence, values),
            provenance={
                "preprocessing_scope": "configured_continuous_before_window_slice",
                "analysis_state": self._analysis_state(quality),
                "role_mapping": dict(config.role_mapping),
                "replayed_window_count": replayed_window_count,
            },
        )

    def _replay_frames(self, inputs, config, end_s: float):
        return build_dynamic_analysis_frames(
            config.start_s,
            min(config.end_s, end_s),
            duration_s=inputs.duration_s,
            window_s=4.0,
            step_s=1.0,
            minimum_window_s=self.manifest.dynamic_policy.minimum_window_s,
            allow_warmup=self.manifest.dynamic_policy.allow_warmup,
        )

    @staticmethod
    def _same_range(frame, start_s: float, end_s: float, sfreq_hz: float) -> bool:
        return (
            int(round(frame.window_start_s * sfreq_hz)) == int(round(start_s * sfreq_hz))
            and int(round(frame.window_end_s * sfreq_hz)) == int(round(end_s * sfreq_hz))
        )
