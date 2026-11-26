"""Bounded offline artifact-segment and ICA inspection service for review and API endpoints."""

from __future__ import annotations

import numpy as np
from dataclasses import replace
from app.scientific.primitives import detrend_and_filter_for_inspection

from app.scientific.artifact_reports import ARTIFACT_DETECTION_VERSION, project_artifact_report
from app.scientific.contracts.preprocessing import ArtifactRejectionSpec, BadChannelSpec


def inspect_recording_artifact_segments(
    recordings,
    recording,
    *,
    start_s: float = 0.0,
    duration_s: float = 60.0,
    montage_id: str | None = None,
    bad_channel_spec: BadChannelSpec | None = None,
    artifact_spec: ArtifactRejectionSpec | None = None,
    low_hz: float = 1.0,
    high_hz: float = 50.0,
    notch_hz: float | None = 50.0,
    notch_q: float = 30.0,
    max_cells: int = 4_000_000,
    artifact_reference_channels: tuple[str, ...] = (),
) -> dict[str, object]:
    """Project the authoritative pre-removal report onto a bounded absolute span."""
    from app.scientific.contracts.preprocessing import OfflinePreprocessingSpec, ScientificFilterSpec

    spec = replace(artifact_spec or ArtifactRejectionSpec(), policy='annotate').normalized()
    target_recording = recording
    if montage_id is not None:
        target_recording = recordings.select_analysis_montage(recording, montage_id)
    norm_bad = bad_channel_spec.normalized() if bad_channel_spec is not None else BadChannelSpec()
    filters = ScientificFilterSpec(low_hz, high_hz, notch_hz, notch_q,
                                 scope='whole_recording_before_window_slice')
    if artifact_reference_channels:
        from app.services.artifact_references import discover_artifact_references
        catalogue = discover_artifact_references(recordings, recording)
        available = {channel["name"].casefold() for channel in catalogue["channels"]}
        if any(name.casefold() not in available for name in artifact_reference_channels):
            raise ValueError("ARTIFACT_REFERENCE_UNAVAILABLE: 选择的辅助通道不存在或不是电压信号")
    prepared = recordings.offline_scientific_preprocessing.prepare_continuous(
        target_recording, low_hz=low_hz, high_hz=high_hz, notch_hz=notch_hz,
        preprocessing_spec=OfflinePreprocessingSpec(filters, norm_bad, spec),
        artifact_reference_channels=artifact_reference_channels,
    )
    sfreq, total = prepared.sfreq, len(prepared.data)
    start_sample = max(0, min(total, int(np.floor(float(start_s) * sfreq))))
    requested_samples = max(1, int(round(float(duration_s) * sfreq)))
    max_samples = max(1, max_cells // max(1, len(prepared.channel_names)))
    end_sample = min(total, start_sample + min(requested_samples, max_samples))
    if end_sample <= start_sample:
        raise ValueError('坏段检测时间窗超出录音有效范围')
    report = project_artifact_report(prepared.artifact_report, start_sample, end_sample)
    payload = report.to_dict()
    payload.update({
        'recording_id': recording.id, 'montage_id': montage_id,
        'signal_mode': 'montage_projected' if target_recording.selected_montage_id or target_recording.stored_name.startswith('wpf-montage:') else 'raw_unprojected',
        'channels': list(prepared.channel_names), 'bad_channels': norm_bad.to_evidence(),
        'spec': spec.to_evidence(),
        'inspection_filter': {'low_hz': low_hz, 'high_hz': high_hz, 'notch_hz': notch_hz, 'notch_q': notch_q},
        'detection_scope': {
            'version': ARTIFACT_DETECTION_VERSION, 'scope': 'whole_recording_before_window_slice',
            'start_sample': 0, 'end_sample': total, 'channels': list(prepared.channel_names),
            'prepared_source_identity': prepared.contract.identity,
        },
        'start_s': round(start_sample / sfreq, 6), 'end_s': round(end_sample / sfreq, 6),
    })
    return payload


def inspect_recording_ica_components(
    recordings,
    recording,
    *,
    start_s: float = 0.0,
    duration_s: float | None = None,
    montage_id: str | None = None,
    bad_channel_spec: BadChannelSpec | None = None,
    artifact_rejection_spec: ArtifactRejectionSpec | None = None,
    removal_spec=None,
    low_hz: float = 0.5,
    high_hz: float = 45.0,
    notch_hz: float | None = 50.0,
    max_cells: int = 4_000_000,
) -> dict[str, object]:
    """Fit reproducible MNE infomax ICA and return reviewable IC scores bound to a fit fingerprint."""
    from app.scientific.artifact_removal import inspect_ica_components
    from app.scientific.contracts.artifact_removal import ArtifactRemovalSpec
    from app.services.ica_review_cache import compute_ica_fit_fingerprint

    target_recording = recording
    if montage_id is not None:
        target_recording = recordings.select_analysis_montage(recording, montage_id)

    norm_bad = bad_channel_spec.normalized() if bad_channel_spec is not None else BadChannelSpec()
    norm_art = artifact_rejection_spec.normalized() if artifact_rejection_spec is not None else ArtifactRejectionSpec()
    spec = (removal_spec or ArtifactRemovalSpec(method="ica_infomax")).normalized()
    from app.services.recording_channel_roles import resolve_removal_references
    spec = resolve_removal_references(recordings, target_recording, spec)

    # Full-recording review uses the exact same continuous pre-ICA matrix as mapped_preprocess
    # and caches the fitted ICA model under a verified fingerprint so user-confirmed ICs never drift.
    if duration_s is None and float(start_s) == 0.0:
        ica_cache = getattr(recordings, "ica_review_cache", None)
        ica_token = ica_cache.publication_token(recording.id) if ica_cache is not None else None
        with recordings.open_access_session(
            target_recording,
            bad_channel_spec=norm_bad if norm_bad.is_active else None,
            **({"artifact_reference_channels": (*spec.eog_channels, *spec.ecg_channels)}
               if spec.eog_channels or spec.ecg_channels else {}),
        ) as session:
            identity = session.identity
            sfreq = float(session.sampling_rate_hz)
            total = int(session.sample_count)
            channel_names = tuple(session.channel_names)
        fit_fp = compute_ica_fit_fingerprint(
            recording_id=identity.recording_id,
            source_sha256=identity.source_sha256,
            signal_view_sha256=identity.signal_view_sha256,
            channel_names=channel_names,
            sampling_rate_hz=sfreq,
            sample_count=total,
            low_hz=low_hz,
            high_hz=high_hz,
            notch_hz=notch_hz,
            bad_channel_spec=norm_bad,
            artifact_rejection_spec=norm_art,
            removal_spec=spec,
        )
        cached = ica_cache.get(fit_fp) if ica_cache is not None else None
        pre_ica = recordings.offline_scientific_preprocessing.prepare_continuous(
            target_recording,
            low_hz=low_hz,
            high_hz=high_hz,
            notch_hz=notch_hz,
            bad_channel_spec=norm_bad if norm_bad.is_active else None,
            artifact_rejection_spec=norm_art if norm_art.is_active else None,
            artifact_reference_channels=(*spec.eog_channels, *spec.ecg_channels),
        )
        if cached is not None:
            fitted = cached
            cache_hit = True
        else:
            from app.services.artifact_references import prepare_reviewed_ica
            from app.scientific.contracts.preprocessing import ScientificFilterSpec
            fitted = prepare_reviewed_ica(recordings, target_recording, pre_ica, spec,
                ScientificFilterSpec(low_hz, high_hz, notch_hz))
            if ica_cache is not None:
                ica_cache.put(fit_fp, fitted, recording_id=recording.id, token=ica_token)
            cache_hit = False
        report = fitted.to_inspection_dict(spec.ica_exclude_components)
        from app.services.preprocessing_preview import attach_ica_component_visuals
        attach_ica_component_visuals(report, fitted, pre_ica.data,
                                     signal_provenance=recordings.analysis_signal_provenance(target_recording))
        report.update({
            "recording_id": recording.id,
            "montage_id": montage_id,
            "signal_mode": "continuous_pre_ica",
            "channels": list(channel_names),
            "bad_channels": norm_bad.to_evidence(),
            "artifact_rejection": norm_art.to_evidence(),
            "inspection_filter": {"low_hz": low_hz, "high_hz": high_hz, "notch_hz": notch_hz},
            "ica_fit_fingerprint": fit_fp,
            "ica_cache_hit": cache_hit,
            "can_apply_to_continuous": True,
            "start_sample": 0,
            "end_sample": total,
            "start_s": 0.0,
            "end_s": round(total / sfreq, 6),
        })
        return report

    if spec.eog_channels or spec.ecg_channels:
        raise ValueError("ARTIFACT_REFERENCE_REQUIRES_CONTINUOUS_SCOPE: 专用辅助通道须使用整段 ICA 审查")
    raw_unprojected = montage_id is None
    with recordings.open_access_session(
        target_recording,
        bad_channel_spec=norm_bad if norm_bad.is_active else None,
        raw_unprojected=raw_unprojected,
    ) as session:
        sfreq = float(session.sampling_rate_hz)
        total = int(session.sample_count)
        start_sample = max(0, min(total, int(round(float(start_s) * sfreq))))
        requested_samples = max(1, int(round(float(duration_s or 60.0) * sfreq)))
        max_samples = max(1, max_cells // max(1, len(session.channel_names)))
        end_sample = min(total, start_sample + min(requested_samples, max_samples))
        if end_sample <= start_sample:
            raise ValueError("ICA 审查时间窗超出录音有效范围")
        block = session.read_samples(start_sample, end_sample)
        eeg_band, _ = detrend_and_filter_for_inspection(
            block.values_v, sfreq, low_hz=low_hz, high_hz=high_hz, notch_hz=notch_hz,
        )
        report = inspect_ica_components(eeg_band, sfreq, block.channel_names, spec)

    report.update({
        "recording_id": recording.id,
        "montage_id": montage_id,
        "signal_mode": "raw_unprojected" if raw_unprojected else "montage_projected",
        "channels": list(block.channel_names),
        "bad_channels": norm_bad.to_evidence(),
        "artifact_rejection": norm_art.to_evidence(),
        "inspection_filter": {"low_hz": low_hz, "high_hz": high_hz, "notch_hz": notch_hz},
        "ica_fit_fingerprint": None,
        "ica_cache_hit": False,
        "can_apply_to_continuous": False,
        "start_sample": start_sample,
        "end_sample": end_sample,
        "start_s": round(start_sample / sfreq, 6),
        "end_s": round(end_sample / sfreq, 6),
    })
    return report
