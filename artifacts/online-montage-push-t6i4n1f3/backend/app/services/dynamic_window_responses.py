"""Map persisted window identity and status without reading scientific payloads."""

from typing import Any

from app.algorithm_runtime.on_demand import DynamicSessionIdentity, DynamicWindowPlan
from app.algorithm_runtime.offline_execution import scientific_cache_key, scientific_configuration_json
from app.persistence.repositories.dynamic_window import DynamicWindowRecord


def session_cache_key(session: Any) -> str:
    configuration = dict(session.configuration)
    time_range = configuration.get("time") or {}
    return scientific_cache_key(
        source_sha256=session.source_sha256,
        definition_sha256=session.definition_sha256,
        config_sha256=session.configuration_sha256,
        implementation_build=session.implementation_build,
        actual_range={"start_s": float(time_range["start_s"]), "end_s": float(time_range["end_s"])},
    )

def plan_from_records(session: Any, window: DynamicWindowRecord) -> DynamicWindowPlan:
    replay_context = (session.configuration.get("_scientific_context") or {}).get("replay_context", {})
    context_identity = str(replay_context.get("context_identity", ""))
    identity = DynamicSessionIdentity(
        project_scope=session.project_id, recording_id=session.recording_id, source_sha256=session.source_sha256,
        algorithm_id=session.algorithm_id, definition_id=session.definition_id, definition_version=session.definition_version,
        definition_sha256=session.definition_sha256, implementation_build=session.implementation_build,
        scientific_version=session.scientific_version, implementation_identity=session.implementation_identity,
        configuration_sha256=session.configuration_sha256, window_policy_version=session.window_policy_version,
        reset_origin_sample=int(replay_context.get("reset_origin_sample", 0)),
        context_identity=context_identity,
    )
    return DynamicWindowPlan(session_identity=identity, cursor_sample=window.end_sample,
                             cursor_s=window.end_s,
                             start_sample=window.start_sample, end_sample=window.end_sample,
                             start_s=window.start_s, end_s=window.end_s,
                             context_start_sample=int(replay_context.get("reset_origin_sample", 0)),
                             context_end_sample=window.end_sample,
                             context_identity=context_identity,
                             state="planned" if window.state not in {"insufficient_data", "unavailable"} else "insufficient_data",
                             reason=window.failure.get("message") if window.failure else None)

def session_scientific_configuration_json(session: Any) -> str:
    stored = dict(session.configuration)
    context = stored.pop("_scientific_context")
    return scientific_configuration_json(stored, context)

def response(session_id: str, window: DynamicWindowRecord, plan: DynamicWindowPlan,
              request_id: str | None = None, result: dict[str, Any] | None = None,
              subscription_id: str | None = None,
              scientific_cache_key: str | None = None,
              *,
              scientific_configuration_json: str,
              playback_delivery: dict[str, Any] | None = None) -> dict[str, Any]:
    identity = {
        **plan.session_identity.model_dump(mode="json"),
        "start_sample": window.start_sample,
        "end_sample": window.end_sample,
    }
    return {"session_id": session_id, "window_id": window.window_id,
            "request_id": request_id or f"window-status-{window.window_id}", "state": window.state,
            "subscription_id": subscription_id,
            "execution_mode": "on_demand",
            "scientific_cache_key": scientific_cache_key,
            "scientific_configuration_json": scientific_configuration_json,
            "identity": {**identity, "generation": window.generation},
            "plan": plan.model_dump(mode="json"), "range": {"start_sample": window.start_sample, "end_sample": window.end_sample,
            "start_s": window.start_s, "end_s": window.end_s}, "quality": window.quality,
            "failure": window.failure, "result": result,
            **({"playback_delivery": playback_delivery} if playback_delivery is not None else {})}
