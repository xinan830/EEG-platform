"""Tests for Step 2 offline artifact-segment quality control (amplitude PTP, MNE muscle z-score, manual)."""

from __future__ import annotations

from pathlib import Path

import numpy as np
import pytest

from app.scientific.artifact_segments import (
    apply_artifact_rejection_mask,
    detect_artifact_segments,
    overlapping_artifact_segments,
)
from app.scientific.contracts.preprocessing import (
    ArtifactRejectionSpec,
    BadChannelSpec,
    ManualArtifactSegment,
    OfflinePreprocessingSpec,
    ScientificFilterSpec,
)
from app.scientific.quality import SpectralQualityGateError
from test_offline_bad_channels import _write_multi_channel_edf


def test_detect_artifact_segments_flags_amplitude_burst_muscle_emg_and_manual_intervals():
    sfreq = 500.0
    t = np.arange(0, 12.0, 1.0 / sfreq)
    rng = np.random.default_rng(11)
    channels = ("Fp1", "Fp2", "F3", "F4")
    data = rng.normal(0.0, 12e-6, size=(t.size, len(channels)))
    data += (15e-6 * np.sin(2 * np.pi * 10.0 * t))[:, None]

    # 1) Giant 800 uV amplitude burst on F3 at 2.0s..2.6s (samples 1000..1300)
    data[1000:1300, 2] += 450e-6 * np.sin(2 * np.pi * 6.0 * t[1000:1300])
    # 2) Strong broadband/high-frequency muscle burst across channels at 6.0s..7.0s (samples 3000..3500)
    data[3000:3500, :] += rng.normal(0.0, 90e-6, size=(500, len(channels)))

    spec = ArtifactRejectionSpec(
        policy="annotate",
        peak_to_peak_uv=300.0,
        amplitude_window_s=0.5,
        muscle_zscore_threshold=3.5,
        min_duration_s=0.1,
        pad_s=0.1,
        manual_segments=(ManualArtifactSegment(start_sample=4500, end_sample=4800, reason="manual_blink", channels=("Fp1",)),),
    )
    report = detect_artifact_segments(data, sfreq, channels, spec)

    assert report.muscle_check_status == "mne_standard_110_140_hz"
    assert report.muscle_band_hz == (110.0, 140.0)
    reasons = {seg.reason for seg in report.segments}
    assert {"amplitude_ptp", "muscle_zscore", "manual_blink"} <= reasons
    assert 0.5 < report.clean_ratio < 0.95

    # Verify amplitude burst overlaps 2.0s..2.6s and muscle burst overlaps 6.0s..7.0s.
    assert any(seg.reason == "amplitude_ptp" and seg.start_sample <= 1050 and seg.end_sample >= 1250 for seg in report.segments)
    assert any(seg.reason == "muscle_zscore" and seg.start_sample <= 3100 and seg.end_sample >= 3400 for seg in report.segments)
    assert any(seg.reason == "manual_blink" and seg.start_sample == 4500 and seg.end_sample == 4800 for seg in report.segments)

    # Masking sets only affected intervals/channels to NaN and leaves clean regions finite.
    masked = apply_artifact_rejection_mask(data, report.segments, channels)
    assert np.all(np.isnan(masked[4500:4800, 0]))
    assert np.all(np.isfinite(masked[4500:4800, 1]))  # manual_blink targeted only Fp1
    assert np.all(np.isfinite(masked[100:500, :]))


def test_muscle_detection_adapts_safely_to_200hz_100hz_and_nan_gaps():
    rng = np.random.default_rng(22)
    channels = ("F3", "F4", "C3", "C4")

    # 200 Hz -> adapted 60-95 Hz band, with a NaN discontinuity in the middle.
    data_200 = rng.normal(0.0, 10e-6, size=(2000, len(channels)))
    data_200[900:950, 0] = np.nan
    spec = ArtifactRejectionSpec(policy="annotate", peak_to_peak_uv=None, muscle_zscore_threshold=4.0)
    report_200 = detect_artifact_segments(data_200, 200.0, channels, spec)
    assert report_200.muscle_check_status == "adapted_high_freq_band"
    assert report_200.muscle_band_hz == (60.0, 95.0)

    # 100 Hz -> skips muscle check safely without raising.
    data_100 = rng.normal(0.0, 10e-6, size=(1000, len(channels)))
    report_100 = detect_artifact_segments(data_100, 100.0, channels, spec)
    assert report_100.muscle_check_status == "skipped_low_sampling_rate"
    assert report_100.muscle_band_hz is None
    assert report_100.segments == ()


