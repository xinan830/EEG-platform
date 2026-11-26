"""Offline EEG ICA component classification via mne-icalabel (ONNX CPU backend).

Pure scientific module (zero service/HTTP/persistence imports). Enforces explicit
scientific eligibility gates (CAR montage, complete unique 3D electrode positions,
sfreq >= 104 Hz, Extended Infomax convergence, Nyquist band awareness) and a strict
brain/other non-auto-exclusion guard.
"""

from __future__ import annotations

from dataclasses import dataclass
from functools import lru_cache
import hashlib
import logging
import math
from pathlib import Path
import threading
from typing import Any, Mapping, Sequence
import warnings

import mne
import numpy as np

from app.scientific.contracts.preprocessing import STANDARD_MONTAGE_NAME


ICLABEL_CLASSES: tuple[str, ...] = (
    "brain", "muscle artifact", "eye blink", "heart beat", "line noise", "channel noise", "other",
)
ICLABEL_ARTIFACT_CLASSES: frozenset[str] = frozenset(
    {"muscle artifact", "eye blink", "heart beat", "line noise", "channel noise"}
)
ICLABEL_PROTECTED_CLASSES: frozenset[str] = frozenset({"brain", "other"})
ICLABEL_EXCLUDE_THRESHOLD = 0.75
ICLABEL_MAX_BRAIN_PROBABILITY = 0.25
ICLABEL_MIN_SFREQ_HZ = 104.0
ICLABEL_FULL_BAND_MIN_SFREQ_HZ = 204.0
ICLABEL_MIN_CHANNELS = 4
ICLABEL_RECOMMENDATION_RULE_VERSION = "iclabel-rec-v2-brain-guard"
ICA_DEFAULT_MAX_ITER = 500
ICA_RANK_REL_TOL = 1e-6
_INFOMAX_LOG_LOCK = threading.Lock()
_INFOMAX_ACTIVE_THREADS: set[int] = set()


class _SuppressInfomaxStepFilter(logging.Filter):
    def filter(self, record: logging.LogRecord) -> bool:
        return threading.get_ident() not in _INFOMAX_ACTIVE_THREADS


_SUPPRESS_STEP_FILTER = _SuppressInfomaxStepFilter()


@dataclass(frozen=True)
class IcaComponentClassification:
    component_index: int
    iclabel_label: str
    iclabel_probability: float
    iclabel_probabilities: dict[str, float]
    recommended_exclude: bool
    recommendation_reasons: tuple[str, ...]


@dataclass(frozen=True)
class IcaClassificationResult:
    classifier_method: str
    classifier_status: str
    classifier_unavailable_reason: str | None = None
    classifier_version: str | None = None
    onnxruntime_version: str | None = None
    iclabel_model_sha256: str | None = None
    recommendation_rule_version: str = ICLABEL_RECOMMENDATION_RULE_VERSION
    iclabel_exclude_threshold: float = ICLABEL_EXCLUDE_THRESHOLD
    iclabel_max_brain_probability: float = ICLABEL_MAX_BRAIN_PROBABILITY
    components: tuple[IcaComponentClassification, ...] = ()


def compute_ica_fit_bandpass_hz(
    sfreq_hz: float, *, main_low_hz: float = 1.0, main_high_hz: float = 50.0,
) -> tuple[float, float, bool]:
    """Return (fit_low_hz, fit_high_hz, limited_by_nyquist) using one unified Nyquist-2 formula."""
    rate = float(sfreq_hz)
    nyquist = rate / 2.0
    if rate >= ICLABEL_FULL_BAND_MIN_SFREQ_HZ:
        return 1.0, 100.0, False
    if rate >= ICLABEL_MIN_SFREQ_HZ:
        return 1.0, round(nyquist - 2.0, 6), True
    low_hz = max(0.1, min(1.0, float(main_low_hz)))
    high_hz = round(min(float(main_high_hz), nyquist - 2.0), 6)
    return low_hz, max(low_hz + 0.5, high_hz), True


