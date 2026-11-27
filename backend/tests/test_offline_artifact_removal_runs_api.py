"""Integration contracts for offline artifact removal and model reuse."""
from pathlib import Path

import numpy as np
from test_offline_bad_channels import _write_multi_channel_edf
from test_offline_artifact_removal import _CHANNELS_10, _synthesize_eeg_with_blinks


def test_formal_api_runs_official_rbp_spectrum_full_bad_segment_evidence_and_ica_cache_deletion(tmp_path: Path):
    from app.services.analysis.official_algorithm_catalog import ensure_official_definitions
    from app.bootstrap import build_builtin_registry
    from app.services.analysis.runs.runs import RunService
    from test_recording_import import _client

    sfreq = 250.0
    mixed, _clean, _blink = _synthesize_eeg_with_blinks(sfreq=sfreq, duration_s=16.0, seed=404)
    mixed[1375:1500, 4] += 650e-6 * np.sin(2.0 * np.pi * 5.0 * np.arange(125) / sfreq)

    client, recordings, _ = _client(tmp_path)
    app = client.app
    runs = RunService(
        recordings, recordings.database_path, tmp_path / "artifacts",
        algorithm_registry=build_builtin_registry(),
        spectral_analysis=app.state.spectral_analysis_service,
    )
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
    assert app.state.offline_scientific_preprocessing_service.ica_review_cache.get(fit_fp) is not None

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
    assert app.state.offline_scientific_preprocessing_service.ica_review_cache.get(fit_fp) is None
