"""Resolve scientific request identity and window plans outside admission locks."""

from __future__ import annotations

import time
from dataclasses import dataclass
from typing import Any

from app.algorithm_runtime.contracts import AlgorithmInputs
from app.algorithm_runtime.errors import (
    DynamicWindowProjectForbiddenError, DynamicWindowCursorInvalidError,
    RetiredAlgorithmVersionError,
)
from app.algorithm_runtime.offline_execution import scientific_configuration_sha256, scientific_context_snapshot
from app.algorithm_runtime.on_demand import DynamicSessionIdentity, DynamicWindowCapability, DynamicWindowPlan, DynamicWindowRequest
from app.algorithm_runtime.on_demand_registry import DynamicWindowCapabilityRegistry
from app.algorithm_runtime.registry import AlgorithmRegistry
from app.core.performance_trace import measure_performance
from app.models.official_algorithm_run import OfficialAlgorithmRunConfig
from app.persistence.repositories.dynamic_window import DynamicSessionRecord, DynamicWindowRecord, DynamicWindowRepository
from app.services.algorithm_recording_context import _RecordingAlgorithmContext
from app.services.dynamic_window_identity import DynamicWindowIdentityResolver
from app.services.dynamic_window_responses import plan_from_records
from app.services.projects import ProjectService
from app.services.recordings import RecordingService


@dataclass(frozen=True)
class PreparedDynamicWindow:
    session: DynamicSessionRecord
    capability: DynamicWindowCapability
    config: Any
    inputs: AlgorithmInputs
    plan: DynamicWindowPlan
    started: float
    planning_ms: float
    input_resolution_ms: float
    window_planning_ms: float


