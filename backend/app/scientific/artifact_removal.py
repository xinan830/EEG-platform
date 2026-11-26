"""Offline EEG artifact removal: EOG regression, MNE SSP, and reviewable ICA (infomax).

Uses 100% public MNE APIs and NumPy/SciPy only (zero private MNE imports, zero heavy ML deps).
All inputs/outputs are sample-major float64 arrays in SI volts (V); NaN gaps are preserved.
"""

from __future__ import annotations

from dataclasses import replace
from typing import Any, Mapping, Sequence
import warnings

import mne
import numpy as np
from scipy import signal

from app.scientific.contracts.artifact_removal import (
    ARTIFACT_REMOVAL_APPLICATION_VERSION,
    ArtifactRemovalResult,
    ArtifactRemovalSpec,
    FittedIcaModel,
    build_ica_result_meta,
)
from app.scientific.artifact_removal_validity import build_application_validity
from app.scientific.artifact_reference_signals import ArtifactReferenceSignals
from app.scientific.ica_classifier import (
    ICA_DEFAULT_MAX_ITER,
    classify_ica_components,
    compute_effective_rank,
    fit_extended_infomax,
    merge_ica_scores_with_classification,
)


_FRONTAL_EOG_PROXY_CANDIDATES = ("fp1", "fp2", "fpz")


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
        candidates = [n for n in channel_names if _strip_montage_suffix(n) == req.casefold()]
        match = lookup.get(req.casefold()) or (candidates[0] if len(candidates) == 1 else None)
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
    return ()


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


