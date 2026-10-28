"""Generic runtime contract tests; no TNK production calculator is imported."""

import numpy as np
import pytest

from app.algorithm_runtime.on_demand import (
    DynamicSessionIdentity,
    DynamicWindowIdentity,
    DynamicWindowPlan,
)
from app.algorithm_runtime.offline_execution import scientific_cache_key
from app.algorithm_runtime.errors import DynamicWindowResourceExceededError
from app.services.dynamic_window_resources import DynamicWindowResourcePolicy


def _session(context_identity: str = "context-a") -> DynamicSessionIdentity:
    return DynamicSessionIdentity(
        recording_id="synthetic", source_sha256="source", algorithm_id="fake-history",
        definition_id="fake-definition", definition_version="1", scientific_version="1",
        implementation_identity="fake-v1", configuration_sha256="config",
        window_policy_version="fake-window-v1", reset_origin_sample=0,
        context_identity=context_identity,
    )


def _plan(session: DynamicSessionIdentity, *, context_start: int = 0,
          target_start: int = 4, target_end: int = 6, replay_windows: int = 3) -> DynamicWindowPlan:
    return DynamicWindowPlan(
        session_identity=session, cursor_sample=target_end,
        start_sample=target_start, end_sample=target_end,
        start_s=float(target_start), end_s=float(target_end),
        context_start_sample=context_start, context_end_sample=target_end,
        context_identity=session.context_identity, replay_window_count=replay_windows,
    )


def test_context_and_reset_are_part_of_window_identity_and_scientific_cache_key():
    session = _session()
    plan = _plan(session)
    identity = DynamicWindowIdentity(**session.model_dump(), generation=0,
                                     start_sample=plan.start_sample, end_sample=plan.end_sample)
    changed_session = _session("context-b")
    changed_plan = _plan(changed_session)
    changed_identity = DynamicWindowIdentity(**changed_session.model_dump(), generation=0,
                                             start_sample=changed_plan.start_sample,
                                             end_sample=changed_plan.end_sample)
    assert identity != changed_identity
    def key(context_identity: str) -> str:
        return scientific_cache_key(
            source_sha256="source", definition_sha256="definition",
            config_sha256=context_identity, implementation_build="build",
            actual_range={"start_s": 4.0, "end_s": 6.0},
        )
    assert key(session.context_identity) != key(changed_session.context_identity)
    changed_reset = session.model_copy(update={"reset_origin_sample": 1})
    assert changed_reset != session


def test_generic_history_replay_matches_full_target_and_only_returns_target():
    samples = np.arange(10, dtype=np.float64)
    full = np.cumsum(samples)
    plan = _plan(_session(), context_start=0, target_start=4, target_end=6)
    state = 0.0
    replayed_target = None
    for index in range(plan.context_start_sample, plan.context_end_sample):
        state += samples[index]
        if index + 1 == plan.end_sample:
            replayed_target = state
    assert replayed_target == full[plan.end_sample - 1]
    assert isinstance(replayed_target, float)


def test_replay_cancellation_never_returns_a_partial_target():
    samples = np.ones(10, dtype=np.float64)
    plan = _plan(_session(), context_start=0, target_start=4, target_end=6)
    visits = 0

    def replay(check_cancelled):
        nonlocal visits
        state = 0.0
        for index in range(plan.context_start_sample, plan.context_end_sample):
            check_cancelled()
            visits += 1
            if visits == 3:
                raise RuntimeError("cancelled")
            state += samples[index]
        return state

    with pytest.raises(RuntimeError, match="cancelled"):
        replay(lambda: None)
    assert visits == 3


def test_replay_budgets_fail_explicitly_with_structured_requested_and_limit():
    policy = DynamicWindowResourcePolicy(max_replay_samples=5, max_replay_windows=2)
    with pytest.raises(DynamicWindowResourceExceededError) as sample_error:
        policy.check_replay_budget(context_start_sample=0, context_end_sample=6,
                                   replay_window_count=2)
    assert sample_error.value.code == "WINDOW_RESOURCE_EXCEEDED"
    assert sample_error.value.detail == {"resource": "replay_samples", "requested": 6, "limit": 5}
    with pytest.raises(DynamicWindowResourceExceededError) as window_error:
        policy.check_replay_budget(context_start_sample=0, context_end_sample=5,
                                   replay_window_count=3)
    assert window_error.value.detail == {"resource": "replay_windows", "requested": 3, "limit": 2}


def test_plan_requires_context_to_contain_target_and_not_precede_reset():
    session = _session().model_copy(update={"reset_origin_sample": 2})
    with pytest.raises(ValueError, match="cannot precede"):
        _plan(session, context_start=1)
    with pytest.raises(ValueError, match="contain the target"):
        DynamicWindowPlan(
            session_identity=session, cursor_sample=6, start_sample=4, end_sample=6,
            start_s=4, end_s=6, context_start_sample=2, context_end_sample=5,
        )


def test_tnk_strategy_matrix_declares_each_algorithm_and_explicit_segment_boundary():
    import json
    from pathlib import Path

    matrix = Path("docs/algorithms/tnk-strategy-matrix.md").read_text(encoding="utf-8")
    expected = {
        "IAPF", "放松度", "空间分布", "节律稳定性", "综合冥想指数", "BrainBeat 窗口模式",
        "BrainBeat Flat", "疲劳指数", "HAI", "FAA", "固定频段 RBP", "1/f 非周期指数",
    }
    rows = [line for line in matrix.splitlines() if line.startswith("|") and "---" not in line]
    assert expected.issubset({row.split("|")[1].strip() for row in rows})
    faa = next(row for row in rows if "| FAA |" in row)
    assert "任意游标窗明确不支持" in faa
    assert "已注册" in matrix
    assert "仅支持 `dynamic`" in matrix
    structured = json.loads(Path("docs/algorithms/tnk-strategy-matrix.json").read_text(encoding="utf-8"))
    assert len(structured["algorithms"]) == 12
    assert structured["public_boundary"]["supported_modes"] == ["dynamic"]
    assert structured["algorithms"]["tnk_faa"]["on_demand"]["intent"] == "unsupported"
    assert structured["algorithms"]["tnk_faa"]["on_demand"]["reason"]["code"] == "TNK_FAA_SEGMENT_SCOPE_REQUIRED"
