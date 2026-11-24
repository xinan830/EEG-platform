"""TNK stateful window modules with explicit reset and replay state."""

from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np
from pydantic import Field

from .contracts import TnkConfig, TnkManifest, TnkSpectralWindow, band_power, result, validate_window


class StatefulConfig(TnkConfig):
    iapf_hz: float = Field(default=10.0, gt=0)
    ema_alpha: float = Field(default=0.15, gt=0, lt=1)
    warmup_count: int = Field(default=3, ge=1)


@dataclass
class LogEma:
    warmup_count: int = 3
    alpha: float = 0.15
    pending: list[float] = field(default_factory=list)
    log_value: float | None = None

    def reset(self) -> None:
        self.pending.clear()
        self.log_value = None

    def update(self, ratio: float | None) -> tuple[float | None, str]:
        if ratio is None or not np.isfinite(ratio) or ratio <= 0:
            return None, "rejected"
        logged = float(np.log(ratio))
        if self.log_value is None:
            self.pending.append(logged)
            if len(self.pending) < self.warmup_count:
                return None, "warmup"
            self.log_value = float(np.mean(self.pending))
            self.pending.clear()
            return float(np.exp(self.log_value)), "ready"
        self.log_value = self.alpha * logged + (1.0 - self.alpha) * self.log_value
        return float(np.exp(self.log_value)), "ready"


@dataclass
class History:
    capacity: int = 10
    minimum_ready: int = 8
    by_role: dict[str, list[float]] = field(default_factory=dict)

    def reset(self) -> None:
        self.by_role.clear()

    def update(self, role: str, value: float | None) -> tuple[float, bool] | None:
        if value is None or not np.isfinite(value):
            return None
        values = self.by_role.setdefault(role, [])
        values.append(float(value))
        del values[:-self.capacity]
        if len(values) < self.minimum_ready:
            return 0.65, False
        mean = float(np.mean(values))
        cv = float(np.std(values) / mean) if mean > 0 else 1.0
        return 1.0 / (1.0 + cv), True


class BrainbeatModule:
    manifest = TnkManifest(
        algorithm_id="tnk_brainbeat", display_name_zh="TNK BrainBeat",
        scientific_version="tnk-brainbeat-v1", implementation_identity="tnk-brainbeat-module-v1",
        channel_roles=["Fz", "Pz"], output_unit="ratio", stateful=True,
        failure_codes=["TNK_CHANNEL_ROLE_MISSING", "TNK_POWER_INVALID", "TNK_WARMUP"],
    )

    def __init__(self) -> None:
        self.ema = LogEma()

    def reset(self) -> None:
        self.ema.reset()

    def compute(self, window: TnkSpectralWindow, config: StatefulConfig):
        failure = validate_window(self.manifest, window)
        if failure:
            return result(self.manifest, window, None, quality="unavailable", failure=failure, evidence={"state": "rejected"})
        theta = band_power(window.frequencies_hz, window.psd_by_role["Fz"], max(4, config.iapf_hz - 6), config.iapf_hz - 2)
        alpha = band_power(window.frequencies_hz, window.psd_by_role["Pz"], config.iapf_hz - 2, config.iapf_hz + 2)
        fz_total = band_power(window.frequencies_hz, window.psd_by_role["Fz"], 1, 30)
        pz_total = band_power(window.frequencies_hz, window.psd_by_role["Pz"], 1, 30)
        ratio = (theta / fz_total) / (alpha / pz_total) if theta > 0 and alpha > 0 and fz_total > 0 and pz_total > 0 else None
        self.ema.warmup_count, self.ema.alpha = config.warmup_count, config.ema_alpha
        value, state = self.ema.update(ratio)
        if state == "rejected":
            return result(self.manifest, window, None, quality="unavailable", failure=_failure("TNK_POWER_INVALID", "TNK BrainBeat 功率无效"), evidence={"state": state})
        if state == "warmup":
            return result(self.manifest, window, None, quality="insufficient_data", failure=_failure("TNK_WARMUP", "TNK BrainBeat 尚未完成 warm-up"), evidence={"state": state, "pending": len(self.ema.pending)})
        return result(self.manifest, window, value, evidence={"state": state, "raw_ratio": ratio, "iapf_hz": config.iapf_hz})


