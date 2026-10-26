"""Stateless TNK window modules.

Each class owns its formula and failure policy. The caller supplies the
normalized PSD window; no class imports a platform calculator or reads files.
"""

from __future__ import annotations

from typing import Protocol

import numpy as np
from pydantic import Field

from .contracts import (
    TnkConfig, TnkManifest, TnkSpectralWindow, band_power, result, validate_window,
)


class TnkWindowModule(Protocol):
    manifest: TnkManifest

    def compute(self, window: TnkSpectralWindow, config: TnkConfig): ...


class RelaxationConfig(TnkConfig):
    iapf_hz: float = Field(gt=0)


class SpatialConfig(RelaxationConfig):
    pass


class RbpConfig(TnkConfig):
    pass


class HaiConfig(TnkConfig):
    pass


class AperiodicConfig(TnkConfig):
    fit_low_hz: float = Field(default=3.0, gt=0)
    fit_high_hz: float = Field(default=30.0, gt=0)


def _failure(code: str, message: str, detail: dict | None = None):
    from app.algorithm_runtime.contracts import AlgorithmFailure
    return AlgorithmFailure(code=code, message=message, detail=detail or {})


class RelaxationModule:
    manifest = TnkManifest(
        algorithm_id="tnk_relaxation", display_name_zh="TNK 放松度",
        scientific_version="tnk-relaxation-v1", implementation_identity="tnk-relaxation-module-v1",
        channel_roles=["Pz", "Oz"], output_unit="score", stateful=False,
        failure_codes=["TNK_CHANNEL_ROLE_MISSING", "TNK_POWER_INVALID"],
    )

    def compute(self, window: TnkSpectralWindow, config: RelaxationConfig):
        failure = validate_window(self.manifest, window)
        if failure:
            return result(self.manifest, window, None, quality="unavailable", failure=failure)
        total = alpha = 0.0
        for role in self.manifest.channel_roles:
            psd = window.psd_by_role[role]
            alpha += band_power(window.frequencies_hz, psd, config.iapf_hz - 1.0, config.iapf_hz + 1.0)
            total += band_power(window.frequencies_hz, psd, 1.0, 30.0)
        if total <= 0 or not np.isfinite(total):
            failure = _failure("TNK_POWER_INVALID", "TNK 放松度总功率无效", {"total_power_v2": total})
            return result(self.manifest, window, None, quality="unavailable", failure=failure)
        ratio = alpha / total
        score = float(100.0 / (1.0 + np.exp(-(ratio - 0.15) / 0.055)))
        return result(self.manifest, window, score, evidence={"ratio": ratio, "iapf_hz": config.iapf_hz})


class SpatialDistributionModule:
    manifest = TnkManifest(
        algorithm_id="tnk_spatial_distribution", display_name_zh="TNK 空间分布",
        scientific_version="tnk-spatial-distribution-v1", implementation_identity="tnk-spatial-distribution-module-v1",
        channel_roles=["Fz", "Pz", "Oz"], output_unit="score", stateful=False,
        failure_codes=["TNK_CHANNEL_ROLE_MISSING", "TNK_POWER_INVALID", "TNK_SPATIAL_LOW_POSTERIOR"],
    )

    def compute(self, window: TnkSpectralWindow, config: SpatialConfig):
        failure = validate_window(self.manifest, window)
        if failure:
            return result(self.manifest, window, None, quality="unavailable", failure=failure)
        fz = window.psd_by_role["Fz"]
        posterior = np.mean(np.vstack([window.psd_by_role["Pz"], window.psd_by_role["Oz"]]), axis=0)
        frontal_total = band_power(window.frequencies_hz, fz, 1.0, 30.0)
        posterior_total = band_power(window.frequencies_hz, posterior, 1.0, 30.0)
        frontal = band_power(window.frequencies_hz, fz, config.iapf_hz - 2, config.iapf_hz + 2) / frontal_total if frontal_total > 0 else 0
        posterior_ratio = band_power(window.frequencies_hz, posterior, config.iapf_hz - 2, config.iapf_hz + 2) / posterior_total if posterior_total > 0 else 0
        if posterior_ratio <= 0:
            return result(self.manifest, window, None, quality="unavailable", failure=_failure("TNK_POWER_INVALID", "TNK 后部功率无效"))
        score = float(100.0 / (1.0 + np.exp(-((frontal / posterior_ratio) - 0.45) / 0.15)))
        quality = "clean" if posterior_ratio >= 0.02 else "degraded"
        return result(self.manifest, window, score, quality=quality,
                      evidence={"frontal_ratio": frontal, "posterior_ratio": posterior_ratio, "valid": quality == "clean", "iapf_hz": config.iapf_hz})


