"""Prepare auxiliary references and apply one identity-bound removal transaction."""
from app.scientific.artifact_removal import apply_offline_artifact_removal, fit_ica_model
from app.scientific.contracts.preprocessing import ScientificFilterSpec
from app.services.artifact_references import prepare_artifact_references
from app.services.ica_review_cache import compute_ica_fit_fingerprint


def apply_prepared_removal(recordings, recording, session, target, names, spec, *,
                           filters: ScientificFilterSpec, bad_spec, artifact_spec,
                           owner, check_cancelled):
    cache = getattr(recordings, "ica_review_cache", None)
    token = cache.publication_token(recording.id) if cache is not None else None
    with prepare_artifact_references(recordings, recording, spec, sample_count=session.sample_count,
            sfreq=session.sampling_rate_hz, filters=filters, allocate=owner.allocate,
            release=owner.release, check_cancelled=check_cancelled) as references:
        fitted, fingerprint, reused = None, None, False
        if spec.method == "ica_infomax":
            identity = session.identity
            fingerprint = compute_ica_fit_fingerprint(recording_id=identity.recording_id,
                source_sha256=identity.source_sha256, signal_view_sha256=identity.signal_view_sha256,
                channel_names=names, sampling_rate_hz=session.sampling_rate_hz,
                sample_count=session.sample_count, low_hz=filters.low_hz, high_hz=filters.high_hz,
                notch_hz=filters.notch_hz, notch_q=filters.notch_q,
                bad_channel_spec=bad_spec, artifact_rejection_spec=artifact_spec, removal_spec=spec)
            if spec.ica_fit_fingerprint and fingerprint != spec.ica_fit_fingerprint:
                raise ValueError("ICA_FIT_FINGERPRINT_MISMATCH: 输入或辅助通道已变化，请重新审查 ICA")
            fitted = cache.get(fingerprint) if cache is not None else None
            reused = fitted is not None
            if fitted is None:
                fitted = fit_ica_model(target, session.sampling_rate_hz, names, spec, references=references)
                if cache is not None:
                    cache.put(fingerprint, fitted, recording_id=recording.id, token=token)
        result = apply_offline_artifact_removal(target, session.sampling_rate_hz, names, spec,
            prefitted_ica=fitted, ica_fit_fingerprint=fingerprint, references=references)
        evidence = result.to_evidence()
        evidence["ica_model_reused"] = reused
        return result.data_v, evidence