def test_offline_preprocessing_continuous_and_bounded_window_reject_artifacts_with_cache_isolation(tmp_path: Path):
    from test_recording_import import _client

    client, recordings, _ = _client(tmp_path)
    labels = ("Fp1", "Fp2", "F3", "F4")
    sfreq = 500
    t = np.arange(0, 12.0, 1.0 / sfreq)
    rng = np.random.default_rng(77)
    samples_uv = rng.normal(0.0, 10.0, size=(t.size, len(labels))) + (15.0 * np.sin(2 * np.pi * 10.0 * t))[:, None]
    # Clean region: 0..4s (0..2000); Artifact burst: 5.0..5.6s (2500..2800); Muscle burst: 8.5..9.3s (4250..4650)
    samples_uv[2500:2800, 2] += 550.0 * np.sin(2 * np.pi * 8.0 * t[2500:2800])
    samples_uv[4250:4650, :] += rng.normal(0.0, 95.0, size=(400, len(labels)))

    source = _write_multi_channel_edf(tmp_path / "artifacts.edf", labels, samples_uv, sfreq=sfreq)
    uploaded = client.post("/api/recordings/import", files={"file": ("artifacts.edf", source)})
    assert uploaded.status_code == 201, uploaded.text
    recording_id = uploaded.json()["id"]
    recording = recordings.require_recording(recording_id)

    # 1. REST inspection endpoint detects both amplitude and muscle segments.
    inspected = client.get(
        f"/api/recordings/{recording_id}/preprocessing/artifact-segments",
        params={"peak_to_peak_uv": 300.0, "muscle_zscore_threshold": 3.5},
    )
    assert inspected.status_code == 200, inspected.text
    body = inspected.json()
    assert body["signal_mode"] == "raw_unprojected"
    assert {"amplitude_ptp", "muscle_zscore"} <= {seg["reason"] for seg in body["segments"]}
    assert body["clean_ratio"] < 0.95

    # 2. Continuous preprocessing: none vs annotate vs reject stay isolated in cache.
    preproc = recordings.offline_scientific_preprocessing
    art_annotate = ArtifactRejectionSpec(policy="annotate", peak_to_peak_uv=300.0, muscle_zscore_threshold=3.5)
    art_reject = ArtifactRejectionSpec(policy="reject", peak_to_peak_uv=300.0, muscle_zscore_threshold=3.5)

    cont_none = preproc.prepare_continuous(recording, low_hz=1.0, high_hz=45.0, notch_hz=50.0)
    cont_ann = preproc.prepare_continuous(
        recording, low_hz=1.0, high_hz=45.0, notch_hz=50.0, artifact_rejection_spec=art_annotate,
    )
    cont_rej = preproc.prepare_continuous(
        recording, low_hz=1.0, high_hz=45.0, notch_hz=50.0, artifact_rejection_spec=art_reject,
    )

    assert cont_none.artifact_intervals == ()
    assert {"amplitude_ptp", "muscle_zscore"} <= {seg.reason for seg in cont_ann.artifact_intervals}
    assert np.all(np.isfinite(cont_ann.data[2500:2800, 2]))  # annotate does not mask
    assert {"amplitude_ptp", "muscle_zscore"} <= {seg.reason for seg in cont_rej.artifact_intervals}
    assert np.all(np.isnan(cont_rej.data[2550:2750, 2]))     # reject masks affected amplitude burst as NaN
    assert np.all(np.isnan(cont_rej.data[4350:4550, :]))     # reject masks broadband EMG burst even under 45 Hz LP
    assert np.all(np.isfinite(cont_rej.data[200:1200, :]))   # clean span stays finite

    # 3. Bounded window preprocessing: dirty window rejected with SpectralQualityGateError; clean window passes.
    f_win = ScientificFilterSpec(low_hz=1.0, high_hz=45.0, notch_hz=50.0, scope="bounded_window_with_finite_context")
    spec_reject = OfflinePreprocessingSpec(f_win, BadChannelSpec(), art_reject).validate(float(sfreq))
    spec_annotate = OfflinePreprocessingSpec(f_win, BadChannelSpec(), art_annotate).validate(float(sfreq))

    with recordings.open_access_session(recording) as session:
        clean_window = preproc.prepare_bounded_window(
            session, start_sample=500, end_sample=1500, channels=["F3"],
            low_hz=1.0, high_hz=45.0, notch_hz=50.0, context_s=0.5, algorithm_version="stft-v1",
            preprocessing_spec=spec_reject,
        )
        assert clean_window.evidence["artifact_rejection"]["clean_ratio"] == 1.0

        with pytest.raises(SpectralQualityGateError) as exc_info:
            preproc.prepare_bounded_window(
                session, start_sample=2200, end_sample=3200, channels=["F3"],
                low_hz=1.0, high_hz=45.0, notch_hz=50.0, context_s=0.5, algorithm_version="stft-v1",
                preprocessing_spec=spec_reject,
            )
        detail = exc_info.value.quality
        assert detail["gate_failed"] == "artifact_segment_rejected"
        assert "amplitude_ptp" in detail["rejected_reasons"]
        assert detail["evidence"]["artifact_intervals"]

        annotated_dirty = preproc.prepare_bounded_window(
            session, start_sample=2200, end_sample=3200, channels=["F3"],
            low_hz=1.0, high_hz=45.0, notch_hz=50.0, context_s=0.5, algorithm_version="stft-v1",
            preprocessing_spec=spec_annotate,
        )
        assert annotated_dirty.evidence["artifact_rejection"]["segments"]
        assert len(overlapping_artifact_segments(cont_ann.artifact_intervals, 2200, 3200, ("F3",))) >= 1

        # 4. Item #5: Bounded window detects 110-140 Hz muscle burst even under 1-30 Hz analysis low-pass!
        f_narrow = ScientificFilterSpec(low_hz=1.0, high_hz=30.0, notch_hz=50.0, scope="bounded_window_with_finite_context")
        art_muscle_only = ArtifactRejectionSpec(policy="reject", peak_to_peak_uv=None, muscle_zscore_threshold=3.5)
        spec_muscle_reject = OfflinePreprocessingSpec(f_narrow, BadChannelSpec(), art_muscle_only).validate(float(sfreq))
        with pytest.raises(SpectralQualityGateError) as muscle_exc:
            preproc.prepare_bounded_window(
                session, start_sample=3000, end_sample=5500, channels=["Fp1", "Fp2", "F3", "F4"],
                low_hz=1.0, high_hz=30.0, notch_hz=50.0, context_s=0.5, algorithm_version="stft-v1",
                preprocessing_spec=spec_muscle_reject,
            )
        assert "muscle_zscore" in muscle_exc.value.quality["rejected_reasons"]
        assert muscle_exc.value.quality["evidence"]["artifact_rejection"]["qc_inputs"]["muscle_source"] == "broadband_source"


