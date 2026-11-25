"""Bounded offline artifact-segment and ICA inspection service for review and API endpoints."""

from __future__ import annotations

import numpy as np
from scipy import signal

from app.scientific.artifact_segments import detect_artifact_segments
from app.scientific.contracts.preprocessing import ArtifactRejectionSpec, BadChannelSpec


def detrend_and_filter_for_inspection(
    values_v: np.ndarray,
    sfreq_hz: float,
    *,
    low_hz: float | None = 0.5,
    high_hz: float | None = 45.0,
    notch_hz: float | None = 50.0,
) -> tuple[np.ndarray, np.ndarray]:
    """Return (bandpassed_for_eeg_v, broadband_notched_for_emg_v) preserving NaN gaps."""
    data = np.asarray(values_v, dtype=np.float64)
    nyquist = sfreq_hz / 2.0
    if data.shape[0] < 32 or nyquist <= 2.0:
        return data.copy(), data.copy()

    def _apply_columnwise(arr: np.ndarray, fn) -> np.ndarray:
        out = arr.copy()
        for ch in range(arr.shape[1]):
            col = arr[:, ch]
            mask = np.isfinite(col)
            if not np.any(mask):
                continue
            padded = np.concatenate(([False], mask, [False]))
            diff = np.diff(padded.astype(np.int8))
            for s, e in zip(np.flatnonzero(diff == 1), np.flatnonzero(diff == -1), strict=True):
                if int(e - s) >= 32:
                    try:
                        out[int(s):int(e), ch] = fn(col[int(s):int(e)])
                    except ValueError:
                        pass
        return out

    broadband = data
    if low_hz is not None and 0.0 < low_hz < nyquist:
        sos_hp = signal.butter(4, low_hz, btype="highpass", fs=sfreq_hz, output="sos")
        broadband = _apply_columnwise(broadband, lambda x: signal.sosfiltfilt(sos_hp, x))
    if notch_hz is not None and 1.0 < notch_hz < nyquist - 1.0:
        b_n, a_n = signal.iirnotch(notch_hz, 30.0, fs=sfreq_hz)
        broadband = _apply_columnwise(broadband, lambda x: signal.filtfilt(b_n, a_n, x))

    eeg_band = broadband
    if high_hz is not None and (low_hz or 0.0) < high_hz < nyquist - 1.0:
        sos_lp = signal.butter(4, high_hz, btype="lowpass", fs=sfreq_hz, output="sos")
        eeg_band = _apply_columnwise(eeg_band, lambda x: signal.sosfiltfilt(sos_lp, x))
    return eeg_band, broadband