def _regress_external_eog(data, sfreq, names, references):
    references.validate(len(data))
    columns = [references.channel_names.index(name) for name in references.eog_channels]
    eog = references.values_v[:, columns]
    joint = np.column_stack((data, eog))
    fit = _highpass_finite_spans(joint, sfreq)
    if len(fit) < max(256, int(5 * sfreq)):
        raise ValueError("ARTIFACT_REFERENCE_INSUFFICIENT_SAMPLES: 眼电回归有效样本不足")
    x, y = fit[:, len(names):], fit[:, :len(names)]
    x = x - x.mean(axis=0)
    weights, *_ = np.linalg.lstsq(x, y - y.mean(axis=0), rcond=None)
    valid = np.all(np.isfinite(eog), axis=1)
    output = np.full_like(data, np.nan)
    output[valid] = data[valid] - (eog[valid] - eog[valid].mean(axis=0)) @ weights
    return ArtifactRemovalResult(data_v=output, method="eog_regression", applied=True, status="applied",
        eog_channels_used=references.eog_channels, eog_channel_source="explicit_channel",
        regression_weights={name: weights[:, i].tolist() for i, name in enumerate(names)})


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
    references: ArtifactReferenceSignals | None = None,
) -> ArtifactRemovalResult:
    external = references.eog_channels if references is not None else ()
    proxies = tuple(name for name in spec.eog_channels if name not in external)
    eog_names, eog_source = ((), None) if external and not proxies else _resolve_eog_channels(
        channel_names, replace(spec, eog_channels=proxies), allow_frontal_fallback=True, required=not external, max_auto=2)
    if external:
        eog_names, eog_source = (*external, *eog_names), "explicit_channel"
    span_values = data_v if references is None else np.column_stack((data_v, references.values_v))
    spans = _contiguous_finite_spans(span_values, max(64, int(round(3.0 * sfreq_hz))))
    if not spans:
        raise ValueError("ARTIFACT_REMOVAL_INSUFFICIENT_FINITE_SAMPLES: 缺少至少 3 秒连续有限样本，无法估计 SSP 眼电投影")
    longest = max(spans, key=lambda pair: pair[1] - pair[0])
    info = mne.create_info(list(channel_names), sfreq=sfreq_hz, ch_types="eeg")
    raw_fit = mne.io.RawArray(np.ascontiguousarray(data_v[longest[0]:longest[1]].T, dtype=np.float64), info, verbose=False)
    eog_lookup = {}
    if references is not None:
        mask = np.zeros(len(data_v), dtype=bool)
        mask[longest[0]:longest[1]] = True
        raw_fit, eog_lookup = references.scoring_raw(data_v, channel_names, sfreq_hz, mask, minimum_seconds=3)
    try:
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", RuntimeWarning)
            projs, events = mne.preprocessing.compute_proj_eog(
                raw_fit, ch_name=[eog_lookup.get(name, name) for name in eog_names], n_eeg=spec.ssp_n_eeg,
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


def _concat_demeaned_finite_spans(data_v: np.ndarray) -> np.ndarray:
    pieces = [data_v[s:e] - np.mean(data_v[s:e], axis=0, keepdims=True) for s, e in _contiguous_finite_spans(data_v, 1)]
    return np.vstack(pieces) if pieces else np.empty((0, data_v.shape[1]), dtype=np.float64)


def fit_ica_model(
    values_v: np.ndarray, sfreq_hz: float, channel_names: Sequence[str], spec: ArtifactRemovalSpec | None = None,
    *, references: ArtifactReferenceSignals | None = None, fit_values_v: np.ndarray | None = None,
    signal_provenance: Mapping[str, object] | None = None, fit_bandpass_hz: tuple[float, float] | None = None,
    fit_band_limited_by_nyquist: bool = False,
) -> FittedIcaModel:
    """Fit reproducible MNE Extended Infomax ICA (on broadband copy if provided) and score components."""
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
        norm = replace(norm, method="ica_infomax").normalized()

    n_channels = len(names)
    if n_channels < 4:
        raise ValueError("ARTIFACT_REMOVAL_INSUFFICIENT_CHANNELS: ICA 去伪迹至少需要 4 路 EEG 通道")
    fit_source = data if fit_values_v is None else np.asarray(fit_values_v, dtype=np.float64)
    if fit_source.shape != data.shape:
        raise ValueError("宽带 ICA 拟合矩阵形状必须与主信号一致")
    finite_rows = np.all(np.isfinite(fit_source), axis=1)
    if int(np.count_nonzero(finite_rows)) < max(256, int(round(5.0 * rate))):
        raise ValueError("ARTIFACT_REMOVAL_INSUFFICIENT_FINITE_SAMPLES: 有效样本不足 5 秒，无法稳定拟合 ICA")

    ecg_names = (references.ecg_channels if references is not None and references.ecg_channels
                 else _resolve_ecg_channels(names, norm.ecg_channels))
    external_eog = references.eog_channels if references is not None else ()
    proxy_requests = tuple(name for name in norm.eog_channels if name not in external_eog)
    eog_names, eog_source = ((), None) if external_eog and not proxy_requests else _resolve_eog_channels(
        names, replace(norm, eog_channels=proxy_requests), allow_frontal_fallback=True, required=False, max_auto=2)
    if external_eog:
        eog_names, eog_source = (*external_eog, *eog_names), "explicit_channel"
    fit_hp = _highpass_finite_spans(data, rate, cutoff_hz=1.0) if fit_values_v is None else _concat_demeaned_finite_spans(fit_source)
    effective_rank = compute_effective_rank(fit_hp)
    if effective_rank < 3:
        raise ValueError(f"ARTIFACT_REMOVAL_INSUFFICIENT_RANK: 有效信号秩（{effective_rank}）不足 3，无法稳定拟合 ICA")
    n_components_requested = min(norm.ica_n_components or min(n_channels - 1, 20), n_channels - 1)
    n_components = min(n_components_requested, effective_rank)
    rank_capped = n_components < n_components_requested
    info = mne.create_info(list(names), sfreq=rate, ch_types="eeg")
    raw_fit = mne.io.RawArray(np.ascontiguousarray(fit_hp.T, dtype=np.float64), info, verbose=False)

    max_iter = ICA_DEFAULT_MAX_ITER
    ica, ica_converged, ica_n_iter = fit_extended_infomax(
        raw_fit, n_components=n_components, random_state=norm.random_state, max_iter=max_iter,
    )
    fit_duration_s = round(len(fit_hp) / rate, 3)
    fit_sample_sufficiency = "sufficient" if len(fit_hp) >= (20 * n_components * n_components) else "short_recording"
    fit_warnings: list[str] = []
    if rank_capped:
        fit_warnings.append("rank_capped_to_effective_rank")
    if not ica_converged:
        fit_warnings.append("ica_not_converged")
    if fit_sample_sufficiency != "sufficient":
        fit_warnings.append("short_recording_for_ica_components")

    scoring_raw, reference_lookup = raw_fit, {}
    if references is not None:
        scoring_raw, reference_lookup = references.scoring_raw(data, names, rate, finite_rows)
    eog_scores, auto_eog = np.zeros(ica.n_components_, dtype=np.float64), set()
    for ch in eog_names:
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", RuntimeWarning)
            indices, scores = ica.find_bads_eog(
                scoring_raw, ch_name=reference_lookup.get(ch, ch), threshold=norm.ica_eog_threshold,
                measure="correlation", verbose=False)
        auto_eog.update(int(i) for i in indices)
        arr = np.asarray(scores, dtype=np.float64)
        eog_scores = np.where(np.abs(arr) > np.abs(eog_scores), arr, eog_scores)

    ecg_scores, auto_ecg = np.zeros(ica.n_components_, dtype=np.float64), set()
    for ch in ecg_names:
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", RuntimeWarning)
            indices, scores = ica.find_bads_ecg(scoring_raw, ch_name=reference_lookup.get(ch, ch), verbose=False)
        auto_ecg.update(int(i) for i in indices)
        arr = np.asarray(scores, dtype=np.float64)
        ecg_scores = np.where(np.abs(arr) > np.abs(ecg_scores), arr, ecg_scores)

    evr = [float(ica.get_explained_variance_ratio(raw_fit, components=[idx], ch_type="eeg").get("eeg", 0.0))
           for idx in range(ica.n_components_)]
    classification = classify_ica_components(
        raw_fit, ica, names, rate, signal_provenance=signal_provenance,
        fit_band_limited_by_nyquist=fit_band_limited_by_nyquist, ica_converged=ica_converged,
    )
    recommended, table = merge_ica_scores_with_classification(
        n_components=int(ica.n_components_), eog_scores=eog_scores, eog_names=eog_names,
        has_dedicated_eog=bool(external_eog),
        auto_eog=auto_eog, ecg_scores=ecg_scores, ecg_names=ecg_names, auto_ecg=auto_ecg,
        explained_variance_ratios=evr, classification=classification, ica_converged=ica_converged)
    return FittedIcaModel(
        ica=ica, info=info, channel_names=names, sampling_rate_hz=rate, sample_count=int(data.shape[0]),
        eog_channels_used=eog_names, eog_channel_source=eog_source, ecg_channels_used=ecg_names,
        recommended_exclude_components=recommended, component_scores=table,
        ica_algorithm="extended_infomax", fit_bandpass_hz=fit_bandpass_hz,
        fit_band_limited_by_nyquist=fit_band_limited_by_nyquist,
        classifier_method=classification.classifier_method, classifier_status=classification.classifier_status,
        classifier_unavailable_reason=classification.classifier_unavailable_reason,
        classifier_version=classification.classifier_version,
        onnxruntime_version=classification.onnxruntime_version,
        iclabel_model_sha256=classification.iclabel_model_sha256,
        recommendation_rule_version=classification.recommendation_rule_version,
        iclabel_exclude_threshold=classification.iclabel_exclude_threshold,
        iclabel_max_brain_probability=classification.iclabel_max_brain_probability,
        effective_rank=effective_rank, n_components_requested=n_components_requested,
        rank_capped=rank_capped, ica_converged=ica_converged, ica_n_iter=ica_n_iter,
        ica_max_iter=max_iter, fit_duration_s=fit_duration_s,
        fit_sample_sufficiency=fit_sample_sufficiency, fit_warnings=tuple(fit_warnings),
    )


def inspect_ica_components(
    values_v: np.ndarray, sfreq_hz: float, channel_names: Sequence[str], spec: ArtifactRemovalSpec | None = None,
    *, signal_provenance: Mapping[str, object] | None = None, fit_values_v: np.ndarray | None = None,
    fit_bandpass_hz: tuple[float, float] | None = None, fit_band_limited_by_nyquist: bool = False,
) -> dict[str, object]:
    """Fit reproducible MNE Extended Infomax ICA and return reviewable IC scores."""
    norm = (spec or ArtifactRemovalSpec(method="ica_infomax")).normalized()
    fitted = fit_ica_model(
        values_v, sfreq_hz, channel_names, norm, fit_values_v=fit_values_v,
        signal_provenance=signal_provenance, fit_bandpass_hz=fit_bandpass_hz,
        fit_band_limited_by_nyquist=fit_band_limited_by_nyquist)
    if norm.ica_exclude_components is not None:
        invalid = [idx for idx in norm.ica_exclude_components if idx >= fitted.ica.n_components_]
        if invalid:
            raise ValueError(f"ARTIFACT_REMOVAL_INVALID_IC: 指定的 ICA 成分索引 {invalid[0]} 超出范围 (0..{fitted.ica.n_components_ - 1})")
    return fitted.to_inspection_dict(norm.ica_exclude_components)


def apply_offline_artifact_removal(
    values_v: np.ndarray, sfreq_hz: float, channel_names: Sequence[str], spec: ArtifactRemovalSpec, *,
    prefitted_ica: FittedIcaModel | None = None, ica_fit_fingerprint: str | None = None,
    references: ArtifactReferenceSignals | None = None, fit_values_v: np.ndarray | None = None,
    signal_provenance: Mapping[str, object] | None = None, fit_bandpass_hz: tuple[float, float] | None = None,
    fit_band_limited_by_nyquist: bool = False,
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

    def with_validity(result: ArtifactRemovalResult) -> ArtifactRemovalResult:
        return replace(result, application_validity=build_application_validity(data, result.data_v, names, rate))

    if norm.method == "eog_regression":
        if references is not None and references.eog_channels:
            return with_validity(_regress_external_eog(data, rate, names, references))
        return with_validity(_apply_eog_regression(data, rate, names, norm))
    if norm.method == "ssp_eog":
        return with_validity(_apply_ssp_eog(data, rate, names, norm, references))

    reused = prefitted_ica is not None
    fitted = prefitted_ica if prefitted_ica is not None else fit_ica_model(
        data, rate, names, norm, references=references, fit_values_v=fit_values_v,
        signal_provenance=signal_provenance, fit_bandpass_hz=fit_bandpass_hz,
        fit_band_limited_by_nyquist=fit_band_limited_by_nyquist)
    if norm.ica_exclude_components is not None:
        invalid = [idx for idx in norm.ica_exclude_components if idx >= fitted.ica.n_components_]
        if invalid:
            raise ValueError(f"ARTIFACT_REMOVAL_INVALID_IC: 指定的 ICA 成分索引 {invalid[0]} 超出范围 (0..{fitted.ica.n_components_ - 1})")
        excluded = norm.ica_exclude_components
    else:
        excluded = fitted.recommended_exclude_components
    table = tuple({**row, "excluded": int(row["component_index"]) in excluded} for row in fitted.component_scores)
    ica_meta = build_ica_result_meta(fitted, table, ica_fit_fingerprint=ica_fit_fingerprint, reused=reused)
    if not excluded:
        return with_validity(ArtifactRemovalResult(
            data_v=data.copy(), method="ica_infomax", applied=False, status="skipped_no_artifact_components",
            excluded_components=(), **ica_meta))

    output = data.copy()
    finite_rows = np.all(np.isfinite(data), axis=1)
    raw_apply = mne.io.RawArray(np.ascontiguousarray(data[finite_rows].T, dtype=np.float64), fitted.info, verbose=False)
    ica_copy = fitted.ica.copy()
    ica_copy.exclude = list(excluded)
    ica_copy.apply(raw_apply, verbose=False)
    output[finite_rows] = raw_apply.get_data().T
    output[~finite_rows] = np.nan
    return with_validity(ArtifactRemovalResult(
        data_v=output, method="ica_infomax", applied=True, status="applied",
        excluded_components=excluded, **ica_meta))
