"""Read-only TNK workbench summary projection."""

from __future__ import annotations

from typing import Any

from app.algorithms.tnk.workbench_summary import score_state_summary


class TnkWorkbenchUnavailableError(ValueError):
    """Raised when a Run cannot provide a completed TNK biofeedback workbench summary."""


def build_tnk_workbench_summary(run_id: str, run: Any, cursor_s: float) -> dict[str, object]:
    """Project a completed TNK Run summary at ``cursor_s`` without exposing algorithm internals to HTTP."""
    if run.config.get("algorithm_id") != "tnk_biofeedback" or run.status.value != "completed":
        raise TnkWorkbenchUnavailableError("需要已完成的 TNK 脑指标组 Run")
    result = score_state_summary(run.result_summary or {}, cursor_s)
    return {"run_id": run_id, "recording_id": run.recording_id, **result}