class BrainbeatFlatModule(BrainbeatModule):
    manifest = TnkManifest(
        algorithm_id="tnk_brainbeat_flat", display_name_zh="TNK BrainBeat Flat",
        scientific_version="tnk-brainbeat-flat-v2", implementation_identity="tnk-brainbeat-flat-current-window-fit-v2",
        channel_roles=["Fz", "Pz"], output_unit="ratio", stateful=True,
        failure_codes=["TNK_CHANNEL_ROLE_MISSING", "TNK_POWER_INVALID", "TNK_FIT_INVALID", "TNK_WARMUP"],
    )

    def compute(self, window: TnkSpectralWindow, config: StatefulConfig):
        failure = validate_window(self.manifest, window)
        if failure:
            return result(self.manifest, window, None, quality="unavailable", failure=failure)
        fits, offsets, fit_quality = {}, {}, {}
        try:
            from specparam import SpectralModel
        except ImportError as exc:
            return result(self.manifest, window, None, quality="unavailable",
                          failure=_failure("TNK_FIT_INVALID", "TNK BrainBeat Flat 拟合依赖 specparam", {"error": str(exc)}))
        for role in self.manifest.channel_roles:
            psd = window.psd_by_role[role]
            mask = (window.frequencies_hz >= 3.0) & (window.frequencies_hz <= 30.0) & (psd > 0)
            if np.count_nonzero(mask) < 3:
                continue
            try:
                model = SpectralModel(peak_width_limits=[1.0, 8.0], max_n_peaks=4, peak_threshold=1.5, verbose=False)
                model.fit(window.frequencies_hz[mask], psd[mask], [3.0, 30.0])
                params = model.results.params
                fits[role] = {"offset": float(params.aperiodic._fit[0]),
                              "exponent": float(params.aperiodic._fit[1])}
                offsets[role] = fits[role]["offset"]
                fit_quality[role] = {item.measure: float(item.result) for item in model.results.metrics.metrics}
            except (KeyError, TypeError, ValueError, AttributeError):
                continue
        if any(role not in fits for role in self.manifest.channel_roles):
            return result(self.manifest, window, None, quality="unavailable",
                          failure=_failure("TNK_FIT_INVALID", "TNK BrainBeat Flat 当前窗口逐通道非周期拟合不可用",
                                           {"available_roles": sorted(fits)}),
                          evidence={"fit_range_hz": [3.0, 30.0], "offsets": offsets, "fit_quality": fit_quality})
        window.provenance["aperiodic_fit_by_role"] = {
            role: {**fits[role], "fit_start_s": window.start_sample / window.sfreq_hz,
                   "fit_end_s": window.end_sample / window.sfreq_hz, "fit_quality": fit_quality.get(role)}
            for role in self.manifest.channel_roles
        }
        def residual(role: str) -> np.ndarray:
            fit = fits[role]
            f = window.frequencies_hz
            baseline = np.zeros_like(f)
            positive = f > 0
            baseline[positive] = 10 ** (float(fit["offset"]) - float(fit["exponent"]) * np.log10(f[positive]))
            return np.clip(window.psd_by_role[role] - baseline, 0, None)
        fz, pz = residual("Fz"), residual("Pz")
        theta = band_power(window.frequencies_hz, fz, max(4, config.iapf_hz - 6), config.iapf_hz - 2)
        alpha = band_power(window.frequencies_hz, pz, config.iapf_hz - 2, config.iapf_hz + 2)
        fz_total = band_power(window.frequencies_hz, fz, 1, 30)
        pz_total = band_power(window.frequencies_hz, pz, 1, 30)
        ratio = (theta / fz_total) / (alpha / pz_total) if theta > 0 and alpha > 0 and fz_total > 0 and pz_total > 0 else None
        self.ema.warmup_count, self.ema.alpha = config.warmup_count, config.ema_alpha
        value, state = self.ema.update(ratio)
        if state == "warmup":
            return result(self.manifest, window, None, quality="insufficient_data", failure=_failure("TNK_WARMUP", "TNK BrainBeat Flat 尚未完成 warm-up"), evidence={"state": state})
        if state == "rejected":
            return result(self.manifest, window, None, quality="unavailable", failure=_failure("TNK_POWER_INVALID", "TNK BrainBeat Flat 残差功率无效"), evidence={"state": state})
        return result(self.manifest, window, value, evidence={
            "state": state, "raw_ratio": ratio, "fit_range_hz": [3.0, 30.0],
            "offsets": offsets, "fit_quality": fit_quality,
        })


