"""Bounded memory + disk-backed cache binding reviewed ICA models to verified fingerprints."""

from __future__ import annotations

from collections import OrderedDict
import hashlib
import json
import os
from pathlib import Path
import shutil
from threading import RLock
from typing import Any, Sequence
import uuid

import mne

from app.scientific.contracts.artifact_removal import FittedIcaModel
from app.scientific.contracts.preprocessing import ArtifactRejectionSpec, ArtifactRemovalSpec, BadChannelSpec
from app.scientific.contracts.preprocessing import ARTIFACT_DETECTION_VERSION
from app.scientific.ica_classifier import (
    ICA_DEFAULT_MAX_ITER,
    compute_ica_fit_bandpass_hz,
    get_iclabel_runtime_metadata,
)


def compute_ica_fit_fingerprint(
    *,
    recording_id: str,
    source_sha256: str,
    signal_view_sha256: str | None,
    channel_names: Sequence[str],
    sampling_rate_hz: float,
    sample_count: int,
    low_hz: float,
    high_hz: float,
    notch_hz: float | None,
    notch_q: float = 30.0,
    bad_channel_spec: BadChannelSpec | None,
    artifact_rejection_spec: ArtifactRejectionSpec | None,
    removal_spec: ArtifactRemovalSpec,
    fit_low_hz: float | None = None,
    fit_high_hz: float | None = None,
    classifier_version: str | None = None,
    onnxruntime_version: str | None = None,
    iclabel_model_sha256: str | None = None,
    recommendation_rule_version: str | None = None,
    ica_max_iter: int = ICA_DEFAULT_MAX_ITER,
) -> str:
    """Deterministic fingerprint of every input that affects fitted ICA unmixing weights.

    Excludes `ica_exclude_components` and `ica_fit_fingerprint` so choosing different
    components on the same reviewed ICA model reuses the exact same model without drift.
    """
    norm_bad = bad_channel_spec.normalized() if bad_channel_spec is not None else BadChannelSpec()
    norm_art = artifact_rejection_spec.normalized() if artifact_rejection_spec is not None else ArtifactRejectionSpec()
    norm_rem = removal_spec.normalized()
    payload = json.dumps(
        {
            "version": "ica-fit-v5-verified-reference-convergence",
            "ica_extended": True,
            "ica_max_iter": int(ica_max_iter),
            "artifact_detection_version": ARTIFACT_DETECTION_VERSION if norm_art.is_active else None,
            "recording_id": str(recording_id),
            "source_sha256": str(source_sha256),
            "signal_view_sha256": str(signal_view_sha256 or ""),
            "channels": [str(ch) for ch in channel_names],
            "sampling_rate_hz": round(float(sampling_rate_hz), 6),
            "sample_count": int(sample_count),
            "low_hz": round(float(low_hz), 6),
            "high_hz": round(float(high_hz), 6),
            "fit_low_hz": None if fit_low_hz is None else round(float(fit_low_hz), 6),
            "fit_high_hz": None if fit_high_hz is None else round(float(fit_high_hz), 6),
            "classifier_version": classifier_version,
            "onnxruntime_version": onnxruntime_version,
            "iclabel_model_sha256": iclabel_model_sha256,
            "recommendation_rule_version": recommendation_rule_version,
            "notch_hz": None if notch_hz is None else round(float(notch_hz), 6),
            "notch_q": round(float(notch_q), 6),
            "bad_channels": norm_bad.fingerprint(),
            "artifact_rejection": norm_art.fingerprint(),
            "eog_channels": sorted(ch.casefold() for ch in norm_rem.eog_channels),
            "ecg_channels": sorted(ch.casefold() for ch in norm_rem.ecg_channels),
            "allow_frontal_eog_proxy": bool(norm_rem.allow_frontal_eog_proxy),
            "ica_n_components": norm_rem.ica_n_components,
            "ica_eog_threshold": round(float(norm_rem.ica_eog_threshold), 6),
            "random_state": int(norm_rem.random_state),
        },
        sort_keys=True,
        separators=(",", ":"),
    )
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()[:24]