def compute_effective_rank(values_v: np.ndarray, *, rel_tol: float = ICA_RANK_REL_TOL) -> int:
    """Compute numerical effective rank via centered channel covariance eigenvalues."""
    data = np.asarray(values_v, dtype=np.float64)
    if data.ndim != 2 or data.shape[0] == 0 or data.shape[1] == 0:
        return 0
    centered = data - np.mean(data, axis=0, keepdims=True)
    cov = (centered.T @ centered) / max(1, centered.shape[0] - 1)
    eigvals = np.linalg.eigvalsh(cov)
    max_eig = float(np.max(eigvals)) if eigvals.size else 0.0
    if not math.isfinite(max_eig) or max_eig <= 0.0:
        return 0
    return int(np.count_nonzero(np.sqrt(np.maximum(eigvals, 0.0) / max_eig) > rel_tol))


def fit_extended_infomax(
    raw_fit: mne.io.BaseRaw, *, n_components: int, random_state: int, max_iter: int = ICA_DEFAULT_MAX_ITER,
) -> tuple[Any, bool, int | None]:
    """Verify MNE's weight-change/small-angle stops from unrounded solver log arguments."""
    tid = threading.get_ident()
    last_step: list[tuple[int, float, int]] = []

    class _StepCapture(logging.Handler):
        def emit(self, record: logging.LogRecord) -> None:
            if threading.get_ident() != tid:
                return
            if not str(record.msg).startswith("step %d - lrate") or not isinstance(record.args, tuple) or len(record.args) != 4:
                return
            step, _, change, angle = record.args
            previous_step, _, small_angles = last_step[-1] if last_step else (0, 0.0, 0)
            if int(step) <= previous_step:
                small_angles = 0  # MNE restarted after weights blew up.
            small_angles = small_angles + 1 if float(angle) <= 60.0 else 0
            last_step[:] = [(int(step), float(change), small_angles)]

    mne_logger = logging.getLogger("mne")
    capture = _StepCapture(level=logging.INFO)
    ica = mne.preprocessing.ICA(
        n_components=n_components, method="infomax",
        fit_params={"extended": True, "verbose": True, "w_change": 1e-12, "anneal_deg": 60.0, "n_small_angle": 20},
        random_state=random_state, max_iter=max_iter, verbose="INFO",
    )
    with _INFOMAX_LOG_LOCK:
        _INFOMAX_ACTIVE_THREADS.add(tid)
        for existing in mne_logger.handlers:
            if isinstance(existing, logging.StreamHandler) and _SUPPRESS_STEP_FILTER not in existing.filters:
                existing.addFilter(_SUPPRESS_STEP_FILTER)
        mne_logger.addHandler(capture)
    try:
        with warnings.catch_warnings(record=True) as caught:
            warnings.simplefilter("always", RuntimeWarning)
            ica.fit(raw_fit, verbose="INFO")
    finally:
        with _INFOMAX_LOG_LOCK:
            mne_logger.removeHandler(capture)
            _INFOMAX_ACTIVE_THREADS.discard(tid)

    saw_warn = any(
        "did not converge" in str(w.message).lower() or "max_iter" in str(w.message).lower() for w in caught
    )
    reported_iter = int(ica.n_iter_) if getattr(ica, "n_iter_", None) is not None else None
    if last_step:
        true_iter, final_wchange, small_angles = last_step[-1]
        converged = (not saw_warn) and ((true_iter > 2 and final_wchange < 1e-12) or small_angles > 20)
        return ica, converged, true_iter
    return ica, False, reported_iter


@lru_cache(maxsize=1)
def _hash_iclabel_model(path: Path, size: int, mtime_ns: int, ctime_ns: int) -> str:
    """Cache a successful digest by file identity; timestamp arguments invalidate stale evidence."""
    return hashlib.sha256(path.read_bytes()).hexdigest()[:16]


