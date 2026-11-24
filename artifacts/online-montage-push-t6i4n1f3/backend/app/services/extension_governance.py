"""Validate extension declarations without loading or granting plugins."""

from __future__ import annotations

from app.models.extension_manifest import ExtensionManifest


SCIENTIFIC_TYPES = (
    "EEGSignal", "WindowedSignal", "PSDSeries", "BandPower", "RelativePower",
    "Scalar", "TimeSeries", "ChannelMap", "QualityMask",
)
DECLARED_PERMISSIONS = (
    "read_recording_metadata", "read_derived_artifacts", "write_derived_artifacts",
)
EXECUTION_BOUNDARIES = {
    "installed_plugins": False,
    "arbitrary_python": False,
    "network_access": False,
    "subprocess_access": False,
    "filesystem_access": False,
    "manifest_persistence": False,
    "signature_verification": False,
    "multi_tenancy": False,
    "marketplace": False,
}


class ExtensionGovernanceError(ValueError):
    code = "EXTENSION_MANIFEST_INVALID"


class ExtensionGovernanceService:
    """Closed vocabulary validator for future-safe local module metadata."""

    def capabilities(self) -> dict[str, object]:
        return {
            "manifest_schema_version": "local-module-manifest-v1",
            "sources": ["local_official", "research_template", "user_private"],
            "execution_modes": ["closed_definition_graph", "metadata_only"],
            "validation_states": ["draft", "engineering_verified", "research_use", "deprecated"],
            "scientific_types": list(SCIENTIFIC_TYPES),
            "declared_permissions": list(DECLARED_PERMISSIONS),
            "boundaries": EXECUTION_BOUNDARIES,
        }

    def validate(self, manifest: ExtensionManifest) -> dict[str, object]:
        unknown_types = sorted(set(manifest.input_types + manifest.output_types) - set(SCIENTIFIC_TYPES))
        if unknown_types:
            raise ExtensionGovernanceError("unknown scientific types: " + ", ".join(unknown_types))
        if manifest.execution_mode != "closed_definition_graph" and manifest.permissions:
            raise ExtensionGovernanceError("metadata-only modules cannot declare permissions")
        return {
            "valid": True,
            "manifest": manifest.model_dump(mode="json"),
            "permission_semantics": "declarative_only_not_granted",
            "execution_boundaries": EXECUTION_BOUNDARIES,
        }
