"""Shared contract for offline execution modes and scientific cache identity."""

from __future__ import annotations

from typing import Any, Literal

from app.algorithm_runtime.contracts import DYNAMIC_ANALYSIS_RESULT_CONTRACT_VERSION
from app.core.provenance import sha256_json


OfflineExecutionMode = Literal["full", "on_demand"]
OFFLINE_EXECUTION_CONTRACT_VERSION = "offline-analysis-v1"
SCIENTIFIC_CACHE_CONTRACT_VERSION = "scientific-analysis-cache-v1"


def scientific_context_snapshot(
    *,
    sampling_rate_hz: float,
    signal_mapping: dict[str, Any],
    reference: dict[str, Any],
    filters: dict[str, Any],
    quality_rules: dict[str, Any],
    window: dict[str, Any],
    window_policy_version: str = "",
) -> dict[str, Any]:
    """Return the normalized execution context that affects EEG values.

    The user-facing algorithm config is not sufficient identity for a result:
    sampling metadata, signal projection, preprocessing, quality gates, and
    window semantics all change the scientific meaning of the same parameters.
    """

    return {
        "sampling_rate_hz": float(sampling_rate_hz),
        "signal_mapping": dict(signal_mapping),
        "reference": dict(reference),
        "filters": dict(filters),
        "quality_rules": dict(quality_rules),
        "window": dict(window),
        "window_policy_version": str(window_policy_version),
    }


def scientific_configuration_sha256(
    config: dict[str, Any], context: dict[str, Any],
) -> str:
    """Hash normalized algorithm parameters together with their value context."""

    # Implementation identity is persisted as an audit snapshot, while the
    # resolved Definition digest already binds it into the scientific cache key.
    # Exclude that duplicate metadata so full and on-demand callers with the
    # same scientific parameters share one configuration identity.
    normalized_config = {key: value for key, value in config.items() if key != "implementation_identity"}
    return sha256_json({
        "scientific_configuration_contract": "scientific-config-v1",
        "config": normalized_config,
        "context": dict(context),
    })


def scientific_cache_key(
    *,
    source_sha256: str,
    definition_sha256: str,
    config_sha256: str,
    implementation_build: str,
    actual_range: dict[str, float],
) -> str:
    """Return one mode-independent identity for the scientific computation.

    The execution strategy is intentionally absent: a full Run and an
    on-demand window with the same source, definition, config, build, and
    recording-relative range are scientifically interchangeable.
    """

    return sha256_json({
        "cache_contract": SCIENTIFIC_CACHE_CONTRACT_VERSION,
        "source_sha256": source_sha256,
        "definition_sha256": definition_sha256,
        "config_sha256": config_sha256,
        "implementation_build": implementation_build,
        "actual_range": actual_range,
    })


def execution_provenance(
    mode: OfflineExecutionMode,
    *,
    cache_key: str,
    reused_window_count: int = 0,
    cache_miss_reasons: dict[str, int] | None = None,
    result_scope: str | None = None,
    served_from: str | None = None,
    coverage: dict[str, Any] | None = None,
) -> dict[str, Any]:
    """Describe how a result was produced without changing its values."""

    resolved_scope = result_scope or ("full_run" if mode == "full" else "window")
    resolved_source = served_from or ("full_computation" if mode == "full" else "on_demand_computation")
    provenance_id = sha256_json({
        "cache_key": cache_key,
        "producer_mode": mode,
        "result_scope": resolved_scope,
        "served_from": resolved_source,
        "coverage": dict(coverage or {}),
    })
    payload = {
        "contract_version": OFFLINE_EXECUTION_CONTRACT_VERSION,
        "execution_mode": mode,
        "scientific_cache_key": cache_key,
        "reused_on_demand_window_count": int(reused_window_count),
        "cache_miss_reasons": dict(cache_miss_reasons or {}),
        "producer_mode": mode,
        "result_scope": resolved_scope,
        "served_from": resolved_source,
        "provenance_id": provenance_id,
    }
    if coverage is not None:
        payload["coverage"] = dict(coverage)
    return payload


def official_definition_sha256(
    *,
    definition_id: str,
    definition_version: str,
    definition_digest: str,
    algorithm_id: str,
    scientific_version: str,
    implementation_identity: str,
) -> str:
    """Build the same resolved official Definition digest in both APIs."""

    return sha256_json({
        "kind": "official_algorithm",
        "definition_id": definition_id,
        "definition_version": definition_version,
        "definition_digest": definition_digest,
        "algorithm_id": algorithm_id,
        "scientific_version": scientific_version,
        "implementation_identity": implementation_identity,
        "result_contract_version": DYNAMIC_ANALYSIS_RESULT_CONTRACT_VERSION,
    })
