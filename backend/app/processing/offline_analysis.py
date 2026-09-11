"""离线分析用例编排；领域公式位于 eeg_core，避免与 API/存储耦合。"""

from __future__ import annotations

from dataclasses import asdict, dataclass
from typing import Any

import numpy as np

from app.eeg_core.analysis_contract import ANALYSIS_CONTRACT
from app.eeg_core.faa import FAA_DISCARD_S, compute_faa
from app.eeg_core.offline_metrics import estimate_iapf, metric_values
from app.eeg_core.spectral import estimate_welch_psd, preprocess_offline
from app.models.recording import ChannelMapping


@dataclass(frozen=True)
class IAPFAttempt:
    elapsed_s: float
    iapf: float | None
    source: str | None
    signal_quality: float
    calibrated: bool
    gate_failed: str | None
    model_r2: float | None
    model_error: float | None
    gaussian_cf: float | None
    peak_hz: float | None
    cog: float | None


@dataclass(frozen=True)
class MetricPoint:
    elapsed_s: float
    relaxation: float | None
    spatial_distribution: float | None
    rhythm_stability: float | None
    brainbeat: float | None
    fatigue: dict[str, float]
    signal_quality: float
    gate_failed: str | None
    iapf_hz: float
    iapf_is_fallback: bool
    iapf_source: str


@dataclass(frozen=True)
class AnalysisResult:
    duration_s: float
    locked_iapf: float | None
    iapf_attempts: list[IAPFAttempt]
    metrics: list[MetricPoint]
    events: list[dict[str, Any]]
    waveform: dict[str, Any]
    algorithm_contract: dict[str, object]
    faa: dict[str, object]
    analysis_input: dict[str, object]

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)


def _downsample_waveform(data: np.ndarray, sfreq: float, channel_names: list[str], max_points: int = 4000) -> dict[str, Any]:
    step = max(1, int(np.ceil(len(data) / max_points)))
    sampled = data[::step]
    return {
        "elapsed_s": (np.arange(len(sampled)) * step / sfreq).round(6).tolist(),
        "channels": {name: (sampled[:, index] * 1e6).round(4).tolist() for index, name in enumerate(channel_names)},
        "sampling_step": step, "unit": "uV", "source": "raw_no_software_rereference",
    }


def _rhythm_stability(history: dict[str, list[float]], narrow_alpha: list[float] | None) -> float | None:
    if narrow_alpha is None:
        return None
    names = ("Fz", "Pz", "Oz")
    weights = {"Fz": 0.2, "Pz": 0.4, "Oz": 0.4}
    for name, value in zip(names, narrow_alpha):
        history[name].append(value)
    history_points = int(ANALYSIS_CONTRACT["rhythm_stability_history_points"])
    if any(len(history[name]) < history_points for name in names):
        return None
    scores = {}
    for name in names:
        recent = np.asarray(history[name][-history_points:], dtype=float)
        mean = float(np.mean(recent))
        scores[name] = 1.0 / (1.0 + float(np.std(recent) / mean)) if mean > 0 else 0.0
    return float(sum(weights[name] * scores[name] for name in names))