class FatigueModule:
    manifest = TnkManifest(
        algorithm_id="tnk_fatigue", display_name_zh="TNK 疲劳指数",
        scientific_version="tnk-fatigue-v1", implementation_identity="tnk-fatigue-module-v1",
        channel_roles=["Fz", "Pz", "Oz"], output_unit="ratio", stateful=True,
        failure_codes=["TNK_CHANNEL_ROLE_MISSING", "TNK_POWER_INVALID", "TNK_WARMUP"],
    )

    def __init__(self) -> None:
        self.ema = {role: LogEma() for role in self.manifest.channel_roles}

    def reset(self) -> None:
        for state in self.ema.values():
            state.reset()

    def compute(self, window: TnkSpectralWindow, config: StatefulConfig):
        failure = validate_window(self.manifest, window)
        if failure:
            return result(self.manifest, window, None, quality="unavailable", failure=failure)
        values: dict[str, float | None] = {}
        states: dict[str, str] = {}
        for role, state in self.ema.items():
            state.warmup_count, state.alpha = config.warmup_count, config.ema_alpha
            psd = window.psd_by_role[role]
            theta = band_power(window.frequencies_hz, psd, max(4, config.iapf_hz - 6), config.iapf_hz - 2)
            beta = band_power(window.frequencies_hz, psd, config.iapf_hz + 2, 30)
            values[role], states[role] = state.update(theta / beta if theta > 0 and beta > 0 else None)
        ready = [value for value in values.values() if value is not None]
        if not ready:
            return result(self.manifest, window, None, quality="insufficient_data", failure=_failure("TNK_WARMUP", "TNK 疲劳指数尚未完成 warm-up"), output_values=values, evidence={"states": states})
        return result(self.manifest, window, None, output_values=values, evidence={"states": states, "iapf_hz": config.iapf_hz})


class RhythmStabilityModule:
    manifest = TnkManifest(
        algorithm_id="tnk_rhythm_stability", display_name_zh="TNK 节律稳定性",
        scientific_version="tnk-rhythm-stability-v1", implementation_identity="tnk-rhythm-stability-module-v1",
        channel_roles=["Fz", "Pz", "Oz"], output_unit="score", stateful=True,
        failure_codes=["TNK_CHANNEL_ROLE_MISSING", "TNK_POWER_INVALID"],
    )

    def __init__(self) -> None:
        self.history = History()

    def reset(self) -> None:
        self.history.reset()

    def compute(self, window: TnkSpectralWindow, config: StatefulConfig):
        failure = validate_window(self.manifest, window)
        if failure:
            return result(self.manifest, window, None, quality="unavailable", failure=failure)
        weights = {"Fz": 0.2, "Pz": 0.4, "Oz": 0.4}
        per_role: dict[str, float] = {}
        readiness: dict[str, bool] = {}
        for role in self.manifest.channel_roles:
            psd = window.psd_by_role[role]
            total = band_power(window.frequencies_hz, psd, 1, 30)
            alpha = band_power(window.frequencies_hz, psd, config.iapf_hz - 1, config.iapf_hz + 1)
            update = self.history.update(role, alpha / total if total > 0 else None)
            if update is not None:
                per_role[role], readiness[role] = update
        if not per_role:
            return result(self.manifest, window, None, quality="unavailable", failure=_failure("TNK_POWER_INVALID", "TNK 稳定性没有可用通道"))
        total_weight = sum(weights[role] for role in per_role)
        normalized = sum(weights[role] * value for role, value in per_role.items()) / total_weight
        score = float(np.clip((normalized - 0.4) / 0.5 * 100, 0, 100))
        return result(self.manifest, window, score, quality="clean" if all(readiness.values()) else "partial",
                      evidence={"per_role": per_role, "ready": readiness, "history_size": {r: len(self.history.by_role.get(r, [])) for r in per_role}})


