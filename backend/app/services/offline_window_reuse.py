"""Verified bridge from persisted on-demand windows to full offline Runs."""

from __future__ import annotations

from collections.abc import Callable
import json
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any

import numpy as np

from app.algorithm_runtime.offline_execution import scientific_configuration_sha256
from app.core.provenance import sha256_json
from app.algorithm_runtime.on_demand import DynamicSessionIdentity, DynamicWindowIdentity, DynamicWindowResult
from app.algorithm_runtime.on_demand_result_validation import validate_window_result
from app.persistence.repositories.dynamic_window import DynamicWindowRepository
from app.services.dynamic_window_artifacts import DynamicWindowArtifactStore


class OfflineWindowReuse:
    """Build a read-only provider for exact, verified dynamic windows."""

    def __init__(self, database_path: Path, artifacts_dir: Path):
        self.repository = DynamicWindowRepository(database_path)
        self.artifacts = DynamicWindowArtifactStore(Path(artifacts_dir) / "dynamic-windows", self.repository)

    @staticmethod
    def _session_identity(run: Any, resolved: dict[str, Any], module: Any, source_sha256: str) -> DynamicSessionIdentity:
        config = dict(resolved["config"])
        return DynamicSessionIdentity(
            project_scope=run.project_id, recording_id=run.recording_id, source_sha256=source_sha256,
            algorithm_id=module.manifest.algorithm_id, definition_id=resolved["definition_id"],
            definition_version=resolved["definition_version"], definition_sha256=resolved["definition_sha256"],
            implementation_build=run.implementation_version, scientific_version=resolved["scientific_version"],
            implementation_identity=module.manifest.implementation_identity,
            configuration_sha256=scientific_configuration_sha256(config, resolved["scientific_context"]),
            window_policy_version=getattr(module, "window_policy_version", ""),
        )

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
        if not callable(validate_reuse) or "window_output_kind" not in module.manifest.output_schema:
            return None
        session_identity = self._session_identity(run, resolved, module, source_sha256)
        session = self.repository.ensure_session(
            session_identity,
            configuration={**config, "_scientific_context": resolved["scientific_context"]},
        )
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
                sfreq = float(resolved["scientific_context"]["sampling_rate_hz"])
                row = self.repository.ensure_window(
                    session.session_id, start_sample=start_sample, end_sample=end_sample,
                    start_s=float(start_sample) / sfreq, end_s=float(end_sample) / sfreq,
                )
                windows[(start_sample, end_sample)] = row
                if self.repository.get_active_attempt(row.window_id) is None:
                    self.repository.create_attempt(
                        row.window_id, f"full:{run.run_id}:{row.window_id}",
                        producer_mode="full", producer_owner_id=run.run_id, priority=10,
                    )
                miss("full_producer_claimed")
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
                        row.window_id, timeout_s=3600.0, check_cancelled=check_cancelled,
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

    def publish_full_windows(
        self,
        *,
        run: Any,
        resolved: dict[str, Any],
        registry: Any,
        source_sha256: str,
        result: dict[str, Any],
        arrays: dict[str, Any],
    ) -> int:
        """Publish full-run windows guarded by the durable full producer lease.

        The full algorithm remains the sole numerical producer. Publication is
        delayed until its validated result exists, then each window gets the
        same immutable artifact contract used by on-demand execution.
        """
        if run.analysis_type != "official_algorithm" or dict(resolved.get("config") or {}).get("mode") != "dynamic":
            return 0
        config = dict(resolved["config"])
        module = registry.get(config["algorithm_id"], resolved["scientific_version"])
        if (not callable(getattr(module, "can_reuse_offline_window", None)) or
                "window_output_kind" not in module.manifest.output_schema):
            return 0
        identity = self._session_identity(run, resolved, module, source_sha256)
        session = self.repository.find_session(identity)
        if session is None:
            return 0
        structured = result.get("structured") if isinstance(result, dict) else None
        if not isinstance(structured, dict) or not isinstance(structured.get("windows"), list):
            return 0
        window_items = [item for item in structured["windows"] if isinstance(item, dict)]
        axis_values: dict[str, np.ndarray] = {}
        for name, metadata in (structured.get("axes") or {}).items():
            key = metadata.get("array_key") if isinstance(metadata, dict) else None
            if key not in arrays:
                return 0
            axis_values[name] = np.asarray(arrays[key], dtype=float)
        evidence_values: list[dict[str, Any]] = []
        raw_evidence = arrays.get("window_evidence_json")
        if raw_evidence is not None:
            try:
                decoded = json.loads(str(np.asarray(raw_evidence).reshape(-1)[0]))
                if isinstance(decoded, list):
                    evidence_values = [item for item in decoded if isinstance(item, dict)]
            except (IndexError, TypeError, ValueError, json.JSONDecodeError):
                evidence_values = []
        published = 0
        worker_id = f"full-publisher-{run.run_id}"
        expiry = (datetime.now(timezone.utc) + timedelta(minutes=10)).isoformat()
        for index, item in enumerate(window_items):
            start_sample, end_sample = int(item["start_sample"]), int(item["end_sample"])
            window = self.repository.latest_window(session.session_id, start_sample=start_sample, end_sample=end_sample)
            if window is None or window.state == "ready":
                continue
            attempt = self.repository.get_active_attempt(window.window_id)
            if attempt is None or attempt.producer_mode != "full" or attempt.producer_owner_id != run.run_id:
                continue
            claimed = self.repository.claim_attempt(window.window_id, worker_id, expiry)
            if claimed is None:
                continue
            dynamic_state = str(item.get("state") or "Complete")
            failure = item.get("failure")
            if isinstance(failure, dict) and "detail" in failure and "details" not in failure:
                failure = {**failure, "details": failure.get("detail") or {}}
                failure.pop("detail", None)
            if dynamic_state in {"Rejected", "Unavailable"}:
                state = "rejected" if dynamic_state == "Rejected" else "unavailable"
                quality = str(item.get("quality") or state)
                payload_axes = {}
                payload_arrays: dict[str, Any] = {}
            else:
                state, quality = "ready", str(item.get("quality") or "clean")
                payload_axes = axis_values
                payload_arrays = {}
                for name, value in arrays.items():
                    if name.startswith("axis_") or name == "window_evidence_json":
                        continue
                    matrix = np.asarray(value)
                    if matrix.ndim == 0:
                        payload_arrays[name] = matrix
                    elif matrix.shape[0] == len(window_items):
                        payload_arrays[name] = matrix[index]
                    else:
                        payload_arrays[name] = matrix
                if any(not np.isfinite(np.asarray(value, dtype=float)).all() for value in payload_arrays.values()):
                    state, quality = "unavailable", "unavailable"
                    payload_axes = {}
                    payload_arrays = {}
                    failure = {"code": "FULL_WINDOW_NONFINITE", "message": "完整结果窗口包含不可复用的非有限值"}
            evidence: dict[str, Any] = {}
            if index < len(evidence_values):
                evidence = dict(evidence_values[index])
            provenance_id = sha256_json({"run_id": run.run_id, "window_id": window.window_id, "producer_mode": "full"})
            window_identity = DynamicWindowIdentity(
                **identity.model_dump(), generation=window.generation,
                start_sample=start_sample, end_sample=end_sample,
            )
            dynamic_result = DynamicWindowResult(
                identity=window_identity, state=state, output_kind=str(structured.get("output", {}).get("kind", "series")),
                axes=payload_axes, axis_units={name: str((structured.get("axes") or {}).get(name, {}).get("unit", "")) for name in payload_axes},
                arrays=payload_arrays,
                array_units={name: str((structured.get("arrays") or {}).get(name, {}).get("unit", "")) for name in payload_arrays},
                quality=quality, failure=failure, evidence=evidence,
                provenance={"execution_mode": "full", "producer_mode": "full", "result_scope": "window",
                            "served_from": "full_computation", "scientific_cache_key": run.cache_key,
                            "provenance_id": provenance_id},
            )
            dynamic_result = validate_window_result(dynamic_result, module.manifest)
            artifact_id = self.artifacts.write(window.window_id, dynamic_result)
            self.repository.update_attempt_window(
                claimed.attempt_id, worker_id, claimed.lease_token or "", "preparing_source",
            )
            self.repository.update_attempt_window(
                claimed.attempt_id, worker_id, claimed.lease_token or "", "computing_window",
            )
            self.repository.update_attempt_window(
                claimed.attempt_id, worker_id, claimed.lease_token or "", state,
                quality=quality, failure=failure, artifact_id=artifact_id,
                manifest_sha256=self.artifacts.manifest_digest(artifact_id) if state == "ready" else None,
            )
            published += 1
        return published
