"""Compatibility facade for the official algorithm registry package."""

from app.eeg_core.official_algorithms.registry import (
    OFFICIAL_ALGORITHM_MANIFESTS,
    ensure_official_definitions,
    official_definition,
    official_definition_draft,
)

# Kept for existing callers; registry manifests are now the source of truth.
OFFICIAL_DEFINITIONS = {
    manifest.algorithm_id: official_definition(manifest.algorithm_id)
    for manifest in OFFICIAL_ALGORITHM_MANIFESTS
}

__all__ = ["OFFICIAL_DEFINITIONS", "ensure_official_definitions", "official_definition", "official_definition_draft"]
