"""Typed, code-owned contracts for platform official algorithms."""

from __future__ import annotations

from typing import Literal

from pydantic import BaseModel, Field

from app.algorithm_runtime.contracts import AlgorithmManifest


OfficialAvailability = Literal["shadow_validation", "available", "deprecated"]
OfficialExecutionKind = Literal["generic_research_primitives", "official_composite_shadow_only", "official_composite_run_adapter"]


# Runnable official algorithms use the same manifest they execute.  This alias
# preserves the catalog type name for callers while removing a second metadata
# model that could drift from executable modules.
OfficialAlgorithmManifest = AlgorithmManifest


class OfficialAlgorithmCatalogItem(BaseModel):
    """Read-only client catalog; intentionally excludes graph and adapter details."""

    algorithm_id: str
    display_name_zh: str
    abbreviation: str
    purpose_zh: str
    scientific_version: str
    implementation_identity: str
    execution_kind: OfficialExecutionKind
    availability: OfficialAvailability
    is_runnable: bool
    required_channel_roles: list[str]
    supported_modes: list[str]
    output_schema: dict[str, object]
    definition_id: str
    definition_version: str