def inspect_recording_artifact_segments(
    recordings,
    recording,
    *,
    start_s: float = 0.0,
    duration_s: float = 60.0,
    montage_id: str | None = None,
    bad_channel_spec: BadChannelSpec | None = None,
    artifact_spec: ArtifactRejectionSpec | None = None,
    low_hz: float | None = 0.5,
    high_hz: float | None = 45.0,
    notch_hz: float | None = 50.0,
    max_cells: int = 4_000_000,
) -> dict[str, object]:
    """Screen a bounded recording span for amplitude-PTP and MNE muscle z-score artifacts."""
    spec = (artifact_spec or ArtifactRejectionSpec(policy="annotate")).normalized()
    if not spec.is_active:
        spec = ArtifactRejectionSpec(
            policy="annotate",
            peak_to_peak_uv=spec.peak_to_peak_uv,
            amplitude_window_s=spec.amplitude_window_s,
            muscle_zscore_threshold=spec.muscle_zscore_threshold,
            min_duration_s=spec.min_duration_s,
            pad_s=spec.pad_s,
            manual_segments=spec.manual_segments,
        ).normalized()

    target_recording = recording
    raw_unprojected = montage_id is None
    if montage_id is not None:
        target_recording = recordings.select_analysis_montage(recording, montage_id)

    norm_bad = bad_channel_spec.normalized() if bad_channel_spec is not None else None
    with recordings.open_access_session(
        target_recording,
        bad_channel_spec=norm_bad if (norm_bad is not None and norm_bad.is_active) else None,
        raw_unprojected=raw_unprojected,
    ) as session:
        sfreq = float(session.sampling_rate_hz)
        total = int(session.sample_count)
        start_sample = max(0, min(total, int(round(float(start_s) * sfreq))))
        requested_samples = max(1, int(round(float(duration_s) * sfreq)))
        max_samples = max(1, max_cells // max(1, len(session.channel_names)))
        end_sample = min(total, start_sample + min(requested_samples, max_samples))
        if end_sample <= start_sample:
            raise ValueError("坏段检测时间窗超出录音有效范围")
        block = session.read_samples(start_sample, end_sample)
        eeg_band, broadband = detrend_and_filter_for_inspection(
            block.values_v, sfreq, low_hz=low_hz, high_hz=high_hz, notch_hz=notch_hz,
        )
        report = detect_artifact_segments(
            eeg_band,
            sfreq,
            block.channel_names,
            spec,
            start_sample_offset=start_sample,
            muscle_values_v=broadband,
        )

    payload = report.to_dict()
    payload.update({
        "recording_id": recording.id,
        "montage_id": montage_id,
        "signal_mode": "raw_unprojected" if raw_unprojected else "montage_projected",
        "channels": list(block.channel_names),
        "bad_channels": norm_bad.to_evidence() if norm_bad is not None else BadChannelSpec().to_evidence(),
        "spec": spec.to_evidence(),
        "inspection_filter": {"low_hz": low_hz, "high_hz": high_hz, "notch_hz": notch_hz},
        "start_s": round(start_sample / sfreq, 6),
        "end_s": round(end_sample / sfreq, 6),
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
    from app.scientific.artifact_removal import fit_ica_model, inspect_ica_components
    from app.scientific.contracts.artifact_removal import ArtifactRemovalSpec
    from app.services.ica_review_cache import compute_ica_fit_fingerprint

    target_recording = recording
    if montage_id is not None:
        target_recording = recordings.select_analysis_montage(recording, montage_id)

    norm_bad = bad_channel_spec.normalized() if bad_channel_spec is not None else BadChannelSpec()
    norm_art = artifact_rejection_spec.normalized() if artifact_rejection_spec is not None else ArtifactRejectionSpec()
    spec = (removal_spec or ArtifactRemovalSpec(method="ica_infomax")).normalized()

    # Full-recording review uses the exact same continuous pre-ICA matrix as mapped_preprocess
    # and caches the fitted ICA model under a verified fingerprint so user-confirmed ICs never drift.
    if duration_s is None and float(start_s) == 0.0:
        with recordings.open_access_session(
            target_recording,
            bad_channel_spec=norm_bad if norm_bad.is_active else None,
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
        ica_cache = getattr(recordings, "ica_review_cache", None)
        cached = ica_cache.get(fit_fp) if ica_cache is not None else None
        pre_ica = recordings.offline_scientific_preprocessing.prepare_continuous(
            target_recording,
            low_hz=low_hz,
            high_hz=high_hz,
            notch_hz=notch_hz,
            bad_channel_spec=norm_bad if norm_bad.is_active else None,
            artifact_rejection_spec=norm_art if norm_art.is_active else None,
        )
        if cached is not None:
            fitted = cached
            cache_hit = True
        else:
            fitted = fit_ica_model(pre_ica.data, pre_ica.sfreq, pre_ica.channel_names, spec)
            if ica_cache is not None:
                ica_cache.put(fit_fp, fitted, recording_id=recording.id)
            cache_hit = False
        report = fitted.to_inspection_dict(spec.ica_exclude_components)
        from app.services.preprocessing_preview import attach_ica_component_visuals
        attach_ica_component_visuals(report, fitted, pre_ica.data)
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