class DynamicWindowPlanner:
    """Validate every request; do not cache mutable permissions or science identity."""

    def __init__(self, recordings: RecordingService, registry: AlgorithmRegistry,
                 capabilities: DynamicWindowCapabilityRegistry, repository: DynamicWindowRepository,
                 projects: ProjectService, identity_resolver: DynamicWindowIdentityResolver) -> None:
        self.recordings = recordings
        self.registry = registry
        self.capabilities = capabilities
        self.repository = repository
        self._projects = projects
        self._identity_resolver = identity_resolver

    def prepare(self, payload: DynamicWindowRequest, *,
                recovering: DynamicWindowRecord | None = None) -> PreparedDynamicWindow:
        started = time.perf_counter()
        planning_started = started
        input_resolution_ms = 0.0
        window_planning_ms = 0.0
        requested_version = payload.config.get("scientific_version")
        module = self.registry.get_current(payload.algorithm_id)
        if requested_version is not None and requested_version != module.manifest.scientific_version:
            raise RetiredAlgorithmVersionError(
                f"official algorithm version {requested_version!r} is retired",
                detail={"algorithm_id": payload.algorithm_id,
                        "requested_version": requested_version,
                        "current_version": module.manifest.scientific_version},
            )
        capability = self.capabilities.get(payload.algorithm_id, module.manifest.scientific_version)
        with measure_performance("recording_metadata"):
            recording = self.recordings.require_recording(payload.recording_id)
        if payload.project_id is not None:
            if (self._projects.get(payload.project_id) is None or
                    recording.id not in self._projects.project_recording_ids(payload.project_id)):
                raise DynamicWindowProjectForbiddenError("记录不属于请求的项目。")
        if not recording.source_sha256 or not recording.sfreq or not recording.duration_s:
            raise ValueError("recording scientific identity or metadata is unavailable")
        official = OfficialAlgorithmRunConfig.model_validate(self._official_payload(payload))
        typed_config = module.config_model.model_validate(official.runtime_config())
        typed_config = self._runtime_validate(module, typed_config)
        montage_snapshot = official.montage_snapshot()
        if montage_snapshot is not None:
            catalogue = {str(item.get("id")): item for item in getattr(recording, "montage_catalogue", ())}
            profile = catalogue.get(str(montage_snapshot["id"]))
            if profile is None:
                raise ValueError("ANALYSIS_MONTAGE_NOT_IN_RECORDING: 所选导联配置不属于该记录的历史目录")
            if montage_snapshot.get("revision") != profile.get("revision") or montage_snapshot.get("fingerprint") != profile.get("fingerprint"):
                raise ValueError("ANALYSIS_MONTAGE_STALE: 所选导联配置版本或指纹与记录快照不一致")
            montage_snapshot = {
                **montage_snapshot,
                "snapshot_source": profile.get("snapshot_source", "acquisition"),
                "source_profile_id": profile.get("source_profile_id", profile["id"]),
                "source_mapping": profile.get("source_mapping", {}),
                "formulas": profile.get("formulas", []),
            }
            catalogue_outputs = {str(item.get("id")): item for item in profile.get("outputs", []) if item.get("available", False)}
            outputs = set(catalogue_outputs)
            requested = module.requested_channels(typed_config)
            requested_outputs = montage_snapshot.get("outputs") or []
            mapped_ids = [str(item.get("output", "")) for item in requested_outputs]
            mapped_roles = [str(item.get("role", "")) for item in requested_outputs]
            if (len(mapped_ids) != len(set(mapped_ids)) or
                    any(not role for role in mapped_roles) or
                    set(mapped_ids) != {str(channel) for channel in requested}):
                raise ValueError("ANALYSIS_MONTAGE_MAPPING_MISMATCH: 导联角色映射与算法请求输入不一致")
            for output in requested_outputs:
                output_id = str(output.get("output", ""))
                declared = catalogue_outputs.get(output_id)
                if declared is not None and str(output.get("role", "")) != str(declared.get("role", "")):
                    raise ValueError("ANALYSIS_MONTAGE_MAPPING_MISMATCH: 导联角色映射与算法请求输入不一致")
            if any(channel.casefold() not in {item.casefold() for item in outputs} for channel in requested):
                raise ValueError("ANALYSIS_MONTAGE_ROLE_UNRESOLVED: 算法输入未解析到所选历史导联输出")
            recording = self.recordings.select_analysis_montage(recording, str(montage_snapshot["id"]))
        context = _RecordingAlgorithmContext(self.recordings, recording)
        try:
            input_resolution_started = time.perf_counter()
            inputs = module.resolve_inputs(context, typed_config)
            input_resolution_ms = (time.perf_counter() - input_resolution_started) * 1000
            max_cursor_sample = int(round(inputs.duration_s * inputs.sfreq_hz))
            if payload.cursor_sample > max_cursor_sample:
                raise DynamicWindowCursorInvalidError("cursor sample exceeds recording duration")
            resolved_identity = self._identity_resolver.resolve(module, payload)
            config = official.model_copy(update={"scientific_version": module.manifest.scientific_version}).model_dump(mode="json")
            if hasattr(self.recordings, "analysis_signal_provenance"):
                try:
                    signal_source = self.recordings.analysis_signal_provenance(recording)
                except ValueError as exc:
                    # Keep the window request auditable so the worker can return
                    # a structured unavailable result instead of publishing a
                    # fabricated ready payload for a damaged historical source.
                    signal_source = {
                        "signal_mode": "unverifiable",
                        "analysis_reference": "unverifiable_source_metadata",
                        "error_type": type(exc).__name__,
                    }
            else:
                signal_source = {"signal_mode": "raw", "analysis_reference": "original_recording_no_software_rereference"}
            # A display label can contain operator-entered text and is not part
            # of the signal calculation. Keep it out of the scientific identity
            # snapshot and the public canonical configuration payload.
            signal_source = {key: value for key, value in signal_source.items() if key != "montage_label"}
            execution_snapshot = module.execution_snapshot(typed_config)
            channel_mapping = {
                "channels": module.requested_channels(typed_config),
                "signal_source": signal_source,
            }
            if montage_snapshot is not None:
                channel_mapping["analysis_montage"] = montage_snapshot
            reference = {"mode": signal_source["analysis_reference"]}
            scientific_context = scientific_context_snapshot(
                sampling_rate_hz=inputs.sfreq_hz,
                signal_mapping=channel_mapping,
                reference=reference,
                filters=execution_snapshot.filters,
                quality_rules=execution_snapshot.quality_rules,
                window=execution_snapshot.window,
                window_policy_version=capability.window_policy_version,
            )
            replay_context = scientific_context["replay_context"]
            context_identity = str(replay_context["context_identity"])
            scientific_config_sha256 = scientific_configuration_sha256(config, scientific_context)
            session_identity = DynamicSessionIdentity(
                project_scope=payload.project_id,
                recording_id=recording.id,
                source_sha256=recording.source_sha256,
                algorithm_id=module.manifest.algorithm_id,
                **resolved_identity,
                scientific_version=module.manifest.scientific_version,
                implementation_identity=module.manifest.implementation_identity,
                configuration_sha256=scientific_config_sha256,
                window_policy_version=capability.window_policy_version,
                reset_origin_sample=0,
                context_identity=context_identity,
            )
            if recovering is not None:
                previous_session = self.repository.get_session(recovering.session_id)
                previous_identity = plan_from_records(previous_session, recovering).session_identity
                if previous_identity != session_identity:
                    raise ValueError("recovered source/configuration identity no longer matches its snapshot")
            session = self.repository.ensure_session(
                session_identity,
                configuration={**config, "_scientific_context": scientific_context},
            )
            window_planning_started = time.perf_counter()
            plan = capability.plan_window(inputs, typed_config, payload.cursor_sample, session_identity)
            window_planning_ms = (time.perf_counter() - window_planning_started) * 1000
        finally:
            context.close()
        planning_ms = (time.perf_counter() - planning_started) * 1000

        return PreparedDynamicWindow(session, capability, typed_config, inputs, plan, started,
                                     planning_ms, input_resolution_ms, window_planning_ms)

    @staticmethod
    def _runtime_validate(module: Any, config: Any) -> Any:
        if config.mode != "dynamic":
            raise ValueError("on-demand dynamic windows require dynamic mode")
        policy = module.manifest.dynamic_policy
        if config.window_s not in policy.window_options_s or config.step_s not in policy.refresh_step_options_s:
            raise ValueError("dynamic window or step is not supported by this algorithm")
        return config

    @staticmethod
    def _official_payload(payload: DynamicWindowRequest) -> dict[str, Any]:
        """Accept the two existing official-config spellings at this boundary."""
        raw = dict(payload.config)
        raw["algorithm_id"] = payload.algorithm_id
        if "time" not in raw and {"start_s", "end_s"} <= raw.keys():
            raw["time"] = {"start_s": raw.pop("start_s"), "end_s": raw.pop("end_s")}
        if "dynamic_window_s" not in raw and "window_s" in raw:
            raw["dynamic_window_s"] = raw.pop("window_s")
        if "refresh_step_s" not in raw and "step_s" in raw:
            raw["refresh_step_s"] = raw.pop("step_s")
        return raw