def test_manual_artifact_unknown_channel_raises_and_narrow_band_ptp_preserves_burst_detection(tmp_path: Path):
    from test_recording_import import _client

    # Item #4: ManualArtifactSegment referencing an unknown channel MUST fail explicitly (never silent no-op).
    sfreq = 250.0
    t = np.arange(0, 8.0, 1.0 / sfreq)
    channels = ("Fp1", "Fp2", "F3", "F4")
    rng = np.random.default_rng(404)
    data = rng.normal(0.0, 10e-6, size=(t.size, len(channels)))

    bad_manual = ArtifactRejectionSpec(
        policy="reject",
        peak_to_peak_uv=None,
        muscle_zscore_threshold=None,
        manual_segments=(ManualArtifactSegment(start_sample=100, end_sample=300, reason="manual", channels=("NON_EXISTENT",)),),
    )
    with pytest.raises(ValueError, match="ARTIFACT_SEGMENT_CHANNEL_NOT_FOUND"):
        detect_artifact_segments(data, sfreq, channels, bad_manual)

    # Item #6: A sharp 22 Hz 500 uV burst is still caught by PTP even when analysis low-pass is narrow (0.5-8 Hz)!
    burst = data.copy()
    burst[700:850, 2] += 260e-6 * np.sin(2.0 * np.pi * 22.0 * t[700:850])
    client, recordings, _ = _client(tmp_path)
    source = _write_multi_channel_edf(tmp_path / "narrow_burst.edf", channels, burst * 1e6, sfreq=int(sfreq))
    rec_id = client.post("/api/recordings/import", files={"file": ("narrow_burst.edf", source)}).json()["id"]
    recording = recordings.require_recording(rec_id)
    preproc = recordings.offline_scientific_preprocessing

    art_ptp = ArtifactRejectionSpec(policy="reject", peak_to_peak_uv=300.0, muscle_zscore_threshold=None)
    cont_narrow = preproc.prepare_continuous(
        recording, low_hz=0.5, high_hz=8.0, notch_hz=None, artifact_rejection_spec=art_ptp,
    )
    assert any(seg.reason == "amplitude_ptp" for seg in cont_narrow.artifact_intervals)
    assert np.all(np.isnan(cont_narrow.data[740:810, 2]))

    with recordings.open_access_session(recording) as session:
        f_narrow = ScientificFilterSpec(low_hz=0.5, high_hz=8.0, notch_hz=None, scope="bounded_window_with_finite_context")
        spec_narrow = OfflinePreprocessingSpec(f_narrow, BadChannelSpec(), art_ptp).validate(sfreq)
        with pytest.raises(SpectralQualityGateError) as narrow_exc:
            preproc.prepare_bounded_window(
                session, start_sample=400, end_sample=1400, channels=["F3"],
                low_hz=0.5, high_hz=8.0, notch_hz=None, context_s=0.5, algorithm_version="tnk-v1",
                preprocessing_spec=spec_narrow,
            )
        assert "amplitude_ptp" in narrow_exc.value.quality["rejected_reasons"]
        assert narrow_exc.value.quality["evidence"]["artifact_rejection"]["qc_inputs"]["ptp_source"] == "wideband_detrended_0.5_45hz"
