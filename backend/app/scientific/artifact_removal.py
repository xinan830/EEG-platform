"""Offline EEG artifact removal: EOG regression, MNE SSP, and reviewable ICA (infomax).

Uses 100% public MNE APIs and NumPy/SciPy only (zero private MNE imports, zero heavy ML deps).
All inputs/outputs are sample-major float64 arrays in SI volts (V); NaN gaps are preserved.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Sequence
import warnings

import mne
import numpy as np
from scipy import signal

from app.scientific.contracts.artifact_removal import ArtifactRemovalSpec


_DEDICATED_EOG_CANDIDATES = ("veog", "heog", "eog", "eog1", "eog2")
_FRONTAL_EOG_PROXY_CANDIDATES = ("fp1", "fp2", "fpz")
_DEFAULT_ECG_CANDIDATES = ("ecg", "ekg", "ecg1")


@dataclass(frozen=True)
class FittedIcaModel:
    """Reusable fitted MNE ICA model + review metadata bound to a verified preprocessing fingerprint."""
    ica: Any
    info: Any
    channel_names: tuple[str, ...]
    sampling_rate_hz: float
    sample_count: int
    eog_channels_used: tuple[str, ...]
    eog_channel_source: str | None
    ecg_channels_used: tuple[str, ...]
    recommended_exclude_components: tuple[int, ...]
    component_scores: tuple[dict[str, object], ...]

    def to_inspection_dict(self, selected_exclude: tuple[int, ...] | None = None) -> dict[str, object]:
        excluded = set(self.recommended_exclude_components if selected_exclude is None else selected_exclude)
        table = [{**row, "excluded": int(row["component_index"]) in excluded} for row in self.component_scores]
        return {
            "method": "ica_infomax", "mne_version": mne.__version__,
            "sampling_rate_hz": self.sampling_rate_hz, "sample_count": self.sample_count,
            "n_components": int(self.ica.n_components_), "eog_channels_used": list(self.eog_channels_used),
            "eog_channel_source": self.eog_channel_source, "ecg_channels_used": list(self.ecg_channels_used),
            "recommended_exclude_components": list(self.recommended_exclude_components),
            "selected_exclude_components": sorted(excluded), "components": table,
        }


@dataclass(frozen=True)
class ArtifactRemovalResult:
    data_v: np.ndarray
    method: str
    applied: bool
    status: str
    eog_channels_used: tuple[str, ...] = ()
    eog_channel_source: str | None = None
    ecg_channels_used: tuple[str, ...] = ()
    excluded_components: tuple[int, ...] = ()
    component_scores: tuple[dict[str, object], ...] = ()
    ssp_projectors_applied: int = 0
    eog_events_detected: int = 0
    regression_weights: dict[str, list[float]] | None = None
    ica_fit_fingerprint: str | None = None
    ica_model_reused: bool = False

    def to_evidence(self) -> dict[str, object]:
        return {
            "method": self.method, "applied": self.applied, "status": self.status, "mne_version": mne.__version__,
            "eog_channels_used": list(self.eog_channels_used), "eog_channel_source": self.eog_channel_source,
            "ecg_channels_used": list(self.ecg_channels_used), "excluded_components": list(self.excluded_components),
            "component_scores": list(self.component_scores), "ssp_projectors_applied": self.ssp_projectors_applied,
            "eog_events_detected": self.eog_events_detected, "regression_weights": self.regression_weights,
            "ica_fit_fingerprint": self.ica_fit_fingerprint, "ica_model_reused": self.ica_model_reused,
        }


def _strip_montage_suffix(label: str) -> str:
    return label.split("-", 1)[0].strip().casefold()


def _match_candidates(channel_names: tuple[str, ...], candidates: tuple[str, ...], max_auto: int) -> tuple[str, ...]:
    auto: list[str] = []
    for candidate in candidates:
        for name in channel_names:
            if name.casefold() == candidate or _strip_montage_suffix(name) == candidate:
                if name not in auto:
                    auto.append(name)
                    if len(auto) >= max_auto:
                        return tuple(auto)
    return tuple(auto)


def _resolve_explicit_channels(
    channel_names: tuple[str, ...], explicit: tuple[str, ...], error_code: str,
) -> tuple[str, ...]:
    lookup = {name.casefold(): name for name in channel_names}
    resolved: list[str] = []
    for req in explicit:
        match = lookup.get(req.casefold()) or next(
            (n for n in channel_names if _strip_montage_suffix(n) == req.casefold()), None,
        )
        if match is None:
            raise ValueError(f"{error_code}: 通道 {req} 不存在于当前信号中")
        if match not in resolved:
            resolved.append(match)
    return tuple(resolved)


def _resolve_eog_channels(
    channel_names: tuple[str, ...], spec: ArtifactRemovalSpec, *,
    allow_frontal_fallback: bool, required: bool, max_auto: int,
) -> tuple[tuple[str, ...], str | None]:
    if spec.eog_channels:
        return (
            _resolve_explicit_channels(channel_names, spec.eog_channels, "ARTIFACT_REMOVAL_EOG_CHANNEL_UNAVAILABLE"),
            "explicit_channel",
        )
    dedicated = _match_candidates(channel_names, _DEDICATED_EOG_CANDIDATES, max_auto)
    if dedicated:
        return dedicated, "dedicated_eog"
    frontal = _match_candidates(channel_names, _FRONTAL_EOG_PROXY_CANDIDATES, max_auto)
    if frontal:
        if allow_frontal_fallback and spec.allow_frontal_eog_proxy:
            return frontal, "frontal_eeg_proxy"
        if not allow_frontal_fallback:
            raise ValueError(
                "ARTIFACT_REMOVAL_EOG_CHANNEL_UNAVAILABLE: 未找到专用 EOG 通道；EOG 回归不会静默把前额 Fp1/Fp2 当作回归自变量，请显式指定 eog_channels 或改用 ica_infomax/ssp_eog"
            )
        if required:
            raise ValueError(
                f"ARTIFACT_REMOVAL_FRONTAL_PROXY_CONFIRMATION_REQUIRED: 未找到专用 EOG 通道，但检测到前额脑电通道 {list(frontal)}；如需作为眼电代理，请显式设置 allow_frontal_eog_proxy=True 或指定 eog_channels"
            )
        return (), None
    if required:
        raise ValueError("ARTIFACT_REMOVAL_EOG_CHANNEL_UNAVAILABLE: 未找到可用于眼电伪迹估计的 EOG 通道")
    return (), None


def _resolve_ecg_channels(channel_names: tuple[str, ...], explicit: tuple[str, ...]) -> tuple[str, ...]:
    if explicit:
        return _resolve_explicit_channels(channel_names, explicit, "ARTIFACT_REMOVAL_ECG_CHANNEL_UNAVAILABLE")
    return _match_candidates(channel_names, _DEFAULT_ECG_CANDIDATES, 1)


def _contiguous_finite_spans(data_v: np.ndarray, min_samples: int) -> list[tuple[int, int]]:
    mask = np.all(np.isfinite(data_v), axis=1)
    if mask.size == 0 or not np.any(mask):
        return []
    padded = np.concatenate(([False], mask, [False]))
    changes = np.diff(padded.astype(np.int8))
    starts, ends = np.flatnonzero(changes == 1), np.flatnonzero(changes == -1)
    return [(int(s), int(e)) for s, e in zip(starts, ends, strict=True) if (int(e) - int(s)) >= min_samples]


def _highpass_finite_spans(data_v: np.ndarray, sfreq_hz: float, cutoff_hz: float = 1.0) -> np.ndarray:
    """High-pass each contiguous finite span at 1 Hz and concatenate for stable fitting without splice ringing."""
    nyquist = sfreq_hz / 2.0
    sos = signal.butter(4, cutoff_hz, btype="highpass", fs=sfreq_hz, output="sos") if 0 < cutoff_hz < nyquist else None
    pieces: list[np.ndarray] = []
    for start, end in _contiguous_finite_spans(data_v, 1):
        chunk = data_v[start:end]
        if sos is not None and (end - start) >= 32:
            try:
                pieces.append(signal.sosfiltfilt(sos, chunk, axis=0))
                continue
            except ValueError:
                pass
        pieces.append(chunk - np.mean(chunk, axis=0, keepdims=True))
    return np.vstack(pieces) if pieces else np.empty((0, data_v.shape[1]), dtype=np.float64)


def _apply_eog_regression(
    data_v: np.ndarray, sfreq_hz: float, channel_names: tuple[str, ...], spec: ArtifactRemovalSpec,
) -> ArtifactRemovalResult:
    eog_names, eog_source = _resolve_eog_channels(
        channel_names, spec, allow_frontal_fallback=False, required=True, max_auto=1,
    )
    name_to_idx = {name: i for i, name in enumerate(channel_names)}
    eog_idx = [name_to_idx[name] for name in eog_names]
    target_idx = [i for i in range(len(channel_names)) if i not in eog_idx]
    if not target_idx:
        raise ValueError("ARTIFACT_REMOVAL_NO_TARGET_CHANNELS: 除去 EOG 参考通道后没有剩余 EEG 通道")
    if int(np.count_nonzero(np.all(np.isfinite(data_v), axis=1))) < max(32, len(eog_idx) + 2):
        raise ValueError("ARTIFACT_REMOVAL_INSUFFICIENT_FINITE_SAMPLES: 有效非 NaN 样本不足，无法拟合 EOG 回归")

    fit_block = _highpass_finite_spans(data_v, sfreq_hz, cutoff_hz=1.0)
    r_fit = fit_block[:, eog_idx] - np.mean(fit_block[:, eog_idx], axis=0, keepdims=True)
    y_fit = fit_block[:, target_idx] - np.mean(fit_block[:, target_idx], axis=0, keepdims=True)
    beta, *_ = np.linalg.lstsq(r_fit, y_fit, rcond=None)

    output = data_v.copy()
    r_all = data_v[:, eog_idx]
    r_valid_rows = np.all(np.isfinite(r_all), axis=1)
    r_mean = np.mean(r_all[r_valid_rows], axis=0, keepdims=True)
    correction = np.where(r_valid_rows[:, None], r_all - r_mean, 0.0) @ beta
    for out_col, ch_idx in enumerate(target_idx):
        col = output[:, ch_idx] - correction[:, out_col]
        col[~r_valid_rows] = np.nan
        output[:, ch_idx] = col

    weights_map = {
        channel_names[ch_idx]: [round(float(w), 6) for w in beta[:, out_col]]
        for out_col, ch_idx in enumerate(target_idx)
    }
    return ArtifactRemovalResult(
        data_v=output, method="eog_regression", applied=True, status="applied",
        eog_channels_used=eog_names, eog_channel_source=eog_source, regression_weights=weights_map,
    )


def _apply_ssp_eog(
    data_v: np.ndarray, sfreq_hz: float, channel_names: tuple[str, ...], spec: ArtifactRemovalSpec,
) -> ArtifactRemovalResult:
    eog_names, eog_source = _resolve_eog_channels(
        channel_names, spec, allow_frontal_fallback=True, required=True, max_auto=1,
    )
    spans = _contiguous_finite_spans(data_v, max(64, int(round(3.0 * sfreq_hz))))
    if not spans:
        raise ValueError("ARTIFACT_REMOVAL_INSUFFICIENT_FINITE_SAMPLES: 缺少至少 3 秒连续有限样本，无法估计 SSP 眼电投影")
    longest = max(spans, key=lambda pair: pair[1] - pair[0])
    info = mne.create_info(list(channel_names), sfreq=sfreq_hz, ch_types="eeg")
    raw_fit = mne.io.RawArray(np.ascontiguousarray(data_v[longest[0]:longest[1]].T, dtype=np.float64), info, verbose=False)
    try:
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", RuntimeWarning)
            projs, events = mne.preprocessing.compute_proj_eog(
                raw_fit, ch_name=eog_names[0], n_eeg=spec.ssp_n_eeg,
                n_grad=0, n_mag=0, average=True, no_proj=True, verbose=False,
            )
    except RuntimeError:
        projs, events = [], np.empty((0, 3), dtype=int)

    if not projs:
        return ArtifactRemovalResult(
            data_v=data_v.copy(), method="ssp_eog", applied=False, status="skipped_no_eog_events",
            eog_channels_used=eog_names, eog_channel_source=eog_source,
            ssp_projectors_applied=0, eog_events_detected=int(len(events)),
        )

    output = data_v.copy()
    finite_rows = np.all(np.isfinite(data_v), axis=1)
    raw_apply = mne.io.RawArray(np.ascontiguousarray(data_v[finite_rows].T, dtype=np.float64), info, verbose=False)
    raw_apply.add_proj(projs)
    raw_apply.apply_proj()
    output[finite_rows] = raw_apply.get_data().T
    output[~finite_rows] = np.nan
    return ArtifactRemovalResult(
        data_v=output, method="ssp_eog", applied=True, status="applied",
        eog_channels_used=eog_names, eog_channel_source=eog_source,
        ssp_projectors_applied=len(projs), eog_events_detected=int(len(events)),
    )


def fit_ica_model(
    values_v: np.ndarray, sfreq_hz: float, channel_names: Sequence[str], spec: ArtifactRemovalSpec | None = None,
) -> FittedIcaModel:
    """Fit reproducible MNE infomax ICA on 1 Hz span-highpassed copy and score components."""
    data = np.asarray(values_v, dtype=np.float64)
    if data.ndim != 2:
        raise ValueError("ICA 审查输入必须是二维样本矩阵 (samples, channels)")
    names = tuple(str(n) for n in channel_names)
    if data.shape[1] != len(names):
        raise ValueError("通道名称数量与样本矩阵列数不匹配")
    rate = float(sfreq_hz)
    if not np.isfinite(rate) or rate <= 0:
        raise ValueError("采样率必须为有限正数")
    norm = (spec or ArtifactRemovalSpec(method="ica_infomax")).normalized()
    if norm.method != "ica_infomax":
        norm = ArtifactRemovalSpec(
            method="ica_infomax", eog_channels=norm.eog_channels, ecg_channels=norm.ecg_channels,
            allow_frontal_eog_proxy=norm.allow_frontal_eog_proxy, ica_n_components=norm.ica_n_components,
            ica_eog_threshold=norm.ica_eog_threshold, ica_exclude_components=norm.ica_exclude_components,
            random_state=norm.random_state,
        ).normalized()

    n_channels = len(names)
    if n_channels < 4:
        raise ValueError("ARTIFACT_REMOVAL_INSUFFICIENT_CHANNELS: ICA 去伪迹至少需要 4 路 EEG 通道")
    finite_rows = np.all(np.isfinite(data), axis=1)
    if int(np.count_nonzero(finite_rows)) < max(256, int(round(5.0 * rate))):
        raise ValueError("ARTIFACT_REMOVAL_INSUFFICIENT_FINITE_SAMPLES: 有效样本不足 5 秒，无法稳定拟合 ICA")

    ecg_names = _resolve_ecg_channels(names, norm.ecg_channels)
    eog_required = not ecg_names and norm.ica_exclude_components is None
    eog_names, eog_source = _resolve_eog_channels(
        names, norm, allow_frontal_fallback=True, required=eog_required, max_auto=2,
    )
    n_components = min(norm.ica_n_components or min(n_channels - 1, 20), n_channels - 1)
    fit_hp = _highpass_finite_spans(data, rate, cutoff_hz=1.0)
    info = mne.create_info(list(names), sfreq=rate, ch_types="eeg")
    raw_fit = mne.io.RawArray(np.ascontiguousarray(fit_hp.T, dtype=np.float64), info, verbose=False)

    ica = mne.preprocessing.ICA(
        n_components=n_components, method="infomax", random_state=norm.random_state, max_iter=300, verbose=False,
    )
    with warnings.catch_warnings():
        warnings.simplefilter("ignore", RuntimeWarning)
        ica.fit(raw_fit, verbose=False)

    eog_scores = np.zeros(ica.n_components_, dtype=np.float64)
    auto_eog: set[int] = set()
    for ch in eog_names:
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", RuntimeWarning)
            indices, scores = ica.find_bads_eog(
                raw_fit, ch_name=ch, threshold=norm.ica_eog_threshold, measure="correlation", verbose=False,
            )
        auto_eog.update(int(i) for i in indices)
        arr = np.asarray(scores, dtype=np.float64)
        eog_scores = np.where(np.abs(arr) > np.abs(eog_scores), arr, eog_scores)

    ecg_scores = np.zeros(ica.n_components_, dtype=np.float64)
    auto_ecg: set[int] = set()
    for ch in ecg_names:
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", RuntimeWarning)
            indices, scores = ica.find_bads_ecg(raw_fit, ch_name=ch, verbose=False)
        auto_ecg.update(int(i) for i in indices)
        arr = np.asarray(scores, dtype=np.float64)
        ecg_scores = np.where(np.abs(arr) > np.abs(ecg_scores), arr, ecg_scores)

    recommended = tuple(sorted(auto_eog | auto_ecg))
    table = tuple(
        {
            "component_index": idx,
            "eog_score": round(float(eog_scores[idx]), 4) if eog_names else None,
            "ecg_score": round(float(ecg_scores[idx]), 4) if ecg_names else None,
            "explained_variance_ratio": round(
                float(ica.get_explained_variance_ratio(raw_fit, components=[idx], ch_type="eeg").get("eeg", 0.0)), 6,
            ),
            "recommended_exclude": idx in recommended,
            "excluded": idx in recommended,
        }
        for idx in range(ica.n_components_)
    )
    return FittedIcaModel(
        ica=ica, info=info, channel_names=names, sampling_rate_hz=rate, sample_count=int(data.shape[0]),
        eog_channels_used=eog_names, eog_channel_source=eog_source, ecg_channels_used=ecg_names,
        recommended_exclude_components=recommended, component_scores=table,
    )


def inspect_ica_components(
    values_v: np.ndarray, sfreq_hz: float, channel_names: Sequence[str], spec: ArtifactRemovalSpec | None = None,
) -> dict[str, object]:
    """Fit reproducible MNE infomax ICA on 1 Hz high-pass copy and return reviewable IC scores."""
    norm = (spec or ArtifactRemovalSpec(method="ica_infomax")).normalized()
    fitted = fit_ica_model(values_v, sfreq_hz, channel_names, norm)
    if norm.ica_exclude_components is not None:
        invalid = [idx for idx in norm.ica_exclude_components if idx >= fitted.ica.n_components_]
        if invalid:
            raise ValueError(f"ARTIFACT_REMOVAL_INVALID_IC: 指定的 ICA 成分索引 {invalid[0]} 超出范围 (0..{fitted.ica.n_components_ - 1})")
    return fitted.to_inspection_dict(norm.ica_exclude_components)


def apply_offline_artifact_removal(
    values_v: np.ndarray, sfreq_hz: float, channel_names: Sequence[str], spec: ArtifactRemovalSpec, *,
    prefitted_ica: FittedIcaModel | None = None, ica_fit_fingerprint: str | None = None,
) -> ArtifactRemovalResult:
    """Apply explicit offline artifact removal (EOG regression, MNE SSP, or reviewable ICA)."""
    norm, data = spec.normalized(), np.asarray(values_v, dtype=np.float64)
    if data.ndim != 2:
        raise ValueError("去伪迹输入必须是二维样本矩阵 (samples, channels)")
    names = tuple(str(n) for n in channel_names)
    if data.shape[1] != len(names):
        raise ValueError("通道名称数量与样本矩阵列数不匹配")
    rate = float(sfreq_hz)
    if not np.isfinite(rate) or rate <= 0:
        raise ValueError("采样率必须为有限正数")

    if not norm.is_active:
        return ArtifactRemovalResult(data_v=data.copy(), method="none", applied=False, status="disabled")
    if norm.method == "eog_regression":
        return _apply_eog_regression(data, rate, names, norm)
    if norm.method == "ssp_eog":
        return _apply_ssp_eog(data, rate, names, norm)

    reused = prefitted_ica is not None
    fitted = prefitted_ica if prefitted_ica is not None else fit_ica_model(data, rate, names, norm)
    if norm.ica_exclude_components is not None:
        invalid = [idx for idx in norm.ica_exclude_components if idx >= fitted.ica.n_components_]
        if invalid:
            raise ValueError(f"ARTIFACT_REMOVAL_INVALID_IC: 指定的 ICA 成分索引 {invalid[0]} 超出范围 (0..{fitted.ica.n_components_ - 1})")
        excluded = norm.ica_exclude_components
    else:
        excluded = fitted.recommended_exclude_components
    table = tuple({**row, "excluded": int(row["component_index"]) in excluded} for row in fitted.component_scores)

    if not excluded:
        return ArtifactRemovalResult(
            data_v=data.copy(), method="ica_infomax", applied=False, status="skipped_no_artifact_components",
            eog_channels_used=fitted.eog_channels_used, eog_channel_source=fitted.eog_channel_source,
            ecg_channels_used=fitted.ecg_channels_used, excluded_components=(), component_scores=table,
            ica_fit_fingerprint=ica_fit_fingerprint, ica_model_reused=reused,
        )

    output = data.copy()
    finite_rows = np.all(np.isfinite(data), axis=1)
    raw_apply = mne.io.RawArray(np.ascontiguousarray(data[finite_rows].T, dtype=np.float64), fitted.info, verbose=False)
    ica_copy = fitted.ica.copy()
    ica_copy.exclude = list(excluded)
    ica_copy.apply(raw_apply, verbose=False)
    output[finite_rows] = raw_apply.get_data().T
    output[~finite_rows] = np.nan
    return ArtifactRemovalResult(
        data_v=output, method="ica_infomax", applied=True, status="applied",
        eog_channels_used=fitted.eog_channels_used, eog_channel_source=fitted.eog_channel_source,
        ecg_channels_used=fitted.ecg_channels_used, excluded_components=excluded, component_scores=table,
        ica_fit_fingerprint=ica_fit_fingerprint, ica_model_reused=reused,
    )
