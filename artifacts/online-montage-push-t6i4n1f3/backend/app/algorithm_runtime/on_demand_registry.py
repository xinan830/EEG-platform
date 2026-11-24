"""Closed registry for validated opt-in dynamic-window capabilities."""

from __future__ import annotations

from .errors import UnsupportedDynamicWindowCapabilityError
from .on_demand import DynamicWindowCapability


class DynamicWindowCapabilityRegistry:
    """Resolve only explicitly declared capabilities; never fall back to full Run."""

    def __init__(self, capabilities: list[DynamicWindowCapability] | None = None) -> None:
        self._capabilities = {}
        for capability in capabilities or []:
            self.register(capability)

    @classmethod
    def from_algorithm_registry(cls, registry) -> "DynamicWindowCapabilityRegistry":
        return cls([
            module for module in registry.list()
            if isinstance(module, DynamicWindowCapability)
        ])

    def register(self, capability: DynamicWindowCapability) -> None:
        manifest = capability.manifest
        if "dynamic" not in manifest.supported_modes:
            raise ValueError("dynamic capability requires a module that supports dynamic mode")
        key = (manifest.algorithm_id, manifest.scientific_version)
        if key in self._capabilities:
            raise ValueError(f"duplicate dynamic capability: {key[0]}@{key[1]}")
        self._capabilities[key] = capability

    def get(self, algorithm_id: str, scientific_version: str) -> DynamicWindowCapability:
        capability = self._capabilities.get((algorithm_id, scientific_version))
        if capability is None:
            raise UnsupportedDynamicWindowCapabilityError(
                f"algorithm {algorithm_id!r} has no validated on-demand capability",
                detail={"algorithm_id": algorithm_id, "scientific_version": scientific_version},
            )
        return capability

    def list(self) -> list[DynamicWindowCapability]:
        return sorted(self._capabilities.values(), key=lambda item: (
            item.manifest.algorithm_id, item.manifest.scientific_version,
        ))