def compute_recording_ica_fit_fingerprint(
    *,
    identity: Any,
    channel_names: Sequence[str],
    sampling_rate_hz: float,
    sample_count: int,
    low_hz: float,
    high_hz: float,
    notch_hz: float | None,
    notch_q: float = 30.0,
    bad_channel_spec: BadChannelSpec | None,
    artifact_rejection_spec: ArtifactRejectionSpec | None,
    removal_spec: ArtifactRemovalSpec,
) -> str:
    """Single shared fingerprint builder for ICA review and continuous execution."""
    fit_low_hz, fit_high_hz, _ = compute_ica_fit_bandpass_hz(
        sampling_rate_hz, main_low_hz=low_hz, main_high_hz=high_hz,
    )
    meta = get_iclabel_runtime_metadata()
    return compute_ica_fit_fingerprint(
        recording_id=identity.recording_id,
        source_sha256=identity.source_sha256,
        signal_view_sha256=identity.signal_view_sha256,
        channel_names=channel_names,
        sampling_rate_hz=sampling_rate_hz,
        sample_count=sample_count,
        low_hz=low_hz,
        high_hz=high_hz,
        notch_hz=notch_hz,
        notch_q=notch_q,
        bad_channel_spec=bad_channel_spec,
        artifact_rejection_spec=artifact_rejection_spec,
        removal_spec=removal_spec,
        fit_low_hz=fit_low_hz,
        fit_high_hz=fit_high_hz,
        classifier_version=meta["classifier_version"] or "unavailable",
        onnxruntime_version=meta["onnxruntime_version"],
        iclabel_model_sha256=meta["iclabel_model_sha256"],
        recommendation_rule_version=meta["recommendation_rule_version"],
        ica_max_iter=ICA_DEFAULT_MAX_ITER,
    )


def _safe_dir_segment(raw: str) -> str:
    cleaned = "".join(ch if ch.isalnum() or ch in ("-", "_") else "_" for ch in str(raw))
    return cleaned or "unknown"