class IapfModule:
    manifest = TnkManifest(
        algorithm_id="tnk_iapf", display_name_zh="TNK 个体 Alpha 峰频率",
        scientific_version="tnk-iapf-v1", implementation_identity="tnk-iapf-module-v1",
        channel_roles=["Pz", "Oz"], output_unit="Hz", stateful=True,
        supported_modes=["window", "report"], failure_codes=["TNK_CHANNEL_ROLE_MISSING", "TNK_CANDIDATE_INVALID", "TNK_WARMUP"],
    )

    def __init__(self, required_candidates: int = 3) -> None:
        self.required_candidates = required_candidates
        self.candidates: list[float] = []
        self.global_hz: float | None = None

    def reset(self) -> None:
        self.candidates.clear()
        self.global_hz = None

    def compute(self, window: TnkSpectralWindow, config: StatefulConfig):
        failure = validate_window(self.manifest, window)
        if failure:
            return result(self.manifest, window, None, quality="unavailable", failure=failure)
        candidate, fit_evidence = self._estimate_candidate(window)
        if candidate is None:
            return result(self.manifest, window, None, quality="unavailable", failure=_failure("TNK_CANDIDATE_INVALID", "TNK IAPF 候选不可用"), evidence={"candidate_count": len(self.candidates), "locked": self.global_hz is not None, **fit_evidence})
        if self.global_hz is None:
            self.candidates.append(float(candidate))
            if len(self.candidates) >= self.required_candidates:
                self.global_hz = float(np.median(self.candidates))
        value = self.global_hz if self.global_hz is not None else float(candidate)
        return result(self.manifest, window, value, quality="clean" if self.global_hz is not None else "partial",
                      evidence={"live_hz": float(candidate), "global_hz": self.global_hz, "candidate_count": len(self.candidates), "locked": self.global_hz is not None, **fit_evidence})

    @staticmethod
    def _estimate_candidate(window: TnkSpectralWindow) -> tuple[float | None, dict[str, object]]:
        try:
            from specparam import SpectralModel
            frequencies = np.asarray(window.frequencies_hz, dtype=float)
            posterior = np.mean(np.vstack([window.psd_by_role["Pz"], window.psd_by_role["Oz"]]), axis=0)
            mask = (frequencies >= 3.0) & (frequencies <= 30.0) & (posterior > 0)
            model = SpectralModel(peak_width_limits=[1.0, 8.0], max_n_peaks=4, peak_threshold=1.5, verbose=False)
            model.fit(frequencies[mask], posterior[mask], [3.0, 30.0])
            params = model.results.params
            metrics = {item.measure: item.result for item in model.results.metrics.metrics}
            peaks = np.asarray(params.periodic._fit, dtype=float)
            if peaks.ndim == 1 and peaks.size:
                peaks = peaks.reshape(1, -1)
            candidates = peaks[(peaks[:, 0] >= 7.0) & (peaks[:, 0] <= 13.0)] if peaks.size else np.empty((0, 3))
            if not candidates.size or float(metrics.get("rsquared", 0.0)) < 0.80 or float(metrics.get("mae", np.inf)) > 0.20:
                return None, {"fit_r_squared": metrics.get("rsquared"), "fit_mae": metrics.get("mae")}
            peak = candidates[np.argmax(candidates[:, 1])]
            if float(peak[1]) < 0.20:
                return None, {"fit_r_squared": metrics.get("rsquared"), "fit_mae": metrics.get("mae"), "peak_prominence": float(peak[1])}
            return float(peak[0]), {"fit_r_squared": metrics.get("rsquared"), "fit_mae": metrics.get("mae"), "peak_prominence": float(peak[1])}
        except Exception:
            return None, {"fit_error": "specparam_unavailable_or_invalid"}


def _failure(code: str, message: str, detail: dict | None = None):
    from app.algorithm_runtime.contracts import AlgorithmFailure
    return AlgorithmFailure(code=code, message=message, detail=detail or {})


def build_stateful_modules() -> tuple[object, ...]:
    return (IapfModule(), BrainbeatModule(), BrainbeatFlatModule(), FatigueModule(), RhythmStabilityModule())
