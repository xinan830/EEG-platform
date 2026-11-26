"""Immutable offline artifact-removal contract (EOG regression, SSP, reviewable ICA)."""

from __future__ import annotations

from dataclasses import dataclass
import hashlib
import json
import math
from typing import Any, Literal

import mne
import numpy as np


ArtifactRemovalMethod = Literal["none", "eog_regression", "ssp_eog", "ica_infomax"]
_VALID_METHODS = frozenset({"none", "eog_regression", "ssp_eog", "ica_infomax"})
ARTIFACT_REMOVAL_APPLICATION_VERSION = "artifact-removal-application-v7-verified-icalabel"


@dataclass(frozen=True)
class FittedIcaModel:
    """Reusable fitted MNE Extended Infomax ICA model + rank/convergence/ICLabel metadata."""

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
    ica_algorithm: str = "extended_infomax"
    fit_bandpass_hz: tuple[float, float] | None = None
    fit_band_limited_by_nyquist: bool = False
    classifier_method: str = "eog_ecg_correlation"
    classifier_status: str = "fallback_eog_ecg"
    classifier_unavailable_reason: str | None = None
    classifier_version: str | None = None
    onnxruntime_version: str | None = None
    iclabel_model_sha256: str | None = None
    recommendation_rule_version: str | None = None
    iclabel_exclude_threshold: float = 0.75
    iclabel_max_brain_probability: float = 0.25
    effective_rank: int | None = None
    n_components_requested: int | None = None
    rank_capped: bool = False
    ica_converged: bool = True
    ica_n_iter: int | None = None
    ica_max_iter: int = 500
    fit_duration_s: float | None = None
    fit_sample_sufficiency: str | None = None
    fit_warnings: tuple[str, ...] = ()

    def to_inspection_dict(self, selected_exclude: tuple[int, ...] | None = None) -> dict[str, object]:
        excluded = set(self.recommended_exclude_components if selected_exclude is None else selected_exclude)
        table = [{**row, "excluded": int(row["component_index"]) in excluded} for row in self.component_scores]
        return {
            "method": "ica_infomax", "ica_algorithm": self.ica_algorithm, "mne_version": mne.__version__,
            "sampling_rate_hz": self.sampling_rate_hz, "sample_count": self.sample_count,
            "n_components": int(self.ica.n_components_), "n_components_requested": self.n_components_requested,
            "effective_rank": self.effective_rank, "rank_capped": self.rank_capped,
            "ica_converged": self.ica_converged, "ica_n_iter": self.ica_n_iter, "ica_max_iter": self.ica_max_iter,
            "fit_duration_s": self.fit_duration_s, "fit_sample_sufficiency": self.fit_sample_sufficiency,
            "fit_warnings": list(self.fit_warnings),
            "eog_channels_used": list(self.eog_channels_used),
            "eog_channel_source": self.eog_channel_source, "ecg_channels_used": list(self.ecg_channels_used),
            "fit_bandpass_hz": None if self.fit_bandpass_hz is None else [float(self.fit_bandpass_hz[0]), float(self.fit_bandpass_hz[1])],
            "fit_band_limited_by_nyquist": self.fit_band_limited_by_nyquist,
            "classifier_method": self.classifier_method, "classifier_status": self.classifier_status,
            "classifier_unavailable_reason": self.classifier_unavailable_reason, "classifier_version": self.classifier_version,
            "onnxruntime_version": self.onnxruntime_version, "iclabel_model_sha256": self.iclabel_model_sha256,
            "recommendation_rule_version": self.recommendation_rule_version,
            "iclabel_exclude_threshold": self.iclabel_exclude_threshold, "iclabel_max_brain_probability": self.iclabel_max_brain_probability,
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
    application_validity: dict[str, object] | None = None
    ica_algorithm: str | None = None
    fit_bandpass_hz: tuple[float, float] | None = None
    fit_band_limited_by_nyquist: bool | None = None
    classifier_method: str | None = None
    classifier_status: str | None = None
    classifier_unavailable_reason: str | None = None
    classifier_version: str | None = None
    onnxruntime_version: str | None = None
    iclabel_model_sha256: str | None = None
    recommendation_rule_version: str | None = None
    effective_rank: int | None = None
    n_components_requested: int | None = None
    rank_capped: bool | None = None
    ica_converged: bool | None = None
    ica_n_iter: int | None = None
    fit_duration_s: float | None = None
    fit_sample_sufficiency: str | None = None
    fit_warnings: tuple[str, ...] = ()

    def to_evidence(self) -> dict[str, object]:
        return {
            "method": self.method, "applied": self.applied, "status": self.status, "mne_version": mne.__version__,
            "application_version": ARTIFACT_REMOVAL_APPLICATION_VERSION,
            "eog_channels_used": list(self.eog_channels_used), "eog_channel_source": self.eog_channel_source,
            "ecg_channels_used": list(self.ecg_channels_used), "excluded_components": list(self.excluded_components),
            "component_scores": list(self.component_scores), "ssp_projectors_applied": self.ssp_projectors_applied,
            "eog_events_detected": self.eog_events_detected, "regression_weights": self.regression_weights,
            "ica_fit_fingerprint": self.ica_fit_fingerprint, "ica_model_reused": self.ica_model_reused,
            "ica_algorithm": self.ica_algorithm,
            "fit_bandpass_hz": None if self.fit_bandpass_hz is None else [float(self.fit_bandpass_hz[0]), float(self.fit_bandpass_hz[1])],
            "fit_band_limited_by_nyquist": self.fit_band_limited_by_nyquist,
            "classifier_method": self.classifier_method, "classifier_status": self.classifier_status,
            "classifier_unavailable_reason": self.classifier_unavailable_reason, "classifier_version": self.classifier_version,
            "onnxruntime_version": self.onnxruntime_version, "iclabel_model_sha256": self.iclabel_model_sha256,
            "recommendation_rule_version": self.recommendation_rule_version,
            "effective_rank": self.effective_rank, "n_components_requested": self.n_components_requested,
            "rank_capped": self.rank_capped, "ica_converged": self.ica_converged, "ica_n_iter": self.ica_n_iter,
            "fit_duration_s": self.fit_duration_s, "fit_sample_sufficiency": self.fit_sample_sufficiency,
            "fit_warnings": list(self.fit_warnings),
            "application_validity": self.application_validity,
        }


def build_ica_result_meta(
    fitted: Any,
    component_scores: tuple[dict[str, object], ...],
    *,
    ica_fit_fingerprint: str | None,
    reused: bool,
) -> dict[str, object]:
    """Extract ArtifactRemovalResult metadata from FittedIcaModel or compatible stub."""
    return {
        "eog_channels_used": fitted.eog_channels_used,
        "eog_channel_source": fitted.eog_channel_source,
        "ecg_channels_used": fitted.ecg_channels_used,
        "component_scores": component_scores,
        "ica_fit_fingerprint": ica_fit_fingerprint,
        "ica_model_reused": reused,
        "ica_algorithm": getattr(fitted, "ica_algorithm", "extended_infomax"),
        "fit_bandpass_hz": getattr(fitted, "fit_bandpass_hz", None),
        "fit_band_limited_by_nyquist": getattr(fitted, "fit_band_limited_by_nyquist", None),
        "classifier_method": getattr(fitted, "classifier_method", None),
        "classifier_status": getattr(fitted, "classifier_status", None),
        "classifier_unavailable_reason": getattr(fitted, "classifier_unavailable_reason", None),
        "classifier_version": getattr(fitted, "classifier_version", None),
        "onnxruntime_version": getattr(fitted, "onnxruntime_version", None),
        "iclabel_model_sha256": getattr(fitted, "iclabel_model_sha256", None),
        "recommendation_rule_version": getattr(fitted, "recommendation_rule_version", None),
        "effective_rank": getattr(fitted, "effective_rank", None),
        "n_components_requested": getattr(fitted, "n_components_requested", None),
        "rank_capped": getattr(fitted, "rank_capped", None),
        "ica_converged": getattr(fitted, "ica_converged", None),
        "ica_n_iter": getattr(fitted, "ica_n_iter", None),
        "fit_duration_s": getattr(fitted, "fit_duration_s", None),
        "fit_sample_sufficiency": getattr(fitted, "fit_sample_sufficiency", None),
        "fit_warnings": tuple(getattr(fitted, "fit_warnings", ())),
    }


def _dedupe_labels(labels: tuple[str, ...]) -> tuple[str, ...]:
    out: list[str] = []
    seen: set[str] = set()
    for raw in labels:
        label = str(raw).strip()
        if label and label.casefold() not in seen:
            seen.add(label.casefold())
            out.append(label)
    return tuple(out)


@dataclass(frozen=True)
class ArtifactRemovalSpec:
    """Explicit opt-in offline artifact-removal configuration for continuous EEG sources."""

    method: ArtifactRemovalMethod = "none"
    eog_channels: tuple[str, ...] = ()
    ecg_channels: tuple[str, ...] = ()
    allow_frontal_eog_proxy: bool = False
    ica_n_components: int | None = None
    ica_eog_threshold: float = 0.7
    ica_exclude_components: tuple[int, ...] | None = None
    ica_fit_fingerprint: str | None = None
    ssp_n_eeg: int = 1
    random_state: int = 42

    def normalized(self) -> ArtifactRemovalSpec:
        method = str(self.method).strip().casefold()
        if method not in _VALID_METHODS:
            raise ValueError(f"不支持的离线去伪迹方法：{self.method!r}")
        eog_channels = _dedupe_labels(self.eog_channels)
        ecg_channels = _dedupe_labels(self.ecg_channels)
        if len(ecg_channels) > 1:
            raise ValueError("ARTIFACT_REFERENCE_INVALID: 心电参考最多选择一路")
        if ecg_channels and method not in {"none", "ica_infomax"}:
            raise ValueError("ARTIFACT_REFERENCE_INVALID: 心电参考仅支持 ICA 去伪迹")
        if {name.casefold() for name in eog_channels} & {name.casefold() for name in ecg_channels}:
            raise ValueError("ARTIFACT_REFERENCE_INVALID: 同一路不能同时作为眼电和心电参考")
        if self.ica_n_components is not None:
            if (
                isinstance(self.ica_n_components, bool)
                or not isinstance(self.ica_n_components, int)
                or self.ica_n_components < 2
            ):
                raise ValueError("ICA 成分数必须为大于等于 2 的整数")
        if not math.isfinite(self.ica_eog_threshold) or not (0.0 < self.ica_eog_threshold <= 1.0):
            raise ValueError("ICA 眼电相关阈值必须在 (0, 1] 范围内")
        if isinstance(self.ssp_n_eeg, bool) or not isinstance(self.ssp_n_eeg, int) or not 1 <= self.ssp_n_eeg <= 3:
            raise ValueError("SSP 投影维度必须在 1 到 3 之间")
        if isinstance(self.random_state, bool) or not isinstance(self.random_state, int):
            raise ValueError("random_state 必须为整数")

        exclude: tuple[int, ...] | None = None
        if self.ica_exclude_components is not None:
            parsed: list[int] = []
            for idx in self.ica_exclude_components:
                if isinstance(idx, bool) or not isinstance(idx, int) or idx < 0:
                    raise ValueError("ICA 排除成分索引必须为非负整数")
                parsed.append(int(idx))
            exclude = tuple(sorted(set(parsed)))

        fit_fp = str(self.ica_fit_fingerprint).strip() if self.ica_fit_fingerprint else None
        if method == "none":
            return ArtifactRemovalSpec(method="none")
        return ArtifactRemovalSpec(
            method=method,  # type: ignore[arg-type]
            eog_channels=eog_channels,
            ecg_channels=ecg_channels,
            allow_frontal_eog_proxy=bool(self.allow_frontal_eog_proxy),
            ica_n_components=self.ica_n_components,
            ica_eog_threshold=float(self.ica_eog_threshold),
            ica_exclude_components=exclude,
            ica_fit_fingerprint=fit_fp,
            ssp_n_eeg=int(self.ssp_n_eeg),
            random_state=int(self.random_state),
        )

    @property
    def is_active(self) -> bool:
        return self.method != "none"

    def fingerprint(self) -> str:
        norm = self.normalized()
        if not norm.is_active:
            return "none"
        payload = json.dumps(
            {
                "application_version": ARTIFACT_REMOVAL_APPLICATION_VERSION,
                "method": norm.method,
                "eog_channels": sorted(ch.casefold() for ch in norm.eog_channels),
                "ecg_channels": sorted(ch.casefold() for ch in norm.ecg_channels),
                "allow_frontal_eog_proxy": norm.allow_frontal_eog_proxy,
                "ica_n_components": norm.ica_n_components,
                "ica_eog_threshold": round(norm.ica_eog_threshold, 6),
                "ica_exclude_components": (
                    None if norm.ica_exclude_components is None else list(norm.ica_exclude_components)
                ),
                "ica_fit_fingerprint": norm.ica_fit_fingerprint,
                "ssp_n_eeg": norm.ssp_n_eeg,
                "random_state": norm.random_state,
            },
            sort_keys=True,
            separators=(",", ":"),
        )
        return hashlib.sha256(payload.encode("utf-8")).hexdigest()[:16]

    def to_evidence(self) -> dict[str, object]:
        norm = self.normalized()
        return {
            **({"application_version": ARTIFACT_REMOVAL_APPLICATION_VERSION} if norm.is_active else {}),
            "method": norm.method,
            "eog_channels": list(norm.eog_channels),
            "ecg_channels": list(norm.ecg_channels),
            "allow_frontal_eog_proxy": norm.allow_frontal_eog_proxy,
            "ica_n_components": norm.ica_n_components,
            "ica_eog_threshold": norm.ica_eog_threshold if norm.method == "ica_infomax" else None,
            "ica_exclude_components": (
                None if norm.ica_exclude_components is None else list(norm.ica_exclude_components)
            ),
            "ica_fit_fingerprint": norm.ica_fit_fingerprint if norm.method == "ica_infomax" else None,
            "ssp_n_eeg": norm.ssp_n_eeg if norm.method == "ssp_eog" else None,
            "random_state": norm.random_state if norm.method == "ica_infomax" else None,
            "spec_fingerprint": norm.fingerprint(),
        }