def analyze_recording(data: np.ndarray, sfreq: float, mapping: ChannelMapping, channel_names: list[str], events: list[dict[str, Any]]) -> AnalysisResult:
    values = np.asarray(data, dtype=float)
    if values.ndim != 2 or len(values) == 0:
        raise ValueError("脑电数据必须是非空二维数组")
    if not np.isfinite(values).all():
        raise ValueError("脑电数据包含非有限值")
    name_to_index = {name.upper(): index for index, name in enumerate(channel_names)}
    ordered_names = [mapping.fz, mapping.pz, mapping.oz]
    try:
        ordered_raw = values[:, [name_to_index[name.upper()] for name in ordered_names]]
    except KeyError as exc:
        raise ValueError(f"缺少映射通道: {exc.args[0]}") from exc
    ordered = preprocess_offline(ordered_raw, sfreq)
    faa: dict[str, object] = {"faa": None, "reason": "channels_unavailable", "scope": "full_recording_after_12_seconds"}
    if mapping.f3 and mapping.f4:
        try:
            frontal_raw = values[:, [name_to_index[mapping.f3.upper()], name_to_index[mapping.f4.upper()]]]
        except KeyError as exc:
            raise ValueError(f"缺少 FAA 映射通道: {exc.args[0]}") from exc
        frontal = preprocess_offline(frontal_raw, sfreq)
        discard = int(round(FAA_DISCARD_S * sfreq))
        faa = {**compute_faa(frontal[discard:, 0], frontal[discard:, 1], sfreq), "scope": "full_recording_after_12_seconds"}

    iapf_window = int(round(float(ANALYSIS_CONTRACT["iapf_window_s"]) * sfreq))
    iapf_step = int(round(float(ANALYSIS_CONTRACT["iapf_step_s"]) * sfreq))
    attempts: list[IAPFAttempt] = []
    candidates: list[float] = []
    locked_iapf: float | None = None
    last_iapf = 10.0
    for end in range(iapf_window, len(ordered) + 1, iapf_step):
        spectrum = estimate_welch_psd(ordered[end - iapf_window:end], sfreq)
        estimate = estimate_iapf(spectrum)
        if estimate.value is not None:
            last_iapf = estimate.value
            if locked_iapf is None:
                candidates.append(estimate.value)
                if len(candidates) >= int(ANALYSIS_CONTRACT["iapf_lock_candidates"]):
                    locked_iapf = float(np.median(candidates))
        attempts.append(IAPFAttempt(
            end / sfreq, estimate.value, estimate.source, spectrum.signal_quality,
            estimate.value is not None, estimate.gate_failed, estimate.model_r2,
            estimate.model_error, None, estimate.peak_hz, estimate.cog,
        ))

    metric_window = int(round(float(ANALYSIS_CONTRACT["welch_epoch_s"]) * sfreq))
    metric_start = int(round(float(ANALYSIS_CONTRACT["metric_start_s"]) * sfreq))
    active_iapf = locked_iapf if locked_iapf is not None else last_iapf
    active_iapf_source = "global_locked" if locked_iapf is not None else ("last_candidate" if candidates else "default_10Hz")
    history = {name: [] for name in ("Fz", "Pz", "Oz")}
    metrics = []
    for end in range(metric_start, len(ordered) + 1, int(round(sfreq))):
        spectrum = estimate_welch_psd(ordered[end - metric_window:end], sfreq)
        point = metric_values(spectrum, active_iapf)
        metrics.append(MetricPoint(
            elapsed_s=end / sfreq, relaxation=point["relaxation"],
            spatial_distribution=point["spatial_distribution"],
            rhythm_stability=_rhythm_stability(history, point["narrow_alpha"]),
            brainbeat=point["brainbeat"], fatigue=point["fatigue"],
            signal_quality=spectrum.signal_quality, gate_failed=spectrum.gate_failed,
            iapf_hz=active_iapf, iapf_is_fallback=locked_iapf is None,
            iapf_source=active_iapf_source,
        ))
    return AnalysisResult(
        duration_s=float(len(values) / sfreq), locked_iapf=locked_iapf,
        iapf_attempts=attempts, metrics=metrics, events=list(events),
        waveform=_downsample_waveform(values, sfreq, channel_names),
        algorithm_contract=dict(ANALYSIS_CONTRACT),
        faa=faa,
        analysis_input={
            "sfreq_hz": float(sfreq), "sample_count": len(values), "input_unit": "V",
            "mapped_channels": {"fz": mapping.fz, "pz": mapping.pz, "oz": mapping.oz, "f3": mapping.f3, "f4": mapping.f4},
            "reference": "original_recording_no_software_rereference",
        },
    )
