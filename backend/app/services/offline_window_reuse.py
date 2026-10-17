"""Verified bridge from persisted on-demand windows to full offline Runs."""

from __future__ import annotations

from collections.abc import Callable
from pathlib import Path
from typing import Any

from app.algorithm_runtime.offline_execution import scientific_configuration_sha256
from app.algorithm_runtime.on_demand import DynamicSessionIdentity, DynamicWindowIdentity, DynamicWindowResult
from app.algorithm_runtime.on_demand_result_validation import validate_window_result
from app.persistence.repositories.dynamic_window import DynamicWindowRepository
from app.services.dynamic_window_artifacts import DynamicWindowArtifactStore


class OfflineWindowReuse:
    """Build a read-only provider for exact, verified dynamic windows."""

    def __init__(self, database_path: Path, artifacts_dir: Path):
        self.repository = DynamicWindowRepository(database_path)
        self.artifacts = DynamicWindowArtifactStore(Path(artifacts_dir) / "dynamic-windows", self.repository)

    def provider(
        self,
        *,
        run: Any,
        resolved: dict[str, Any],
        registry: Any,
        source_sha256: str,
    ) -> Callable[[int, int], DynamicWindowResult | None] | None:
        if run.analysis_type != "official_algorithm":
            return None
        config = dict(resolved.get("config") or {})
        if config.get("mode") != "dynamic":
            return None
        module = registry.get(config["algorithm_id"], resolved["scientific_version"])
        validate_reuse = getattr(module, "can_reuse_offline_window", None)
        if not callable(validate_reuse):
            return None
        scientific_config_sha256 = scientific_configuration_sha256(
            config, resolved["scientific_context"],
        )
        session = self.repository.find_session(DynamicSessionIdentity(
            project_scope=run.project_id,
            recording_id=run.recording_id,
            source_sha256=source_sha256,
            algorithm_id=module.manifest.algorithm_id,
            definition_id=resolved["definition_id"],
            definition_version=resolved["definition_version"],
            definition_sha256=resolved["definition_sha256"],
            implementation_build=run.implementation_version,
            scientific_version=resolved["scientific_version"],
            implementation_identity=module.manifest.implementation_identity,
            configuration_sha256=scientific_config_sha256,
            window_policy_version=getattr(module, "window_policy_version", ""),
        ))
        if session is None:
            return None
        windows = {(item.start_sample, item.end_sample): item for item in self.repository.latest_windows(session.session_id)}
        miss_reasons: dict[str, int] = {}

        def miss(reason: str) -> None:
            miss_reasons[reason] = miss_reasons.get(reason, 0) + 1

        def get_cached(
            start_sample: int,
            end_sample: int,
            *,
            check_cancelled: Callable[[], None] | None = None,
        ) -> DynamicWindowResult | None:
            if check_cancelled is not None:
                check_cancelled()
            row = windows.get((start_sample, end_sample))
            if row is None:
                miss("not_cached")
                return None
            # When the combined strategy starts full analysis immediately
            # after the preview request, let the existing on-demand producer
            # finish this exact window before deciding to recompute it.  The
            # repository/lease state remains authoritative across restarts;
            # a bounded wait falls back to the normal validated full path.
            if row.state in {"queued", "preparing_source", "computing_window"}:
                # A full Run is another consumer while it waits for the
                # interactive producer. Without this protection subscription,
                # cancelling the UI subscription could cancel shared work that
                # the full Run is still waiting to reuse.
                protection = self.repository.create_subscription(
                    row.window_id,
                    f"full-reuse:{run.run_id}:{row.window_id}",
                    project_id=session.project_id,
                )
                try:
                    row = self.repository.wait_for_window_terminal(
                        row.window_id, check_cancelled=check_cancelled,
                    )
                finally:
                    self.repository.cancel_subscription(
                        protection.subscription_id,
                        project_id=session.project_id,
                        cancel_unobserved_work=False,
                    )
                if row is None:
                    miss("window_disappeared")
                    return None
                windows[(start_sample, end_sample)] = row
            if row.state != "ready" or not row.result_artifact_id:
                miss("not_reusable_state")
                return None
            try:
                manifest_sha256 = self.repository.artifact_manifest_digest(row.result_artifact_id)
                if not manifest_sha256:
                    miss("manifest_unverified")
                    return None
                payload = self.artifacts.read(
                    row.result_artifact_id,
                    manifest_sha256=manifest_sha256,
                )
                result = DynamicWindowResult.model_validate(payload)
                expected_identity = DynamicWindowIdentity(
                    project_scope=session.project_id,
                    recording_id=session.recording_id,
                    source_sha256=session.source_sha256,
                    algorithm_id=session.algorithm_id,
                    definition_id=session.definition_id,
                    definition_version=session.definition_version,
                    definition_sha256=session.definition_sha256,
                    implementation_build=session.implementation_build,
                    scientific_version=session.scientific_version,
                    implementation_identity=session.implementation_identity,
                    configuration_sha256=session.configuration_sha256,
                    window_policy_version=session.window_policy_version,
                    generation=row.generation,
                    start_sample=row.start_sample,
                    end_sample=row.end_sample,
                )
                if result.identity != expected_identity:
                    miss("identity_mismatch")
                    return None
                if result.provenance.get("scientific_cache_key") != run.cache_key:
                    miss("cache_key_mismatch")
                    return None
                validated = validate_window_result(result, module.manifest)
                if validated.state != "ready" or validated.quality != row.quality:
                    miss("state_quality_mismatch")
                    return None
                if not validate_reuse(
                    validated,
                    sampling_rate_hz=resolved["scientific_context"]["sampling_rate_hz"],
                    config=config,
                ):
                    miss("window_output_mismatch")
                    return None
                return validated
            except (OSError, ValueError, KeyError, TypeError):
                miss("artifact_invalid")
                return None

        get_cached.cache_miss_reasons = miss_reasons
        return get_cached