def _iclabel_onnx_sha256() -> str | None:
    try:
        import mne_icalabel
    except (ImportError, OSError):
        return None
    pkg_dir = Path(mne_icalabel.__file__).resolve().parent
    candidate = (pkg_dir / "iclabel" / "network" / "assets" / "ICLabelNet.onnx").resolve()
    try:
        stat = candidate.stat()
    except FileNotFoundError:
        return None
    return _hash_iclabel_model(candidate, stat.st_size, stat.st_mtime_ns, stat.st_ctime_ns)


def _load_iclabel_runtime() -> tuple[Any | None, dict[str, str | None]]:
    try:
        import mne_icalabel
        from mne_icalabel.iclabel import iclabel_label_components
        import onnxruntime
    except (ImportError, OSError):
        return None, {
            "mne_icalabel_version": None, "onnxruntime_version": None, "iclabel_model_sha256": None,
            "recommendation_rule_version": ICLABEL_RECOMMENDATION_RULE_VERSION, "classifier_version": None,
        }
    mne_ic_ver, ort_ver = str(getattr(mne_icalabel, "__version__", "unknown")), str(getattr(onnxruntime, "__version__", "unknown"))
    try:
        weight_sha = _iclabel_onnx_sha256()
    except OSError:
        weight_sha = None
    return iclabel_label_components if weight_sha else None, {
        "mne_icalabel_version": mne_ic_ver, "onnxruntime_version": ort_ver,
        "iclabel_model_sha256": weight_sha, "recommendation_rule_version": ICLABEL_RECOMMENDATION_RULE_VERSION,
        "classifier_version": f"mne-icalabel-{mne_ic_ver}-onnx-{ort_ver}-w{weight_sha[:8]}" if weight_sha else None,
        "unavailable_reason": None if weight_sha else "icalabel_model_unavailable",
    }

def get_iclabel_runtime_metadata() -> dict[str, str | None]:
    return dict(_load_iclabel_runtime()[1])

def get_classifier_version() -> str:
    return get_iclabel_runtime_metadata().get("classifier_version") or "unavailable"

@lru_cache(maxsize=1)
def _standard_3d_montage_positions():
    pos = mne.channels.make_standard_montage(STANDARD_MONTAGE_NAME).get_positions()
    ch_pos = {name.casefold(): np.asarray(xyz, dtype=np.float64) for name, xyz in pos["ch_pos"].items()}
    return ch_pos, pos.get("nasion"), pos.get("lpa"), pos.get("rpa"), str(pos.get("coord_frame", "head"))


def _resolve_car_base_labels(
    channel_names: tuple[str, ...], signal_provenance: Mapping[str, object] | None,
) -> dict[str, str] | None:
    """Verify every channel belongs to one common-average reference (CAR) and map to scalp site."""
    if not signal_provenance:
        return None
    formulas_raw = signal_provenance.get("formulas")
    if isinstance(formulas_raw, Sequence) and not isinstance(formulas_raw, (str, bytes)) and len(formulas_raw) > 0:
        by_name = {
            str(row["name"]).casefold(): row
            for row in formulas_raw
            if isinstance(row, Mapping) and row.get("name") is not None
        }
        base_labels: dict[str, str] = {}
        pool_sig: frozenset[str] | None = None
        for ch_name in channel_names:
            formula = by_name.get(ch_name.casefold())
            if formula is None or str(formula.get("negative_kind", "")).strip().casefold() != "mean":
                return None
            positive = str(formula.get("positive", "")).strip()
            if not positive:
                return None
            neg_raw = formula.get("negative_labels")
            if not isinstance(neg_raw, Sequence) or isinstance(neg_raw, (str, bytes)) or not neg_raw:
                return None
            cur_pool = frozenset(str(item).strip().casefold() for item in neg_raw if str(item).strip())
            if len(cur_pool) != len(neg_raw) or (pool_sig is not None and cur_pool != pool_sig):
                return None
            pool_sig = cur_pool
            base_labels[ch_name] = positive
        if pool_sig != frozenset(label.casefold() for label in base_labels.values()):
            return None
        return base_labels

    if str(signal_provenance.get("analysis_reference", "")).strip().casefold() in {"average", "car", "common_average"}:
        base_labels = {}
        for ch_name in channel_names:
            parts = ch_name.rsplit("-", 1)
            if len(parts) == 2 and parts[1].strip().casefold() not in {"avg", "car"}:
                return None
            base_labels[ch_name] = parts[0].strip()
        return base_labels
    return None


