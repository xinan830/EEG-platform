"""Integration contracts for offline artifact removal and model reuse."""
from pathlib import Path

import numpy as np
import pytest

from app.scientific.contracts.preprocessing import (
    ArtifactRejectionSpec, ArtifactRemovalSpec, BadChannelSpec, OfflinePreprocessingSpec, ScientificFilterSpec,
)
from test_offline_bad_channels import _write_multi_channel_edf
from test_offline_artifact_removal import _CHANNELS_10, _synthesize_eeg_with_blinks


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
    assert len(comp0["preview_times_s"]) == len(comp0["preview_waveform"])
    assert np.all(np.diff(comp0["preview_times_s"]) > 0)
    assert None in comp0["preview_waveform"]
    assert comp0["preview_unit"] == "z-score"
    assert comp0["preview_compression"]["scientific_resampling"] is False
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
    assert prev_body["display_compression"]["method"] == "finite_run_min_max_envelope_v1"
    for channel in prev_body["channels"]:
        assert len(prev_body["trace_times_s"][channel]) == len(prev_body["traces_uv"][channel])
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
