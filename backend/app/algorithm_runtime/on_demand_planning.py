"""Sample-coordinate adapter for an explicitly declared dynamic window policy."""

import numpy as np

from app.algorithm_runtime.on_demand import DynamicWindowPlan
from app.algorithm_runtime.windows import latest_dynamic_analysis_frame_at_or_before
from app.scientific.contracts.types import SampleRange


def plan_policy_window(inputs, config, cursor_sample, session_identity, policy):
    """Use the existing full-Run policy, never invent future or local-filter windows."""
    if cursor_sample < 0:
        raise ValueError("cursor sample must be non-negative")
    cursor_s = cursor_sample / inputs.sfreq_hz
    frame = latest_dynamic_analysis_frame_at_or_before(
        config.start_s, config.end_s, duration_s=inputs.duration_s,
        window_s=config.window_s or policy.default_window_s,
        step_s=config.step_s or policy.refresh_step_s,
        cursor_s=cursor_s, minimum_window_s=policy.minimum_window_s,
        allow_warmup=policy.allow_warmup,
    )
    if frame is None:
        start_s = min(max(cursor_s, 0.0), inputs.duration_s)
        end_s = min(inputs.duration_s, start_s + 1.0 / inputs.sfreq_hz)
        if end_s <= start_s:
            start_s = max(0.0, inputs.duration_s - 1.0 / inputs.sfreq_hz)
            end_s = inputs.duration_s
        start_sample = int(np.floor(start_s * inputs.sfreq_hz))
        return DynamicWindowPlan(
            session_identity=session_identity, cursor_sample=cursor_sample, cursor_s=cursor_s,
            start_sample=start_sample,
            end_sample=max(int(np.ceil(end_s * inputs.sfreq_hz)), start_sample + 1),
            start_s=start_s, end_s=end_s, state="insufficient_data",
            reason=f"cursor has no policy-eligible {session_identity.algorithm_id.upper()} frame",
        )
    sample_range = SampleRange.from_seconds(frame.window_start_s, frame.window_end_s, inputs.sfreq_hz)
    return DynamicWindowPlan(
        session_identity=session_identity, cursor_sample=cursor_sample, cursor_s=cursor_s,
        start_sample=sample_range.start_sample, end_sample=sample_range.end_sample,
        start_s=frame.window_start_s, end_s=frame.window_end_s,
    )