def _build_channel_dig_montage(channel_names: tuple[str, ...], base_labels: Mapping[str, str], electrode_geometry=None):
    if len(channel_names) < ICLABEL_MIN_CHANNELS:
        return None
    if electrode_geometry is None:
        std_pos, nasion, lpa, rpa, coord_frame = _standard_3d_montage_positions()
    else:
        from app.scientific.electrode_geometry import geometry_montage
        positions = geometry_montage(electrode_geometry).get_positions()
        std_pos = {name.casefold(): np.asarray(xyz) for name, xyz in positions["ch_pos"].items()}
        nasion, lpa, rpa, coord_frame = None, None, None, "head"
    ch_pos: dict[str, np.ndarray] = {}
    seen_xyz: set[tuple[float, float, float]] = set()
    for ch_name in channel_names:
        xyz = std_pos.get(base_labels.get(ch_name, "").strip().casefold())
        if xyz is None or not np.all(np.isfinite(xyz)):
            return None
        key = (round(float(xyz[0]), 6), round(float(xyz[1]), 6), round(float(xyz[2]), 6))
        if key in seen_xyz:
            return None
        seen_xyz.add(key)
        ch_pos[ch_name] = xyz
    return mne.channels.make_dig_montage(ch_pos=ch_pos, nasion=nasion, lpa=lpa, rpa=rpa, coord_frame=coord_frame)


def evaluate_iclabel_recommendation(
    probabilities: Mapping[str, float],
    *,
    exclude_threshold: float = ICLABEL_EXCLUDE_THRESHOLD,
    max_brain_probability: float = ICLABEL_MAX_BRAIN_PROBABILITY,
) -> tuple[bool, tuple[str, ...]]:
    """Apply conservative recommendation gate: only high-confidence artifact with low brain probability."""
    top_label = max(ICLABEL_CLASSES, key=lambda cls: float(probabilities.get(cls, 0.0)))
    top_prob = float(probabilities.get(top_label, 0.0))
    brain_prob = float(probabilities.get("brain", 0.0))
    if top_label in ICLABEL_ARTIFACT_CLASSES and top_prob >= exclude_threshold and brain_prob < max_brain_probability:
        return True, (f"iclabel:{top_label.replace(' ', '_')}:{top_prob:.2f}",)
    return False, ()