class HaiModule:
    manifest = TnkManifest(
        algorithm_id="tnk_hai", display_name_zh="TNK HAI",
        scientific_version="tnk-hai-v1", implementation_identity="tnk-hai-module-v1",
        channel_roles=["Fz", "Pz", "Oz"], output_unit="log10_ratio", stateful=False,
        failure_codes=["TNK_CHANNEL_ROLE_MISSING", "TNK_POWER_INVALID"],
    )

    def compute(self, window: TnkSpectralWindow, config: HaiConfig):
        failure = validate_window(self.manifest, window)
        if failure:
            return result(self.manifest, window, None, quality="unavailable", failure=failure)
        values: dict[str, float | None] = {}
        for role in self.manifest.channel_roles:
            psd = window.psd_by_role[role]
            low = band_power(window.frequencies_hz, psd, 1, 8)
            beta = band_power(window.frequencies_hz, psd, 13, 25)
            values[role] = float(np.log10(beta / low)) if low > 0 and beta > 0 else None
        if all(value is None for value in values.values()):
            return result(self.manifest, window, None, quality="unavailable", failure=_failure("TNK_POWER_INVALID", "TNK HAI 频带功率无效"), output_values=values)
        return result(self.manifest, window, None, output_values=values, evidence={"bands_hz": {"low": [1, 8], "beta": [13, 25]}})


class RbpModule:
    manifest = TnkManifest(
        algorithm_id="tnk_rbp", display_name_zh="TNK 固定频段相对功率",
        scientific_version="tnk-rbp-v1", implementation_identity="tnk-rbp-module-v1",
        channel_roles=["channel"], output_unit="ratio", stateful=False,
        failure_codes=["TNK_CHANNEL_ROLE_MISSING", "TNK_POWER_INVALID"],
    )
    bands = {"delta": (1.0, 4.0), "theta": (4.0, 8.0), "alpha": (8.0, 13.0), "beta": (13.0, 30.0)}

    def compute(self, window: TnkSpectralWindow, config: RbpConfig):
        failure = validate_window(self.manifest, window)
        if failure:
            return result(self.manifest, window, None, quality="unavailable", failure=failure)
        psd = window.psd_by_role["channel"]
        powers = {name: band_power(window.frequencies_hz, psd, *band) for name, band in self.bands.items()}
        denominator = sum(powers.values())
        if denominator <= 0 or not np.isfinite(denominator):
            return result(self.manifest, window, None, quality="unavailable", failure=_failure("TNK_POWER_INVALID", "TNK RBP 分母无效"), output_values={name: None for name in powers})
        values = {name: power / denominator for name, power in powers.items()}
        return result(self.manifest, window, None, output_values=values, evidence={"band_powers_v2_per_hz": powers, "denominator": denominator})


class AperiodicModule:
    manifest = TnkManifest(
        algorithm_id="tnk_aperiodic", display_name_zh="TNK 非周期指数",
        scientific_version="tnk-aperiodic-v1", implementation_identity="tnk-aperiodic-module-v1",
        channel_roles=["Fz", "Pz", "Oz"], output_unit="exponent", stateful=False,
        failure_codes=["TNK_CHANNEL_ROLE_MISSING", "TNK_FIT_INVALID"],
    )

    def compute(self, window: TnkSpectralWindow, config: AperiodicConfig):
        failure = validate_window(self.manifest, window)
        if failure:
            return result(self.manifest, window, None, quality="unavailable", failure=failure)
        try:
            from specparam import SpectralModel
        except ImportError as exc:
            return result(self.manifest, window, None, quality="unavailable",
                          failure=_failure("TNK_FIT_INVALID", "TNK 非周期拟合依赖 specparam", {"error": str(exc)}))
        values, offsets, fit_quality = {}, {}, {}
        for role in self.manifest.channel_roles:
            psd = window.psd_by_role[role]
            mask = (window.frequencies_hz >= config.fit_low_hz) & (window.frequencies_hz <= config.fit_high_hz) & (psd > 0)
            if np.count_nonzero(mask) < 3:
                values[role] = None
                continue
            frequencies = window.frequencies_hz[mask]
            model = SpectralModel(peak_width_limits=[1.0, 8.0], max_n_peaks=4, peak_threshold=1.5, verbose=False)
            try:
                model.fit(frequencies, psd[mask], [config.fit_low_hz, config.fit_high_hz])
                params = model.results.params
                metrics = {item.measure: item.result for item in model.results.metrics.metrics}
                values[role] = float(params.aperiodic._fit[1])
                offsets[role] = float(params.aperiodic._fit[0])
                fit_quality[role] = {"r_squared": float(metrics["rsquared"]), "mae": float(metrics["mae"])}
            except (KeyError, TypeError, ValueError, AttributeError):
                values[role] = None
        if all(value is None for value in values.values()):
            return result(self.manifest, window, None, quality="unavailable", failure=_failure("TNK_FIT_INVALID", "TNK 非周期拟合不可用"), output_values=values)
        return result(self.manifest, window, None, output_values=values,
                      evidence={"fit_range_hz": [config.fit_low_hz, config.fit_high_hz], "offsets": offsets, "fit_quality": fit_quality})


def build_stateless_modules() -> tuple[TnkWindowModule, ...]:
    return (RbpModule(), HaiModule(), RelaxationModule(), SpatialDistributionModule(), AperiodicModule())
