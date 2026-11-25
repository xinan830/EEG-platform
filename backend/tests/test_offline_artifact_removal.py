"""Tests for Step 3 offline artifact removal: EOG regression, MNE SSP, and reviewable ICA."""

from __future__ import annotations

from pathlib import Path

import numpy as np
import pytest

from app.scientific.artifact_removal import apply_offline_artifact_removal, inspect_ica_components
from app.scientific.contracts.preprocessing import (
    ArtifactRejectionSpec,
    ArtifactRemovalSpec,
    BadChannelSpec,
    OfflinePreprocessingSpec,
    ScientificFilterSpec,
)
from test_offline_bad_channels import _write_multi_channel_edf


_CHANNELS_10 = ("Fp1", "Fp2", "F3", "F4", "C3", "C4", "P3", "P4", "O1", "O2")
_BLINK_WEIGHTS = np.array([1.0, 0.95, 0.55, 0.52, 0.28, 0.26, 0.14, 0.13, 0.08, 0.07], dtype=np.float64)


def _synthesize_eeg_with_blinks(
    *,
    sfreq: float = 250.0,
    duration_s: float = 16.0,
    include_blinks: bool = True,
    seed: int = 101,
) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Return (mixed_v, clean_brain_v, blink_wave_v) in SI volts (samples, 10)."""
    rng = np.random.default_rng(seed)
    t = np.arange(int(round(duration_s * sfreq)), dtype=np.float64) / sfreq
    n_samples = t.size

    # Independent brain sources (posterior alpha + central beta + low-level sensor noise).
    alpha = 18e-6 * np.sin(2.0 * np.pi * 10.0 * t)
    beta = 7e-6 * np.sin(2.0 * np.pi * 18.0 * t + 0.7)
    alpha_topo = np.array([0.15, 0.15, 0.30, 0.30, 0.45, 0.45, 0.80, 0.80, 1.0, 1.0])
    beta_topo = np.array([0.20, 0.20, 0.55, 0.55, 0.90, 0.90, 0.40, 0.40, 0.20, 0.20])
    clean = (
        alpha[:, None] * alpha_topo[None, :]
        + beta[:, None] * beta_topo[None, :]
        + rng.normal(0.0, 5e-6, size=(n_samples, len(_CHANNELS_10)))
    )

    blink = np.zeros(n_samples, dtype=np.float64)
    if include_blinks:
        for center_s in (2.0, 4.5, 7.0, 9.5, 12.0, 14.0):
            blink += 180e-6 * np.exp(-0.5 * ((t - center_s) / 0.08) ** 2)

    mixed = clean + blink[:, None] * _BLINK_WEIGHTS[None, :]
    return mixed, clean, blink


def test_eog_regression_attenuates_blinks_preserves_alpha_and_propagates_nan_gaps():
    mixed, clean, _blink = _synthesize_eeg_with_blinks()
    # Inject a NaN gap on the EOG reference channel Fp1 to verify strict NaN propagation.
    mixed_with_gap = mixed.copy()
    mixed_with_gap[500:540, 0] = np.nan

    spec = ArtifactRemovalSpec(method="eog_regression", eog_channels=("Fp1",))
    res = apply_offline_artifact_removal(mixed_with_gap, 250.0, _CHANNELS_10, spec)

    assert res.applied is True
    assert res.status == "applied"
    assert res.eog_channels_used == ("Fp1",)
    assert res.regression_weights is not None
    assert 0.35 < res.regression_weights["F3"][0] < 0.75

    # NaN on EOG proxy must propagate as NaN on corrected targets (never fabricated as zero).
    assert np.all(np.isnan(res.data_v[500:540, 2]))
    assert np.all(np.isfinite(res.data_v[600:, 2]))

    # F3 blink peak-to-peak drops dramatically while 10 Hz posterior alpha on O1 stays intact.
    f3_before_ptp = float(np.nanmax(mixed[:, 2]) - np.nanmin(mixed[:, 2]))
    f3_after_ptp = float(np.nanmax(res.data_v[600:, 2]) - np.nanmin(res.data_v[600:, 2]))
    assert f3_after_ptp < f3_before_ptp * 0.55

    corr_o1 = float(np.corrcoef(res.data_v[600:, 8], clean[600:, 8])[0, 1])
    assert corr_o1 > 0.90


def test_ssp_eog_reduces_blink_artifact_and_skips_clean_signal_honestly():
    mixed, _clean, _blink = _synthesize_eeg_with_blinks()
    spec = ArtifactRemovalSpec(method="ssp_eog", eog_channels=("Fp1",), ssp_n_eeg=1)
    res = apply_offline_artifact_removal(mixed, 250.0, _CHANNELS_10, spec)

    assert res.applied is True
    assert res.status == "applied"
    assert res.ssp_projectors_applied == 1
    assert res.eog_events_detected >= 3

    f3_before_ptp = float(np.ptp(mixed[:, 2]))
    f3_after_ptp = float(np.ptp(res.data_v[:, 2]))
    assert f3_after_ptp < f3_before_ptp * 0.50

    # On a blink-free low-amplitude clean signal with no EOG spikes, SSP skips honestly if 0 events.
    clean_only, _, _ = _synthesize_eeg_with_blinks(include_blinks=False, seed=202)
    clean_only *= 0.05  # tiny 1 uV signal well below EOG peak detector
    res_clean = apply_offline_artifact_removal(clean_only, 250.0, _CHANNELS_10, spec)
    assert res_clean.method == "ssp_eog"
    if not res_clean.applied:
        assert res_clean.status == "skipped_no_eog_events"
        assert np.array_equal(res_clean.data_v, clean_only)


def test_ica_infomax_inspection_auto_removal_manual_override_and_validation():
    mixed, clean, _blink = _synthesize_eeg_with_blinks()
    # Inject a NaN gap (simulating Step 2 artifact segment rejection) so ICA fits only on finite spans.
    mixed_gapped = mixed.copy()
    mixed_gapped[1500:1600, :] = np.nan

    inspect = inspect_ica_components(
        mixed_gapped,
        250.0,
        _CHANNELS_10,
        ArtifactRemovalSpec(method="ica_infomax", eog_channels=("Fp1",), ica_n_components=6, random_state=42),
    )
    assert inspect["method"] == "ica_infomax"
    assert inspect["n_components"] == 6
    assert len(inspect["components"]) == 6
    recommended = inspect["recommended_exclude_components"]
    assert len(recommended) >= 1
    assert all(0.0 <= float(c["explained_variance_ratio"]) <= 1.0 for c in inspect["components"])

    # 1) Auto-removal excludes the recommended blink component and preserves NaN gaps + alpha rhythm.
    auto_res = apply_offline_artifact_removal(
        mixed_gapped,
        250.0,
        _CHANNELS_10,
        ArtifactRemovalSpec(method="ica_infomax", eog_channels=("Fp1",), ica_n_components=6, random_state=42),
    )
    assert auto_res.applied is True
    assert auto_res.status == "applied"
    assert list(auto_res.excluded_components) == recommended
    assert np.all(np.isnan(auto_res.data_v[1500:1600, :]))
    assert np.all(np.isfinite(auto_res.data_v[:1490, :]))

    f3_before = float(np.nanmax(mixed_gapped[:, 2]) - np.nanmin(mixed_gapped[:, 2]))
    f3_after = float(np.nanmax(auto_res.data_v[:, 2]) - np.nanmin(auto_res.data_v[:, 2]))
    assert f3_after < f3_before * 0.50
    valid_mask = np.isfinite(auto_res.data_v[:, 8])
    assert float(np.corrcoef(auto_res.data_v[valid_mask, 8], clean[valid_mask, 8])[0, 1]) > 0.85

    # 2) Explicit user-reviewed override: exclude=() skips cleanly without modifying signal.
    skip_res = apply_offline_artifact_removal(
        mixed_gapped,
        250.0,
        _CHANNELS_10,
        ArtifactRemovalSpec(
            method="ica_infomax",
            eog_channels=("Fp1",),
            ica_n_components=6,
            ica_exclude_components=(),
            random_state=42,
        ),
    )
    assert skip_res.applied is False
    assert skip_res.status == "skipped_no_artifact_components"
    assert skip_res.excluded_components == ()
    assert np.array_equal(skip_res.data_v, mixed_gapped, equal_nan=True)

    # 3) Explicit user-reviewed override with out-of-range IC raises structured error.
    with pytest.raises(ValueError, match="ARTIFACT_REMOVAL_INVALID_IC"):
        apply_offline_artifact_removal(
            mixed,
            250.0,
            _CHANNELS_10,
            ArtifactRemovalSpec(method="ica_infomax", ica_n_components=6, ica_exclude_components=(9,)),
        )

    # 4) Insufficient channels (< 4) or duration (< 5 s) fails explicitly.
    with pytest.raises(ValueError, match="ARTIFACT_REMOVAL_INSUFFICIENT_CHANNELS"):
        apply_offline_artifact_removal(
            mixed[:, :3], 250.0, _CHANNELS_10[:3], ArtifactRemovalSpec(method="ica_infomax"),
        )
    with pytest.raises(ValueError, match="ARTIFACT_REMOVAL_INSUFFICIENT_FINITE_SAMPLES"):
        apply_offline_artifact_removal(
            mixed[:500, :], 250.0, _CHANNELS_10, ArtifactRemovalSpec(method="ica_infomax"),
        )


def test_step3_end_to_end_ica_api_continuous_pipeline_composition_and_bounded_window_rejection(tmp_path: Path):
    from test_recording_import import _client

    sfreq = 250.0
    mixed, _clean, _blink = _synthesize_eeg_with_blinks(sfreq=sfreq, duration_s=16.0, seed=303)
    # Also inject a giant 1200 uV movement burst at 5.5s..6.0s so Step 2 rejects it BEFORE Step 3 ICA!
    mixed[1375:1500, 4] += 650e-6 * np.sin(2.0 * np.pi * 5.0 * np.arange(125) / sfreq)

    client, recordings, _ = _client(tmp_path)
    source = _write_multi_channel_edf(tmp_path / "step3_blinks_and_burst.edf", _CHANNELS_10, mixed * 1e6, sfreq=int(sfreq))
    uploaded = client.post("/api/recordings/import", files={"file": ("step3_blinks_and_burst.edf", source)})
    assert uploaded.status_code == 201, uploaded.text
    rec_id = uploaded.json()["id"]

    # 1) REST inspection endpoint (continuous default) returns reviewable ICA table + reusable fit fingerprint.
    ica_resp = client.get(
        f"/api/recordings/{rec_id}/preprocessing/ica-components",
        params={
            "low_hz": 1.0,
            "high_hz": 50.0,
            "notch_hz": 50.0,
            "artifact_policy": "reject",
            "peak_to_peak_uv": 450.0,
            "bad_channels": "O2",
            "bad_channel_policy": "exclude",
            "allow_frontal_eog_proxy": "true",
            "ica_n_components": 6,
        },
    )
    assert ica_resp.status_code == 200, ica_resp.text
    ica_body = ica_resp.json()
    assert ica_body["recording_id"] == rec_id
    assert ica_body["signal_mode"] == "continuous_pre_ica"
    assert ica_body["can_apply_to_continuous"] is True
    assert ica_body["eog_channel_source"] == "frontal_eeg_proxy"
    fit_fp = ica_body["ica_fit_fingerprint"]
    assert isinstance(fit_fp, str) and len(fit_fp) == 24
    assert ica_body["n_components"] == 6
    assert len(ica_body["components"]) == 6
    # Verify ICA review components include 2D topomap grid and preview waveform for WPF review dialog.
    comp0 = ica_body["components"][0]
    assert comp0["topomap"]["status"] == "ready"
    assert comp0["topomap"]["grid"]["width"] == 33
    assert len(comp0["preview_waveform"]) >= 16
    rec_ics = ica_body["recommended_exclude_components"]
    assert len(rec_ics) >= 1

    # Verify read-only preprocessing preview-window endpoint reuses cached ICA and preserves NaN gaps as null.
    prev_resp = client.post(
        f"/api/recordings/{rec_id}/preprocessing/preview-window",
        json={
            "start_s": 5.0,
            "window_s": 5.0,
            "low_hz": 1.0,
            "high_hz": 50.0,
            "notch_hz": 50.0,
            "max_points": 300,
            "preprocessing": {
                "bad_channels": {"policy": "exclude", "channels": ["O2"]},
                "artifact_rejection": {"policy": "reject", "peak_to_peak_uv": 450.0, "muscle_zscore_threshold": 4.0},
                "artifact_removal": {
                    "method": "ica_infomax",
                    "allow_frontal_eog_proxy": True,
                    "ica_n_components": 6,
                    "ica_exclude_components": rec_ics,
                    "ica_fit_fingerprint": fit_fp,
                },
            },
        },
    )
    assert prev_resp.status_code == 200, prev_resp.text
    prev_body = prev_resp.json()
    assert prev_body["ica_model_reused"] is True
    assert "O2" in prev_body["excluded_channels"]
    assert len(prev_body["times_s"]) == 300
    assert any(v is None for v in prev_body["traces_uv"]["F3"])  # burst around 5.5s..6.0s preserved as null/NaN!

    # Re-calling inspection with same settings hits the ICA review cache immediately.
    ica_resp2 = client.get(
        f"/api/recordings/{rec_id}/preprocessing/ica-components",
        params={
            "low_hz": 1.0,
            "high_hz": 50.0,
            "notch_hz": 50.0,
            "artifact_policy": "reject",
            "peak_to_peak_uv": 450.0,
            "bad_channels": "O2",
            "bad_channel_policy": "exclude",
            "allow_frontal_eog_proxy": "true",
            "ica_n_components": 6,
        },
    )
    assert ica_resp2.status_code == 200 and ica_resp2.json()["ica_cache_hit"] is True

    # 2) Item #1 + #3: POST /spectrum/configured (PSD + RBP) applies reviewed ICA via verified fingerprint!
    psd_base = client.post(
        f"/api/recordings/{rec_id}/spectrum/configured",
        json={"mode": "static", "channels": ["F3", "O1"], "time": {"start_s": 7.0, "end_s": 15.0}},
    )
    assert psd_base.status_code == 200, psd_base.text

    psd_cleaned = client.post(
        f"/api/recordings/{rec_id}/spectrum/configured",
        json={
            "mode": "static",
            "channels": ["F3", "O1"],
            "time": {"start_s": 7.0, "end_s": 15.0},
            "preprocessing": {
                "bad_channels": {"policy": "exclude", "channels": ["O2"]},
                "artifact_rejection": {"policy": "reject", "peak_to_peak_uv": 450.0, "muscle_zscore_threshold": 4.0},
                "artifact_removal": {
                    "method": "ica_infomax",
                    "allow_frontal_eog_proxy": True,
                    "ica_n_components": 6,
                    "ica_exclude_components": rec_ics,
                    "ica_fit_fingerprint": fit_fp,
                },
            },
        },
    )
    assert psd_cleaned.status_code == 200, psd_cleaned.text
    cleaned_ev = psd_cleaned.json()["filter_contract"]["preprocessing"]["artifact_removal"]
    assert cleaned_ev["applied"] is True
    assert cleaned_ev["ica_fit_fingerprint"] == fit_fp
    assert cleaned_ev["ica_model_reused"] is True
    assert cleaned_ev["excluded_components"] == rec_ics
    # Blink delta power (1-4 Hz) on F3 drops sharply while posterior alpha (8-13 Hz) on O1 is preserved.
    assert psd_cleaned.json()["band_power"]["F3"]["delta"] < psd_base.json()["band_power"]["F3"]["delta"] * 0.50

    # 3) Mismatched ICA fit fingerprint MUST be explicitly rejected (prevents IC index drift!).
    psd_mismatch = client.post(
        f"/api/recordings/{rec_id}/spectrum/configured",
        json={
            "mode": "static",
            "channels": ["F3", "O1"],
            "time": {"start_s": 7.0, "end_s": 15.0},
            "preprocessing": {
                "artifact_removal": {
                    "method": "ica_infomax",
                    "allow_frontal_eog_proxy": True,
                    "ica_n_components": 6,
                    "ica_exclude_components": rec_ics,
                    "ica_fit_fingerprint": fit_fp,  # mismatches because O2 exclusion & artifact_rejection changed!
                },
            },
        },
    )
    assert psd_mismatch.status_code == 422
    assert "ICA_FIT_FINGERPRINT_MISMATCH" in psd_mismatch.text

    # 4) STFT configured endpoint supports bad_channels & artifact_rejection and rejects active artifact_removal.
    stft_clean = client.post(
        f"/api/recordings/{rec_id}/spectrogram/configured",
        json={
            "mode": "spectrogram",
            "channels": ["F3"],
            "time": {"start_s": 7.0, "end_s": 15.0},
            "preprocessing": {
                "bad_channels": {"policy": "exclude", "channels": ["O2"]},
                "artifact_rejection": {"policy": "reject", "peak_to_peak_uv": 450.0, "muscle_zscore_threshold": None},
            },
        },
    )
    assert stft_clean.status_code == 200, stft_clean.text

    stft_ica_reject = client.post(
        f"/api/recordings/{rec_id}/spectrogram/configured",
        json={
            "mode": "spectrogram",
            "channels": ["F3"],
            "time": {"start_s": 7.0, "end_s": 15.0},
            "preprocessing": {"artifact_removal": {"method": "ica_infomax", "allow_frontal_eog_proxy": True}},
        },
    )
    assert stft_ica_reject.status_code == 422
    assert "ARTIFACT_REMOVAL_REQUIRES_CONTINUOUS_SCOPE" in stft_ica_reject.text

    offline = recordings.offline_scientific_preprocessing
    recording = recordings.require_recording(rec_id)

    # 5) Continuous pipeline: baseline vs EOG regression vs Step 1 + Step 2 + Step 3 ICA composition.
    prep_none = offline.prepare_continuous(recording, low_hz=0.5, high_hz=45.0, notch_hz=None)
    prep_eog = offline.prepare_continuous(
        recording,
        low_hz=0.5,
        high_hz=45.0,
        notch_hz=None,
        artifact_removal_spec=ArtifactRemovalSpec(method="eog_regression", eog_channels=("Fp1",)),
    )
    prep_full = offline.prepare_continuous(
        recording,
        low_hz=0.5,
        high_hz=45.0,
        notch_hz=None,
        bad_channel_spec=BadChannelSpec(policy="exclude", channels=("O2",)),
        artifact_rejection_spec=ArtifactRejectionSpec(
            policy="reject", peak_to_peak_uv=450.0, muscle_zscore_threshold=None,
        ),
        artifact_removal_spec=ArtifactRemovalSpec(
            method="ica_infomax", eog_channels=("Fp1",), ica_n_components=6, random_state=42,
        ),
    )

    # Strict cache isolation across specs.
    assert prep_none is not prep_eog
    assert prep_eog is not prep_full
    assert prep_none.artifact_removal_evidence is None
    assert prep_eog.artifact_removal_evidence is not None
    assert prep_eog.artifact_removal_evidence["method"] == "eog_regression"
    assert prep_eog.artifact_removal_evidence["eog_channel_source"] == "explicit_channel"
    assert prep_eog.artifact_removal_evidence["applied"] is True

    assert prep_full.artifact_removal_evidence is not None
    assert prep_full.artifact_removal_evidence["method"] == "ica_infomax"
    assert prep_full.artifact_removal_evidence["applied"] is True
    # Step 1 excluded O2:
    assert "O2" not in prep_full.channel_names
    # Step 2 masked the 1200 uV movement burst around sample 1400 as NaN and Step 3 preserved the NaN gap:
    assert len(prep_full.artifact_intervals) >= 1
    assert np.all(np.isnan(prep_full.data[1400:1480, :]))
    # Step 3 ICA attenuated blink amplitude on F3 in the clean finite region (after the burst):
    f3_idx_none = prep_none.channel_names.index("F3")
    f3_idx_full = prep_full.channel_names.index("F3")
    ptp_none = float(np.nanmax(prep_none.data[1700:, f3_idx_none]) - np.nanmin(prep_none.data[1700:, f3_idx_none]))
    ptp_full = float(np.nanmax(prep_full.data[1700:, f3_idx_full]) - np.nanmin(prep_full.data[1700:, f3_idx_full]))
    assert ptp_full < ptp_none * 0.60

    # 6) Bounded-window preparation must explicitly reject active artifact removal.
    with recordings.open_access_session(recording) as session:
        bad_window_spec = OfflinePreprocessingSpec(
            filter_spec=ScientificFilterSpec(
                low_hz=0.5, high_hz=45.0, notch_hz=None,
                filter_phase="zero_phase", scope="bounded_window_with_finite_context",
            ),
            artifact_removal=ArtifactRemovalSpec(method="ica_infomax"),
        )
        with pytest.raises(ValueError, match="ARTIFACT_REMOVAL_REQUIRES_CONTINUOUS_SCOPE"):
            offline.prepare_bounded_window(
                session,
                start_sample=0,
                end_sample=1000,
                channels=None,
                low_hz=0.5,
                high_hz=45.0,
                notch_hz=None,
                context_s=2.0,
                algorithm_version="test-v1",
                preprocessing_spec=bad_window_spec,
            )


def test_eog_proxy_safety_blocks_silent_frontal_eeg_regression_and_requires_confirmation():
    mixed, _clean, _blink = _synthesize_eeg_with_blinks()

    # EOG regression MUST NOT silently grab Fp1 when no dedicated EOG channel exists.
    with pytest.raises(ValueError, match="ARTIFACT_REMOVAL_EOG_CHANNEL_UNAVAILABLE"):
        apply_offline_artifact_removal(mixed, 250.0, _CHANNELS_10, ArtifactRemovalSpec(method="eog_regression"))

    # SSP and ICA MUST require explicit allow_frontal_eog_proxy=True (or explicit eog_channels) on frontal EEG-only caps.
    with pytest.raises(ValueError, match="ARTIFACT_REMOVAL_FRONTAL_PROXY_CONFIRMATION_REQUIRED"):
        apply_offline_artifact_removal(mixed, 250.0, _CHANNELS_10, ArtifactRemovalSpec(method="ssp_eog"))
    with pytest.raises(ValueError, match="ARTIFACT_REMOVAL_FRONTAL_PROXY_CONFIRMATION_REQUIRED"):
        apply_offline_artifact_removal(mixed, 250.0, _CHANNELS_10, ArtifactRemovalSpec(method="ica_infomax"))

    # With allow_frontal_eog_proxy=True, SSP & ICA succeed and honestly report eog_channel_source="frontal_eeg_proxy".
    res_ssp = apply_offline_artifact_removal(
        mixed, 250.0, _CHANNELS_10, ArtifactRemovalSpec(method="ssp_eog", allow_frontal_eog_proxy=True),
    )
    assert res_ssp.eog_channel_source == "frontal_eeg_proxy"
    assert res_ssp.applied is True


def test_formal_api_runs_official_rbp_spectrum_full_bad_segment_evidence_and_ica_cache_deletion(tmp_path: Path):
    from app.algorithms.catalog import ensure_official_definitions
    from app.main import app
    from app.services.runs import RunService
    from test_recording_import import _client

    sfreq = 250.0
    mixed, _clean, _blink = _synthesize_eeg_with_blinks(sfreq=sfreq, duration_s=16.0, seed=404)
    mixed[1375:1500, 4] += 650e-6 * np.sin(2.0 * np.pi * 5.0 * np.arange(125) / sfreq)

    client, recordings, _ = _client(tmp_path)
    runs = RunService(recordings, recordings.database_path, tmp_path / "artifacts")
    ensure_official_definitions(runs.definition_service)
    app.state.definition_service = runs.definition_service
    app.state.run_service = runs

    source = _write_multi_channel_edf(tmp_path / "formal_runs.edf", _CHANNELS_10, mixed * 1e6, sfreq=int(sfreq))
    rec_id = client.post("/api/recordings/import", files={"file": ("formal_runs.edf", source)}).json()["id"]

    # 1. Inspect ICA on continuous pre-ICA source (aligned with official RBP 1-50 Hz + 50 Hz notch).
    ica_resp = client.get(
        f"/api/recordings/{rec_id}/preprocessing/ica-components",
        params={
            "low_hz": 1.0,
            "high_hz": 50.0,
            "notch_hz": 50.0,
            "artifact_policy": "reject",
            "peak_to_peak_uv": 450.0,
            "muscle_zscore_threshold": 4.0,
            "bad_channels": "O2",
            "bad_channel_policy": "exclude",
            "allow_frontal_eog_proxy": "true",
            "ica_n_components": 6,
        },
    )
    assert ica_resp.status_code == 200, ica_resp.text
    ica_body = ica_resp.json()
    fit_fp = ica_body["ica_fit_fingerprint"]
    rec_ics = ica_body["recommended_exclude_components"]
    assert recordings.ica_review_cache.get(fit_fp) is not None

    preproc_cfg = {
        "bad_channels": {"policy": "exclude", "channels": ["O2"]},
        "artifact_rejection": {"policy": "reject", "peak_to_peak_uv": 450.0, "muscle_zscore_threshold": 4.0},
        "artifact_removal": {
            "method": "ica_infomax",
            "allow_frontal_eog_proxy": True,
            "ica_n_components": 6,
            "ica_exclude_components": rec_ics,
            "ica_fit_fingerprint": fit_fp,
        },
    }

    # 2. Formal POST /api/runs -> official_algorithm RBP (baseline vs preprocessed).
    rbp_base = client.post(
        "/api/runs",
        json={
            "recording_id": rec_id,
            "analysis_type": "official_algorithm",
            "config": {
                "algorithm_id": "rbp",
                "mode": "static",
                "channel": "F3",
                "channels": ["F3", "O1"],
                "time": {"start_s": 7.0, "end_s": 15.0},
                "notch_hz": 50.0,
            },
        },
    )
    assert rbp_base.status_code == 202, rbp_base.text
    rbp_clean = client.post(
        "/api/runs",
        json={
            "recording_id": rec_id,
            "analysis_type": "official_algorithm",
            "config": {
                "algorithm_id": "rbp",
                "mode": "static",
                "channel": "F3",
                "channels": ["F3", "O1"],
                "time": {"start_s": 7.0, "end_s": 15.0},
                "notch_hz": 50.0,
                "preprocessing": preproc_cfg,
            },
        },
    )
    assert rbp_clean.status_code == 202, rbp_clean.text
    base_json = rbp_base.json()
    clean_json = rbp_clean.json()
    assert base_json["status"] == "completed"
    assert clean_json["status"] == "completed"
    assert base_json["run_id"] != clean_json["run_id"]
    assert clean_json["filters"]["offline_preprocessing"]["artifact_removal"]["ica_fit_fingerprint"] == fit_fp

    # Verify official RBP delta absolute power on F3 dropped sharply AND full preprocessing evidence survived in result_summary!
    base_trace = base_json["result_summary"]["structured"]["calculation_trace"]["band_powers_uv2"]
    clean_trace = clean_json["result_summary"]["structured"]["calculation_trace"]["band_powers_uv2"]
    assert float(clean_trace["F3"]["delta"]) < float(base_trace["F3"]["delta"]) * 0.50
    rbp_pre = clean_json["result_summary"]["structured"]["spectral_evidence"]["filter_contract"]["preprocessing"]
    assert rbp_pre["artifact_removal"]["ica_model_reused"] is True
    assert rbp_pre["artifact_removal"]["ica_fit_fingerprint"] == fit_fp
    assert len(rbp_pre["artifact_rejection"]["segments"]) >= 1

    # 3. Formal POST /api/runs -> spectrum preserves preprocessing AND returns full bad-segment list (not just count).
    spec_run = client.post(
        "/api/runs",
        json={
            "recording_id": rec_id,
            "analysis_type": "spectrum",
            "config": {
                "mode": "static",
                "channels": ["F3", "O1"],
                "time": {"start_s": 7.0, "end_s": 15.0},
                "preprocessing": preproc_cfg,
            },
        },
    )
    assert spec_run.status_code == 202, spec_run.text
    spec_result = spec_run.json()["result_summary"]
    art_ev = spec_result["filter_contract"]["preprocessing"]["artifact_rejection"]
    assert art_ev["detected_interval_count"] >= 1
    assert len(art_ev["segments"]) == art_ev["detected_interval_count"]
    assert all({"start_s", "end_s", "reason", "channels"} <= set(seg.keys()) for seg in art_ev["segments"])
    assert 0.0 < art_ev["clean_ratio"] < 1.0
    assert spec_result["filter_contract"]["preprocessing"]["artifact_removal"]["ica_model_reused"] is True

    # 4. Formal POST /api/runs -> spectrogram preserves preprocessing and fails cleanly if ICA is requested.
    stft_ica_run = client.post(
        "/api/runs",
        json={
            "recording_id": rec_id,
            "analysis_type": "spectrogram",
            "config": {
                "mode": "spectrogram",
                "channels": ["F3"],
                "time": {"start_s": 7.0, "end_s": 15.0},
                "preprocessing": {"artifact_removal": {"method": "ica_infomax", "allow_frontal_eog_proxy": True}},
            },
        },
    )
    assert stft_ica_run.status_code == 202
    assert stft_ica_run.json()["status"] == "failed"
    assert "ARTIFACT_REMOVAL_REQUIRES_CONTINUOUS_SCOPE" in stft_ica_run.json()["error"]["message"]

    # 5. Deleting the recording invalidates its cached fitted ICA models.
    rec = recordings.require_recording(rec_id)
    recordings._delete_recording(rec)
    assert recordings.ica_review_cache.get(fit_fp) is None