def classify_ica_components(
    raw_fit: mne.io.BaseRaw, ica: Any, channel_names: Sequence[str], sfreq_hz: float, *,
    signal_provenance: Mapping[str, object] | None = None,
    fit_band_limited_by_nyquist: bool = False, ica_converged: bool = True,
    exclude_threshold: float = ICLABEL_EXCLUDE_THRESHOLD,
    max_brain_probability: float = ICLABEL_MAX_BRAIN_PROBABILITY,
) -> IcaClassificationResult:
    """Run explicit eligibility gates and classify ICA components with ONNX ICLabel when eligible."""
    iclabel_fn, meta = _load_iclabel_runtime()
    meta_kwargs = dict(
        classifier_version=meta["classifier_version"], onnxruntime_version=meta["onnxruntime_version"],
        iclabel_model_sha256=meta["iclabel_model_sha256"],
        recommendation_rule_version=meta["recommendation_rule_version"] or ICLABEL_RECOMMENDATION_RULE_VERSION,
        iclabel_exclude_threshold=exclude_threshold, iclabel_max_brain_probability=max_brain_probability,
    )
    if iclabel_fn is None:
        return IcaClassificationResult(
            "eog_ecg_correlation", "unavailable", meta.get("unavailable_reason") or "icalabel_dependency_unavailable", **meta_kwargs)
    names = tuple(str(n) for n in channel_names)
    base_labels = _resolve_car_base_labels(names, signal_provenance)
    if base_labels is None:
        return IcaClassificationResult(
            "eog_ecg_correlation", "fallback_eog_ecg", "non_average_reference_montage", **meta_kwargs)
    samples = raw_fit.get_data()
    if not np.allclose(samples.mean(axis=0), 0.0, rtol=0.0, atol=max(1e-15, float(np.max(np.abs(samples))) * 1e-8)):
        return IcaClassificationResult(
            "eog_ecg_correlation", "fallback_eog_ecg", "fit_samples_not_common_average", **meta_kwargs)
    if not math.isfinite(float(sfreq_hz)) or float(sfreq_hz) < ICLABEL_MIN_SFREQ_HZ:
        return IcaClassificationResult(
            "eog_ecg_correlation", "fallback_eog_ecg", "sampling_rate_too_low_for_icalabel", **meta_kwargs)
    dig_montage = _build_channel_dig_montage(names, base_labels, (signal_provenance or {}).get("electrode_geometry"))
    if dig_montage is None:
        return IcaClassificationResult(
            "eog_ecg_correlation", "fallback_eog_ecg", "electrode_3d_coordinates_incomplete", **meta_kwargs)

    info = mne.create_info(list(names), sfreq=float(sfreq_hz), ch_types="eeg")
    raw_iclabel = mne.io.RawArray(np.array(samples, dtype=np.float64, order="C", copy=True), info, verbose=False)
    raw_iclabel.set_montage(dig_montage, on_missing="raise")
    raw_iclabel.set_eeg_reference(ref_channels=[], verbose=False)  # Already CAR; mark metadata without changing the ICA input.
    try:
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", RuntimeWarning)
            warnings.simplefilter("ignore", UserWarning)
            probs = np.asarray(iclabel_fn(raw_iclabel, ica, inplace=False, backend="onnx"), dtype=np.float64)
    except Exception as exc:
        ort_error = type(exc).__module__ == "onnxruntime.capi.onnxruntime_pybind11_state" and type(exc).__name__ in {
            "Fail", "NoSuchFile", "NoModel", "EngineError", "RuntimeException", "InvalidProtobuf", "InvalidGraph", "EPFail", "NotImplemented",
        }
        if not isinstance(exc, (OSError, ImportError)) and not ort_error:
            raise
        logging.getLogger(__name__).warning("ICLabel inference unavailable: %s", type(exc).__name__)
        return IcaClassificationResult(
            "eog_ecg_correlation", "unavailable", "icalabel_inference_unavailable", **meta_kwargs)

    expected_shape = (int(ica.n_components_), len(ICLABEL_CLASSES))
    if probs.shape != expected_shape or not np.all(np.isfinite(probs)):
        raise RuntimeError(f"ICLabel 输出概率矩阵形态或数值异常: shape={probs.shape}, expected={expected_shape}")

    nyquist_limited = bool(fit_band_limited_by_nyquist or float(sfreq_hz) < ICLABEL_FULL_BAND_MIN_SFREQ_HZ)
    if not ica_converged:
        status, reason, allow_auto_rec = "advisory_only", "ica_not_converged", False
    elif nyquist_limited:
        status, reason, allow_auto_rec = "ready_limited", "fit_band_limited_by_nyquist", False
    else:
        status, reason, allow_auto_rec = "ready", None, True

    items: list[IcaComponentClassification] = []
    for idx in range(expected_shape[0]):
        row_probs = {cls: round(float(probs[idx, col]), 4) for col, cls in enumerate(ICLABEL_CLASSES)}
        top_col = int(np.argmax(probs[idx]))
        rec, reasons = (
            evaluate_iclabel_recommendation(
                {cls: float(probs[idx, col]) for col, cls in enumerate(ICLABEL_CLASSES)},
                exclude_threshold=exclude_threshold, max_brain_probability=max_brain_probability,
            )
            if allow_auto_rec else (False, ())
        )
        items.append(IcaComponentClassification(
            idx, ICLABEL_CLASSES[top_col], round(float(probs[idx, top_col]), 4), row_probs, rec, reasons,
        ))
    return IcaClassificationResult(
        "mne_icalabel_onnx", status, reason, components=tuple(items), **meta_kwargs,
    )


