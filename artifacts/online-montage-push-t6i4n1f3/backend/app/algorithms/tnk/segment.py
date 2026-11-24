"""Segment/report TNK modules whose semantics are not a cursor window."""

from __future__ import annotations

import numpy as np
from pydantic import Field

from .contracts import TnkConfig, TnkManifest, TnkSpectralWindow, band_power, result, validate_window


class FaaConfig(TnkConfig):
    min_clean_epochs: int = Field(default=10, ge=1)


class FaaModule:
    manifest = TnkManifest(
        algorithm_id="tnk_faa", display_name_zh="TNK 额叶 Alpha 不对称",
        scientific_version="tnk-faa-v1", implementation_identity="tnk-faa-module-v1",
        channel_roles=["F3", "F4"], output_unit="ln_ratio", stateful=False,
        supported_modes=["segment_report"], failure_codes=["TNK_SEGMENT_REQUIRED", "TNK_TOO_FEW_CLEAN_EPOCHS", "TNK_POWER_INVALID"],
    )

    def compute(self, window: TnkSpectralWindow, config: FaaConfig):
        failure = validate_window(self.manifest, window)
        if failure:
            return result(self.manifest, window, None, quality="unavailable", failure=failure)
        if window.provenance.get("segment_complete") is not True:
            return result(self.manifest, window, None, quality="unavailable",
                          failure=_failure("TNK_SEGMENT_REQUIRED", "TNK FAA 需要完整事件时段，不支持任意游标窗口"))
        clean_epochs = int(window.provenance.get("clean_epochs", 0))
        if clean_epochs < config.min_clean_epochs:
            return result(self.manifest, window, None, quality="unavailable",
                          failure=_failure("TNK_TOO_FEW_CLEAN_EPOCHS", "TNK FAA 干净 epoch 数不足", {"clean_epochs": clean_epochs}))
        f3 = band_power(window.frequencies_hz, window.psd_by_role["F3"], 8, 13)
        f4 = band_power(window.frequencies_hz, window.psd_by_role["F4"], 8, 13)
        if f3 <= 0 or f4 <= 0:
            return result(self.manifest, window, None, quality="unavailable", failure=_failure("TNK_POWER_INVALID", "TNK FAA Alpha 功率无效"))
        return result(self.manifest, window, float(np.log(f4) - np.log(f3)),
                      evidence={"clean_epochs": clean_epochs, "band_hz": [8.0, 13.0], "segment_complete": True})


class CompositeMeditationModule:
    manifest = TnkManifest(
        algorithm_id="tnk_composite_meditation", display_name_zh="TNK 综合冥想指数",
        scientific_version="tnk-composite-meditation-v1", implementation_identity="tnk-composite-meditation-module-v1",
        channel_roles=["dependencies"], output_unit="score", stateful=True,
        failure_codes=["TNK_DEPENDENCY_MISSING", "TNK_DEPENDENCY_DEGRADED"],
    )

    def compute(self, window: TnkSpectralWindow, config: TnkConfig):
        spatial = window.provenance.get("spatial_distribution")
        stability = window.provenance.get("rhythm_stability")
        stability_ready = bool(window.provenance.get("stability_ready", False))
        if spatial is None or stability is None:
            return result(self.manifest, window, None, quality="unavailable",
                          failure=_failure("TNK_DEPENDENCY_MISSING", "TNK 综合指数缺少依赖指标"))
        spatial_valid = bool(window.provenance.get("spatial_valid", False))
        if stability_ready and spatial_valid:
            value, weights, quality = 0.8 * float(stability) + 0.2 * float(spatial), {"stability": 0.8, "spatial_distribution": 0.2}, "clean"
        elif stability_ready:
            value, weights, quality = float(stability), {"stability": 1.0, "spatial_distribution": 0.0}, "degraded"
        else:
            value, weights, quality = float(spatial), {"stability": 0.0, "spatial_distribution": 1.0}, "degraded"
        return result(self.manifest, window, value, quality=quality,
                      evidence={"weights": weights, "dependencies": {"spatial_distribution": spatial, "rhythm_stability": stability}, "spatial_valid": spatial_valid, "stability_ready": stability_ready})


def _failure(code: str, message: str, detail: dict | None = None):
    from app.algorithm_runtime.contracts import AlgorithmFailure
    return AlgorithmFailure(code=code, message=message, detail=detail or {})
