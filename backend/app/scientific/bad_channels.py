"""Deterministic EEG bad-channel detection and MNE spherical spline interpolation.

All signal inputs and outputs are sample-major float64 arrays in SI volts (V).
NaN discontinuities are explicitly preserved and never zero-filled.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Sequence
import warnings

import mne
import numpy as np
from scipy import signal

from app.scientific.contracts.preprocessing import STANDARD_MONTAGE_NAME as MONTAGE_NAME


@dataclass(frozen=True)
class BadChannelDetectionConfig:
    """Explainable deterministic thresholds for scalp EEG bad-channel screening."""

    max_nan_ratio: float = 0.20
    min_std_v: float = 0.5e-6
    min_ptp_v: float = 2.0e-6
    max_robust_span_v: float = 500e-6
    max_rms_v: float = 150e-6
    hf_low_hz: float = 40.0
    max_hf_power_ratio: float = 0.50
    min_hf_rms_v: float = 10e-6
    min_neighbor_correlation: float = 0.30
    min_channels_for_correlation: int = 4


@dataclass(frozen=True)
class BadChannelMetrics:
    channel: str
    is_eeg: bool
    is_bad: bool
    reasons: tuple[str, ...]
    nan_ratio: float
    std_uv: float | None
    ptp_uv: float | None
    robust_span_uv: float | None
    rms_uv: float | None
    hf_power_ratio: float | None
    max_neighbor_correlation: float | None
    can_interpolate: bool
    interpolation_unavailable_reason: str | None


@dataclass(frozen=True)
class BadChannelDetectionReport:
    sampling_rate_hz: float
    sample_count: int
    channels: tuple[BadChannelMetrics, ...]
    recommended_bad_channels: tuple[str, ...]
    montage_template: str = MONTAGE_NAME

    def to_dict(self) -> dict[str, object]:
        def _r(val: float | None, digits: int = 4) -> float | None:
            return None if val is None else round(val, digits)

        return {
            "sampling_rate_hz": self.sampling_rate_hz,
            "sample_count": self.sample_count,
            "montage_template": self.montage_template,
            "recommended_bad_channels": list(self.recommended_bad_channels),
            "channels": [
                {
                    "channel": item.channel, "is_eeg": item.is_eeg, "is_bad": item.is_bad,
                    "reasons": list(item.reasons), "nan_ratio": round(item.nan_ratio, 6),
                    "std_uv": _r(item.std_uv), "ptp_uv": _r(item.ptp_uv),
                    "robust_span_uv": _r(item.robust_span_uv), "rms_uv": _r(item.rms_uv),
                    "hf_power_ratio": _r(item.hf_power_ratio),
                    "max_neighbor_correlation": _r(item.max_neighbor_correlation),
                    "can_interpolate": item.can_interpolate,
                    "interpolation_unavailable_reason": item.interpolation_unavailable_reason,
                }
                for item in self.channels
            ],
        }


@dataclass(frozen=True)
class SphericalSplineInterpolationPlan:
    """Precomputed linear MNE spherical spline operator W: X_bad(t) = X_good(t) @ W.T."""

    channel_names: tuple[str, ...]
    bad_channels: tuple[str, ...]
    good_donor_channels: tuple[str, ...]
    bad_indices: tuple[int, ...]
    good_indices: tuple[int, ...]
    weights: np.ndarray  # shape (n_bad, n_good), float64
    montage_template: str
    origin_mode: str = "auto"

    def to_evidence(self) -> dict[str, object]:
        return {
            "method": "mne_spherical_spline",
            "montage_template": self.montage_template,
            "mne_version": mne.__version__,
            "bad_channels": list(self.bad_channels),
            "good_donor_channels": list(self.good_donor_channels),
            "origin_mode": self.origin_mode,
        }


def _standard_3d_lookup(montage_name: str = MONTAGE_NAME) -> dict[str, tuple[str, np.ndarray]]:
    montage = mne.channels.make_standard_montage(montage_name)
    positions = montage.get_positions()["ch_pos"]
    return {
        name.casefold(): (name, np.asarray(xyz, dtype=np.float64))
        for name, xyz in positions.items()
    }


def _channel_interpolation_eligibility(
    label: str,
    *,
    is_eeg: bool,
    kind: str | None,
    positions: dict[str, tuple[str, np.ndarray]],
) -> tuple[bool, str | None]:
    if not is_eeg:
        return False, "non_eeg_channel"
    if kind is not None and kind.strip().casefold() == "bipolar":
        return False, "bipolar_channel_not_interpolatable"
    if label.strip().casefold() not in positions:
        return False, "electrode_3d_position_unavailable"
    return True, None


def _bandpass_for_correlation(values_v: np.ndarray, sfreq_hz: float) -> np.ndarray | None:
    nyquist = sfreq_hz / 2.0
    high_hz = min(40.0, nyquist - 1.0)
    if high_hz <= 1.5 or values_v.shape[0] < 32:
        return None
    sos = signal.butter(4, [1.0, high_hz], btype="bandpass", fs=sfreq_hz, output="sos")
    try:
        return signal.sosfiltfilt(sos, values_v, axis=0)
    except ValueError:
        return None


def _hf_noise_metrics(column_v: np.ndarray, sfreq_hz: float, hf_low_hz: float) -> tuple[float | None, float | None]:
    nyquist = sfreq_hz / 2.0
    hf_high_hz = min(95.0, nyquist - 2.0)
    if hf_high_hz <= hf_low_hz + 2.0 or column_v.size < 64:
        return None, None
    nperseg = min(column_v.size, max(64, int(round(sfreq_hz * 2.0))))
    freqs, psd = signal.welch(column_v, fs=sfreq_hz, nperseg=nperseg, detrend="constant")
    eeg_mask = (freqs >= 1.0) & (freqs <= hf_low_hz)
    # Exclude narrowband 50/60 Hz mains hum so un-notched DC-coupled recordings do not false-flag broadband HF noise.
    mains_mask = ((freqs >= 48.0) & (freqs <= 52.0)) | ((freqs >= 58.0) & (freqs <= 62.0))
    hf_mask = (freqs >= hf_low_hz) & (freqs <= hf_high_hz) & (~mains_mask)
    if not np.any(eeg_mask) or not np.any(hf_mask):
        return None, None
    eeg_power = float(np.trapezoid(psd[eeg_mask], freqs[eeg_mask]))
    hf_power = float(np.trapezoid(psd[hf_mask], freqs[hf_mask]))
    if not np.isfinite(eeg_power) or not np.isfinite(hf_power) or eeg_power <= 0.0:
        return None, None
    return hf_power / eeg_power, float(np.sqrt(max(hf_power, 0.0)))


def _amplitude_eval_signal(finite_v: np.ndarray, sfreq_hz: float) -> np.ndarray:
    """Remove slow DC polarization drift (<0.5 Hz) and 50/60 Hz mains for amplitude screening on DC-coupled amps."""
    centered = finite_v - float(np.mean(finite_v))
    nyquist = sfreq_hz / 2.0
    if centered.size < 32 or nyquist <= 2.0:
        return centered
    work = centered
    try:
        sos_hp = signal.butter(4, 0.5, btype="highpass", fs=sfreq_hz, output="sos")
        work = signal.sosfiltfilt(sos_hp, work)
        for mains_hz in (50.0, 60.0):
            if mains_hz < nyquist - 2.0:
                b, a = signal.iirnotch(mains_hz, 30.0, fs=sfreq_hz)
                work = signal.filtfilt(b, a, work)
    except ValueError:
        return centered
    return work


def detect_bad_channels(
    values_v: np.ndarray,
    sfreq_hz: float,
    channel_names: Sequence[str],
    *,
    channel_types: Sequence[str] | None = None,
    channel_kinds: Sequence[str] | None = None,
    config: BadChannelDetectionConfig | None = None,
    montage_name: str = MONTAGE_NAME,
) -> BadChannelDetectionReport:
    """Screen sample-major float64 V EEG signals for flatline, saturation, HF noise, and decorrelation."""
    cfg = config or BadChannelDetectionConfig()
    data = np.asarray(values_v, dtype=np.float64)
    if data.ndim != 2:
        raise ValueError("坏导检测输入必须是二维样本矩阵 (samples, channels)")
    names = tuple(str(name) for name in channel_names)
    if data.shape[1] != len(names):
        raise ValueError("通道名称数量与样本矩阵列数不匹配")
    rate = float(sfreq_hz)
    if not np.isfinite(rate) or rate <= 0:
        raise ValueError("采样率必须为有限正数")

    types = tuple(channel_types) if channel_types is not None else ("eeg",) * len(names)
    kinds = tuple(channel_kinds) if channel_kinds is not None else (None,) * len(names)
    if len(types) != len(names) or len(kinds) != len(names):
        raise ValueError("通道类型元数据长度与通道数不一致")

    positions = _standard_3d_lookup(montage_name)
    n_samples = int(data.shape[0])

    # Pre-screen per channel before computing cross-channel correlation among healthy-enough EEG peers.
    per_ch_stats: list[dict[str, object]] = []
    corr_candidates: list[int] = []
    for idx, name in enumerate(names):
        is_eeg = str(types[idx]).strip().casefold() == "eeg"
        can_interp, interp_reason = _channel_interpolation_eligibility(
            name, is_eeg=is_eeg, kind=kinds[idx], positions=positions,
        )
        col = data[:, idx]
        finite_mask = np.isfinite(col)
        finite_count = int(np.count_nonzero(finite_mask))
        nan_ratio = 1.0 if n_samples == 0 else float(1.0 - finite_count / n_samples)
        reasons: list[str] = []
        std_v = ptp_v = span_v = rms_v = hf_ratio = None
        if is_eeg:
            if nan_ratio > cfg.max_nan_ratio or finite_count < 8:
                reasons.append("excessive_nan_ratio")
            else:
                finite = col[finite_mask]
                centered = finite - float(np.mean(finite))
                raw_std_v = float(np.std(centered))
                ptp_v = float(np.ptp(finite))
                if raw_std_v < cfg.min_std_v or ptp_v < cfg.min_ptp_v:
                    reasons.append("flatline")
                amp_eval = _amplitude_eval_signal(finite, rate)
                std_v = float(np.std(amp_eval))
                p01, p99 = np.percentile(amp_eval, [1.0, 99.0])
                span_v = float(p99 - p01)
                rms_v = float(np.sqrt(np.mean(amp_eval * amp_eval)))
                if span_v > cfg.max_robust_span_v or rms_v > cfg.max_rms_v:
                    reasons.append("extreme_amplitude")
                hf_ratio, hf_rms = _hf_noise_metrics(centered, rate, cfg.hf_low_hz)
                if (
                    hf_ratio is not None
                    and hf_rms is not None
                    and hf_ratio > cfg.max_hf_power_ratio
                    and hf_rms > cfg.min_hf_rms_v
                ):
                    reasons.append("high_frequency_noise")
                if "flatline" not in reasons and "excessive_nan_ratio" not in reasons and finite_count == n_samples:
                    corr_candidates.append(idx)
        per_ch_stats.append({
            "channel": name, "is_eeg": is_eeg, "reasons": reasons, "nan_ratio": nan_ratio,
            "std_v": std_v, "ptp_v": ptp_v, "span_v": span_v, "rms_v": rms_v,
            "hf_ratio": hf_ratio, "max_corr": None,
            "can_interpolate": can_interp, "interp_reason": interp_reason,
        })

    if len(corr_candidates) >= cfg.min_channels_for_correlation:
        subset = data[:, corr_candidates]
        filtered = _bandpass_for_correlation(subset, rate)
        target = filtered if filtered is not None else subset
        centered = target - np.mean(target, axis=0, keepdims=True)
        norms = np.linalg.norm(centered, axis=0)
        valid_local = norms > 0
        if int(np.count_nonzero(valid_local)) >= cfg.min_channels_for_correlation:
            valid_indices = [corr_candidates[i] for i, ok in enumerate(valid_local) if ok]
            unit = centered[:, valid_local] / norms[valid_local]
            corr_matrix = np.abs(unit.T @ unit)
            np.fill_diagonal(corr_matrix, -1.0)
            for local_i, global_idx in enumerate(valid_indices):
                max_corr = float(np.max(corr_matrix[local_i]))
                per_ch_stats[global_idx]["max_corr"] = max_corr
                if max_corr < cfg.min_neighbor_correlation:
                    per_ch_stats[global_idx]["reasons"].append("low_neighbor_correlation")

    good_3d_donor_count = sum(
        1 for item in per_ch_stats if bool(item["can_interpolate"]) and not bool(item["reasons"])
    )
    metrics_list: list[BadChannelMetrics] = []
    for item in per_ch_stats:
        is_bad = bool(item["reasons"])
        can_interp = bool(item["can_interpolate"])
        interp_reason = item["interp_reason"]
        if can_interp:
            remaining_donors = good_3d_donor_count if is_bad else good_3d_donor_count - 1
            if remaining_donors < 3:
                can_interp = False
                interp_reason = "insufficient_good_3d_donors"
        metrics_list.append(BadChannelMetrics(
            channel=str(item["channel"]), is_eeg=bool(item["is_eeg"]), is_bad=is_bad,
            reasons=tuple(item["reasons"]), nan_ratio=float(item["nan_ratio"]),
            std_uv=None if item["std_v"] is None else float(item["std_v"]) * 1e6,
            ptp_uv=None if item["ptp_v"] is None else float(item["ptp_v"]) * 1e6,
            robust_span_uv=None if item["span_v"] is None else float(item["span_v"]) * 1e6,
            rms_uv=None if item["rms_v"] is None else float(item["rms_v"]) * 1e6,
            hf_power_ratio=item["hf_ratio"], max_neighbor_correlation=item["max_corr"],
            can_interpolate=can_interp, interpolation_unavailable_reason=interp_reason,
        ))
    metrics = tuple(metrics_list)
    recommended = tuple(item.channel for item in metrics if item.is_bad)
    return BadChannelDetectionReport(
        sampling_rate_hz=rate, sample_count=n_samples, channels=metrics,
        recommended_bad_channels=recommended, montage_template=montage_name,
    )


def build_spherical_spline_interpolator(
    channel_names: Sequence[str], bad_channels: Sequence[str], *,
    channel_kinds: Sequence[str | None] | None = None, montage_name: str = MONTAGE_NAME,
) -> SphericalSplineInterpolationPlan:
    """Precompute an MNE-exact spherical spline matrix for unipolar scalp EEG channels."""
    names = tuple(str(name) for name in channel_names)
    if not names:
        raise ValueError("通道列表为空，无法构建球面插值矩阵")
    kinds = tuple(channel_kinds) if channel_kinds is not None else (None,) * len(names)
    if len(kinds) != len(names):
        raise ValueError("通道类型数量与通道名称数量不一致")

    lookup = {name.casefold(): index for index, name in enumerate(names)}
    bad_indices, bad_canonical, seen_bad = [], [], set()
    for requested in bad_channels:
        folded = str(requested).strip().casefold()
        if not folded:
            continue
        if folded not in lookup:
            raise ValueError(f"BAD_CHANNEL_NOT_FOUND: 坏导通道 {requested} 不存在于原始通道中")
        idx = lookup[folded]
        if idx not in seen_bad:
            seen_bad.add(idx)
            bad_indices.append(idx)
            bad_canonical.append(names[idx])
    if not bad_indices:
        raise ValueError("BAD_CHANNEL_LIST_EMPTY: 球面插值至少需要指定一个坏导通道")

    positions = _standard_3d_lookup(montage_name)
    for idx in bad_indices:
        if kinds[idx] is not None and kinds[idx].strip().casefold() == "bipolar":
            raise ValueError(f"BAD_CHANNEL_INTERPOLATION_UNSUPPORTED: 双极通道 {names[idx]} 不支持球面插值")
        if names[idx].strip().casefold() not in positions:
            raise ValueError(f"BAD_CHANNEL_POSITION_UNAVAILABLE: 坏导通道 {names[idx]} 缺少标准 3D 电极坐标，无法球面插值")

    good_indices, good_canonical = [], []
    for idx, name in enumerate(names):
        if idx in seen_bad:
            continue
        if kinds[idx] is not None and kinds[idx].strip().casefold() not in {"reference", "eeg"}:
            continue
        if name.strip().casefold() in positions:
            good_indices.append(idx)
            good_canonical.append(name)

    if len(good_indices) < 3:
        raise ValueError("BAD_CHANNEL_INSUFFICIENT_GOOD_DONORS: 具备 3D 坐标的可用好导少于 3 路，无法执行球面插值")

    # Probe MNE's public RawArray.interpolate_bads() with an identity basis on good donors.
    canonical_mne_names = [positions[names[i].strip().casefold()][0] for i in (*good_indices, *bad_indices)]
    n_good = len(good_indices)
    info = mne.create_info(canonical_mne_names, sfreq=256.0, ch_types="eeg")
    info.set_montage(mne.channels.make_standard_montage(montage_name), on_missing="raise")
    probe = np.zeros((len(canonical_mne_names), n_good), dtype=np.float64)
    probe[:n_good, :] = np.eye(n_good, dtype=np.float64)
    raw = mne.io.RawArray(probe, info, verbose=False)
    raw.info["bads"] = list(canonical_mne_names[n_good:])
    with warnings.catch_warnings():
        warnings.simplefilter("ignore", RuntimeWarning)
        raw.interpolate_bads(reset_bads=True, mode="accurate", origin="auto", verbose=False)
    weights = np.asarray(raw.get_data()[n_good:, :], dtype=np.float64)
    if not np.all(np.isfinite(weights)):
        raise ValueError("BAD_CHANNEL_GEOMETRY_INVALID: MNE 球面插值矩阵包含非有限数值")
    weights.flags.writeable = False

    return SphericalSplineInterpolationPlan(
        channel_names=names, bad_channels=tuple(bad_canonical), good_donor_channels=tuple(good_canonical),
        bad_indices=tuple(bad_indices), good_indices=tuple(good_indices),
        weights=weights, montage_template=montage_name, origin_mode="auto",
    )


def apply_spherical_spline_interpolation(values_v: np.ndarray, plan: SphericalSplineInterpolationPlan) -> np.ndarray:
    """Interpolate bad channels in-copy (sample-major float64 V); NaN gaps propagate explicitly."""
    data = np.asarray(values_v, dtype=np.float64)
    if data.ndim != 2 or data.shape[1] != len(plan.channel_names):
        raise ValueError("插值输入矩阵形状与插值计划通道数不匹配")
    output = data.copy()
    good_block = data[:, list(plan.good_indices)]
    interpolated = good_block @ plan.weights.T
    bad_sample_mask = ~np.all(np.isfinite(good_block), axis=1)
    if np.any(bad_sample_mask):
        interpolated[bad_sample_mask, :] = np.nan
    for col_offset, bad_idx in enumerate(plan.bad_indices):
        output[:, bad_idx] = interpolated[:, col_offset]
    return output