def merge_ica_scores_with_classification(
    *, n_components: int, eog_scores: np.ndarray, eog_names: tuple[str, ...], has_dedicated_eog: bool,
    auto_eog: set[int], ecg_scores: np.ndarray, ecg_names: tuple[str, ...], auto_ecg: set[int],
    explained_variance_ratios: Sequence[float], classification: IcaClassificationResult,
    ica_converged: bool = True,
) -> tuple[tuple[int, ...], tuple[dict[str, object], ...]]:
    """Combine ICLabel 7-class probabilities with dedicated EOG/ECG scores under strict brain/other protection."""
    has_iclabel = (
        classification.classifier_status in {"ready", "ready_limited", "advisory_only"}
        and len(classification.components) == n_components
    )
    allow_auto_rec = ica_converged and classification.classifier_status != "advisory_only"
    recommended_list: list[int] = []
    rows: list[dict[str, object]] = []
    for idx in range(n_components):
        reasons, conflicts = [], []
        cls_item = classification.components[idx] if has_iclabel else None
        protected = cls_item is not None and (
            cls_item.iclabel_label in ICLABEL_PROTECTED_CLASSES
            or float(cls_item.iclabel_probabilities.get("brain", 0.0)) >= classification.iclabel_max_brain_probability
        )
        if allow_auto_rec and cls_item is not None and cls_item.recommended_exclude and not protected:
            reasons.extend(cls_item.recommendation_reasons)

        for tag, score_arr, active, is_hit in (
            ("eog", eog_scores, bool(eog_names) and (not has_iclabel or has_dedicated_eog), idx in auto_eog),
            ("ecg", ecg_scores, bool(ecg_names), idx in auto_ecg),
        ):
            if active and is_hit:
                val = abs(float(score_arr[idx]))
                if not allow_auto_rec:
                    conflicts.append(f"{tag}_correlation_requires_review:{val:.2f}")
                elif protected and cls_item is not None:
                    conflicts.append(f"{tag}_correlation_conflicts_with_{cls_item.iclabel_label.replace(' ', '_')}:{val:.2f}")
                else:
                    reasons.append(f"{tag}_correlation:{val:.2f}")

        rec = len(reasons) > 0
        if rec:
            recommended_list.append(idx)
        rows.append({
            "component_index": idx,
            "eog_score": round(float(eog_scores[idx]), 4) if eog_names else None,
            "ecg_score": round(float(ecg_scores[idx]), 4) if ecg_names else None,
            "explained_variance_ratio": round(float(explained_variance_ratios[idx]), 6),
            "iclabel_label": cls_item.iclabel_label if cls_item is not None else None,
            "iclabel_probability": cls_item.iclabel_probability if cls_item is not None else None,
            "iclabel_probabilities": dict(cls_item.iclabel_probabilities) if cls_item is not None else None,
            "recommendation_reasons": reasons, "advisory_conflicts": conflicts,
            "recommended_exclude": rec, "excluded": rec,
        })
    return tuple(recommended_list), tuple(rows)