def _persist_fitted_model(target_dir: Path, fingerprint: str, model: FittedIcaModel, *, max_per_recording: int) -> None:
    target_dir.mkdir(parents=True, exist_ok=True)
    nonce = uuid.uuid4().hex[:8]
    fif_path = target_dir / f"{fingerprint}-ica.fif"
    meta_path = target_dir / f"{fingerprint}.meta.json"
    tmp_fif = target_dir / f".{fingerprint}.{nonce}-ica.fif"
    tmp_meta = target_dir / f".{fingerprint}.{nonce}.meta.json"
    try:
        model.ica.save(tmp_fif, overwrite=True, verbose=False)
        fif_sha = hashlib.sha256(tmp_fif.read_bytes()).hexdigest()
        payload = {
            "schema_version": 1,
            "fingerprint": fingerprint,
            "fif_sha256": fif_sha,
            "channel_names": list(model.channel_names),
            "sampling_rate_hz": float(model.sampling_rate_hz),
            "sample_count": int(model.sample_count),
            "eog_channels_used": list(model.eog_channels_used),
            "eog_channel_source": model.eog_channel_source,
            "ecg_channels_used": list(model.ecg_channels_used),
            "recommended_exclude_components": list(model.recommended_exclude_components),
            "component_scores": list(model.component_scores),
            "ica_algorithm": model.ica_algorithm,
            "fit_bandpass_hz": None if model.fit_bandpass_hz is None else list(model.fit_bandpass_hz),
            "fit_band_limited_by_nyquist": bool(model.fit_band_limited_by_nyquist),
            "classifier_method": model.classifier_method,
            "classifier_status": model.classifier_status,
            "classifier_unavailable_reason": model.classifier_unavailable_reason,
            "classifier_version": model.classifier_version,
            "onnxruntime_version": model.onnxruntime_version,
            "iclabel_model_sha256": model.iclabel_model_sha256,
            "recommendation_rule_version": model.recommendation_rule_version,
            "iclabel_exclude_threshold": float(model.iclabel_exclude_threshold),
            "iclabel_max_brain_probability": float(model.iclabel_max_brain_probability),
            "effective_rank": model.effective_rank,
            "n_components_requested": model.n_components_requested,
            "rank_capped": bool(model.rank_capped),
            "ica_converged": bool(model.ica_converged),
            "ica_n_iter": model.ica_n_iter,
            "ica_max_iter": int(model.ica_max_iter),
            "fit_duration_s": model.fit_duration_s,
            "fit_sample_sufficiency": model.fit_sample_sufficiency,
            "fit_warnings": list(model.fit_warnings),
        }
        tmp_meta.write_text(json.dumps(payload, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
        os.replace(tmp_fif, fif_path)
        os.replace(tmp_meta, meta_path)
        _prune_recording_dir(target_dir, max_per_recording)
    finally:
        tmp_fif.unlink(missing_ok=True)
        tmp_meta.unlink(missing_ok=True)


def _prune_recording_dir(target_dir: Path, max_per_recording: int) -> None:
    metas = sorted(target_dir.glob("*.meta.json"), key=lambda p: p.stat().st_mtime, reverse=True)
    for stale in metas[max_per_recording:]:
        fp = stale.name.removesuffix(".meta.json")
        stale.unlink(missing_ok=True)
        (target_dir / f"{fp}-ica.fif").unlink(missing_ok=True)


def _load_persisted_model(target_dir: Path, fingerprint: str) -> FittedIcaModel | None:
    meta_path = target_dir / f"{fingerprint}.meta.json"
    fif_path = target_dir / f"{fingerprint}-ica.fif"
    if not meta_path.is_file() or not fif_path.is_file():
        return None
    try:
        meta = json.loads(meta_path.read_text(encoding="utf-8"))
        if meta.get("fingerprint") != fingerprint:
            return None
        actual_sha = hashlib.sha256(fif_path.read_bytes()).hexdigest()
        if actual_sha != meta.get("fif_sha256"):
            return None
        ica = mne.preprocessing.read_ica(fif_path, verbose=False)
        names = tuple(str(ch) for ch in meta["channel_names"])
        sfreq = float(meta["sampling_rate_hz"])
        info = mne.create_info(list(names), sfreq=sfreq, ch_types="eeg")
        band_raw = meta.get("fit_bandpass_hz")
        band = (float(band_raw[0]), float(band_raw[1])) if isinstance(band_raw, list) and len(band_raw) == 2 else None
        return FittedIcaModel(
            ica=ica,
            info=info,
            channel_names=names,
            sampling_rate_hz=sfreq,
            sample_count=int(meta["sample_count"]),
            eog_channels_used=tuple(meta.get("eog_channels_used") or ()),
            eog_channel_source=meta.get("eog_channel_source"),
            ecg_channels_used=tuple(meta.get("ecg_channels_used") or ()),
            recommended_exclude_components=tuple(int(i) for i in (meta.get("recommended_exclude_components") or ())),
            component_scores=tuple(meta.get("component_scores") or ()),
            ica_algorithm=str(meta.get("ica_algorithm") or "extended_infomax"),
            fit_bandpass_hz=band,
            fit_band_limited_by_nyquist=bool(meta.get("fit_band_limited_by_nyquist", False)),
            classifier_method=str(meta.get("classifier_method") or "eog_ecg_correlation"),
            classifier_status=str(meta.get("classifier_status") or "fallback_eog_ecg"),
            classifier_unavailable_reason=meta.get("classifier_unavailable_reason"),
            classifier_version=meta.get("classifier_version"),
            onnxruntime_version=meta.get("onnxruntime_version"),
            iclabel_model_sha256=meta.get("iclabel_model_sha256"),
            recommendation_rule_version=meta.get("recommendation_rule_version"),
            iclabel_exclude_threshold=float(meta.get("iclabel_exclude_threshold", 0.75)),
            iclabel_max_brain_probability=float(meta.get("iclabel_max_brain_probability", 0.25)),
            effective_rank=meta.get("effective_rank"),
            n_components_requested=meta.get("n_components_requested"),
            rank_capped=bool(meta.get("rank_capped", False)),
            ica_converged=bool(meta.get("ica_converged", True)),
            ica_n_iter=meta.get("ica_n_iter"),
            ica_max_iter=int(meta.get("ica_max_iter", ICA_DEFAULT_MAX_ITER)),
            fit_duration_s=meta.get("fit_duration_s"),
            fit_sample_sufficiency=meta.get("fit_sample_sufficiency"),
            fit_warnings=tuple(meta.get("fit_warnings") or ()),
        )
    except Exception:
        return None


class IcaReviewCache:
    """Thread-safe bounded LRU + optional disk-backed store for reviewed FittedIcaModel objects."""

    def __init__(self, max_entries: int = 16, *, storage_dir: Path | str | None = None) -> None:
        self._max_entries = max(1, int(max_entries))
        self._storage_dir = Path(storage_dir) if storage_dir is not None else None
        self._lock = RLock()
        self._entries: OrderedDict[str, tuple[str, Any]] = OrderedDict()
        self._recording_tokens: dict[str, object] = {}

    def publication_token(self, recording_id: str) -> object:
        with self._lock:
            return self._recording_tokens.setdefault(str(recording_id), object())

    def get(self, fingerprint: str, *, recording_id: str | None = None) -> Any | None:
        with self._lock:
            entry = self._entries.get(fingerprint)
            if entry is not None:
                self._entries.move_to_end(fingerprint)
                return entry[1]
            if self._storage_dir is None or not self._storage_dir.is_dir():
                return None
            dirs = (
                [self._storage_dir / _safe_dir_segment(recording_id)]
                if recording_id is not None
                else [p for p in self._storage_dir.iterdir() if p.is_dir()]
            )
            for rec_dir in dirs:
                restored = _load_persisted_model(rec_dir, fingerprint)
                if restored is not None:
                    rec_key = str(recording_id) if recording_id is not None else rec_dir.name
                    self._entries[fingerprint] = (rec_key, restored)
                    self._entries.move_to_end(fingerprint)
                    while len(self._entries) > self._max_entries:
                        self._entries.popitem(last=False)
                    return restored
            return None

    def put(self, fingerprint: str, model: Any, *, recording_id: str, token: object) -> None:
        with self._lock:
            if self._recording_tokens.get(str(recording_id)) is not token:
                return
            self._entries[fingerprint] = (str(recording_id), model)
            self._entries.move_to_end(fingerprint)
            while len(self._entries) > self._max_entries:
                self._entries.popitem(last=False)
            if (
                self._storage_dir is not None
                and isinstance(model, FittedIcaModel)
                and hasattr(model.ica, "save")
            ):
                _persist_fitted_model(
                    self._storage_dir / _safe_dir_segment(recording_id),
                    fingerprint,
                    model,
                    max_per_recording=self._max_entries,
                )

    def invalidate_recording(self, recording_id: str) -> int:
        with self._lock:
            self._recording_tokens.pop(str(recording_id), None)
            keys = [fp for fp, (rec_id, _model) in self._entries.items() if rec_id == recording_id]
            for fp in keys:
                self._entries.pop(fp, None)
            if self._storage_dir is not None:
                rec_dir = self._storage_dir / _safe_dir_segment(recording_id)
                if rec_dir.exists():
                    shutil.rmtree(rec_dir, ignore_errors=True)
            return len(keys)

    def clear(self) -> None:
        with self._lock:
            self._entries.clear()
            self._recording_tokens.clear()
