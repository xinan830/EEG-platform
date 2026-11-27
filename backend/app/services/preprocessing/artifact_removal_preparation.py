"""Prepare auxiliary references, broadband ICA fit copy, and identity-bound removal."""

from __future__ import annotations

from contextlib import contextmanager
import numpy as np

from app.scientific.artifact_removal import apply_offline_artifact_removal, fit_ica_model
from app.scientific.contracts.preprocessing import ScientificFilterSpec
from app.scientific.ica_classifier import ICLABEL_MIN_SFREQ_HZ, compute_ica_fit_bandpass_hz
from app.scientific.primitives import preprocess_blocks
from app.services.preprocessing.artifact_references import prepare_artifact_references
from app.services.preprocessing.ica_review_cache import compute_recording_ica_fit_fingerprint
from app.services.preprocessing.mapped_arrays import MappedArrays


@contextmanager
def prepare_broadband_ica_fit_matrix(
    recordings,
    recording,
    *,
    target: np.ndarray,
    names: tuple[str, ...],
    sfreq: float,
    filters: ScientificFilterSpec,
    bad_spec,
    spec,
    owner,
    raw_source: np.ndarray | None = None,
    session=None,
    check_cancelled=lambda: None,
):
    """Build temporary broadband (1–100 Hz, synced notch) ICA fit copy sharing target's NaN mask."""
    fit_low_hz, fit_high_hz, fit_limited = compute_ica_fit_bandpass_hz(
        sfreq, main_low_hz=filters.low_hz, main_high_hz=filters.high_hz,
    )
    if float(sfreq) < ICLABEL_MIN_SFREQ_HZ:
        yield None, (float(filters.low_hz), float(filters.high_hz)), True
        return

    block_samples = max(1, min(65536, (8 * 1024**2) // max(8, 8 * len(names))))
    opened_session = None
    allocated_source = None
    broadband = None
    try:
        src = raw_source
        if src is None:
            active_session = session
            if active_session is None:
                ref_channels = (*spec.eog_channels, *spec.ecg_channels)
                ref_opts = {"artifact_reference_channels": ref_channels} if ref_channels else {}
                active_session = (
                    recordings.open_access_session(recording, **ref_opts)
                    if bad_spec is None or not bad_spec.is_active
                    else recordings.open_access_session(recording, bad_channel_spec=bad_spec, **ref_opts)
                )
                opened_session = active_session
            allocated_source = owner.allocate(target.shape)
            for start in range(0, len(target), block_samples):
                check_cancelled()
                stop = min(start + block_samples, len(target))
                allocated_source[start:stop] = active_session.read_samples(
                    start, stop, names, check_cancelled=check_cancelled,
                ).values_v
            src = allocated_source
        broadband = owner.allocate(target.shape)
        preprocess_blocks(
            src, broadband, sfreq, owner.allocate,
            low_hz=fit_low_hz, high_hz=fit_high_hz, notch_hz=filters.notch_hz, notch_q=filters.notch_q,
            check_cancelled=check_cancelled, release=owner.release, block_samples=block_samples,
        )
        if allocated_source is not None:
            owner.release(allocated_source)
            allocated_source = None
        for start in range(0, len(target), block_samples):
            check_cancelled()
            stop = min(start + block_samples, len(target))
            invalid = ~np.isfinite(target[start:stop])
            if np.any(invalid):
                chunk = broadband[start:stop]
                chunk[invalid] = np.nan
        yield broadband, (fit_low_hz, fit_high_hz), fit_limited
    finally:
        if broadband is not None:
            owner.release(broadband)
        if allocated_source is not None:
            owner.release(allocated_source)
        if opened_session is not None:
            opened_session.close()


def apply_prepared_removal(
    recordings, recording, session, target, names, spec, *,
    filters: ScientificFilterSpec, bad_spec, artifact_spec,
    owner, check_cancelled, raw_source=None,
):
    cache = getattr(recordings, "ica_review_cache", None)
    token = cache.publication_token(recording.id) if cache is not None else None
    with prepare_artifact_references(recordings, recording, spec, sample_count=session.sample_count,
            sfreq=session.sampling_rate_hz, filters=filters, allocate=owner.allocate,
            release=owner.release, check_cancelled=check_cancelled) as references:
        fitted, fingerprint, reused = None, None, False
        if spec.method == "ica_infomax":
            fingerprint = compute_recording_ica_fit_fingerprint(
                identity=session.identity,
                channel_names=names,
                sampling_rate_hz=session.sampling_rate_hz,
                sample_count=session.sample_count,
                low_hz=filters.low_hz,
                high_hz=filters.high_hz,
                notch_hz=filters.notch_hz,
                notch_q=filters.notch_q,
                bad_channel_spec=bad_spec,
                artifact_rejection_spec=artifact_spec,
                removal_spec=spec,
            )
            if spec.ica_fit_fingerprint and fingerprint != spec.ica_fit_fingerprint:
                raise ValueError("ICA_FIT_FINGERPRINT_MISMATCH: 输入或辅助通道已变化，请重新审查 ICA")
            fitted = cache.get(fingerprint, recording_id=recording.id) if cache is not None else None
            reused = fitted is not None
            if fitted is None:
                if spec.ica_fit_fingerprint:
                    raise ValueError(
                        "ICA_REVIEW_MODEL_EXPIRED: 已审查的 ICA 模型已失效或不可恢复，请重新打开 ICA 审查并确认成分"
                    )
                provenance = (
                    recordings.analysis_signal_provenance(recording)
                    if hasattr(recordings, "analysis_signal_provenance") else None
                )
                with prepare_broadband_ica_fit_matrix(
                    recordings, recording, target=target, names=names, sfreq=session.sampling_rate_hz,
                    filters=filters, bad_spec=bad_spec, spec=spec, owner=owner,
                    raw_source=raw_source, session=session, check_cancelled=check_cancelled,
                ) as (fit_values, fit_band, fit_limited):
                    fitted = fit_ica_model(
                        target, session.sampling_rate_hz, names, spec, references=references,
                        fit_values_v=fit_values, signal_provenance=provenance,
                        fit_bandpass_hz=fit_band, fit_band_limited_by_nyquist=fit_limited,
                        fit_notch_hz=filters.notch_hz,
                    )
                if cache is not None:
                    cache.put(fingerprint, fitted, recording_id=recording.id, token=token)
        result = apply_offline_artifact_removal(target, session.sampling_rate_hz, names, spec,
            prefitted_ica=fitted, ica_fit_fingerprint=fingerprint, references=references)
        evidence = result.to_evidence()
        evidence["ica_model_reused"] = reused
        return result.data_v, evidence


def prepare_reviewed_ica(
    recordings,
    recording,
    pre_ica,
    spec,
    filters,
    *,
    bad_spec=None,
    signal_provenance=None,
):
    """Shared reference & broadband-fit preparation for whole-recording ICA review and execution."""
    owner = MappedArrays()
    try:
        with prepare_artifact_references(
            recordings,
            recording,
            spec,
            sample_count=len(pre_ica.data),
            sfreq=pre_ica.sfreq,
            filters=filters,
            allocate=owner.allocate,
            release=owner.release,
        ) as references:
            with prepare_broadband_ica_fit_matrix(
                recordings,
                recording,
                target=pre_ica.data,
                names=tuple(pre_ica.channel_names),
                sfreq=pre_ica.sfreq,
                filters=filters,
                bad_spec=bad_spec,
                spec=spec,
                owner=owner,
            ) as (fit_values, fit_band, fit_limited):
                return fit_ica_model(
                    pre_ica.data,
                    pre_ica.sfreq,
                    pre_ica.channel_names,
                    spec,
                    references=references,
                    fit_values_v=fit_values,
                    signal_provenance=signal_provenance,
                    fit_bandpass_hz=fit_band,
                    fit_band_limited_by_nyquist=fit_limited,
                    fit_notch_hz=filters.notch_hz,
                )
    finally:
        owner.close()
