"""Regression tests for the 5 reported offline scientific preprocessing bugs."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

import numpy as np
import pytest

from app.algorithm_runtime.offline_execution import scientific_configuration_sha256
from app.algorithm_runtime.on_demand import DynamicWindowRequest
from app.models.run import RunCreateRequest
from app.scientific.artifact_segments import apply_artifact_rejection_mask, detect_artifact_segments
from app.scientific.contracts.preprocessing import (
    ArtifactRejectionSpec,
    BadChannelSpec,
    ManualArtifactSegment,
    OfflinePreprocessingSpec,
    ScientificFilterSpec,
)
from app.scientific.primitives import preprocess_offline
from app.services.on_demand_dynamic import DynamicWindowService
from app.services.runs import RunService
from test_offline_bad_channels import _write_multi_channel_edf


def test_bug1_per_channel_bad_segments_do_not_contaminate_other_channels_clean_samples():
    sfreq = 500.0
    n_samples = 2000
    channels = ("F3", "O1")
    rng = np.random.default_rng(101)
    data = rng.normal(0.0, 10e-6, size=(n_samples, 2))

    # 1) Manual overlapping segments on different channels: F3=[100,200), O1=[150,250)
    manual_spec = ArtifactRejectionSpec(
        policy="reject",
        peak_to_peak_uv=None,
        muscle_zscore_threshold=None,
        manual_segments=(
            ManualArtifactSegment(start_sample=100, end_sample=200, reason="manual", channels=("F3",)),
            ManualArtifactSegment(start_sample=150, end_sample=250, reason="manual", channels=("O1",)),
        ),
    )
    report = detect_artifact_segments(data, sfreq, channels, manual_spec)
    assert len(report.segments) == 2
    masked = apply_artifact_rejection_mask(data, report.segments, channels)
    assert np.all(np.isnan(masked[100:200, 0]))
    assert np.all(np.isfinite(masked[200:250, 0]))  # F3 clean tail preserved
    assert np.all(np.isfinite(masked[100:150, 1]))  # O1 clean lead preserved
    assert np.all(np.isnan(masked[150:250, 1]))

    # 2) Automatic amplitude PTP bursts staggered across F3 and O1
    # Use win=0.2s (100 samples, step=25). F3 burst [400, 600), O1 burst [550, 750):
    # O1's [400, 450) is well before any O1 burst window; F3's [700, 750) is well after any F3 burst window.
    t = np.arange(n_samples) / sfreq
    burst_data = data.copy()
    burst_data[400:600, 0] += 500e-6 * np.sin(2.0 * np.pi * 12.0 * t[400:600])
    burst_data[550:750, 1] += 500e-6 * np.sin(2.0 * np.pi * 12.0 * t[550:750])
    auto_spec = ArtifactRejectionSpec(
        policy="reject",
        peak_to_peak_uv=300.0,
        amplitude_window_s=0.2,
        muscle_zscore_threshold=None,
        min_duration_s=0.05,
        pad_s=0.0,
    )
    auto_report = detect_artifact_segments(burst_data, sfreq, channels, auto_spec)
    auto_masked = apply_artifact_rejection_mask(burst_data, auto_report.segments, channels)
    assert np.all(np.isnan(auto_masked[420:580, 0]))
    assert np.all(np.isfinite(auto_masked[400:440, 1]))  # O1 clean region before its own burst stays finite
    assert np.all(np.isnan(auto_masked[570:730, 1]))
    assert np.all(np.isfinite(auto_masked[710:750, 0]))  # F3 clean region after its own burst stays finite


def test_bug2_global_manual_bad_segment_on_f3_allows_single_channel_o1_spectrogram(tmp_path: Path):
    from test_recording_import import _client

    client, recordings, _ = _client(tmp_path)
    sfreq = 250
    t = np.arange(0, 12.0, 1.0 / sfreq)
    rng = np.random.default_rng(202)
    samples_uv = rng.normal(0.0, 10.0, size=(t.size, 2)) + (12.0 * np.sin(2 * np.pi * 10.0 * t))[:, None]
    source = _write_multi_channel_edf(tmp_path / "f3_o1.edf", ("F3", "O1"), samples_uv, sfreq=sfreq)
    rec_id = client.post("/api/recordings/import", files={"file": ("f3_o1.edf", source)}).json()["id"]

    prep_valid_f3 = {
        "artifact_rejection": {
            "policy": "reject",
            "peak_to_peak_uv": None,
            "muscle_zscore_threshold": None,
            "manual_segments": [
                {"start_sample": 50, "end_sample": 200, "reason": "manual", "channels": ["F3"]},
            ],
        },
    }
    psd_resp = client.post(
        f"/api/recordings/{rec_id}/spectrum/configured",
        json={"mode": "static", "channels": ["O1"], "time": {"start_s": 4.0, "end_s": 10.0}, "preprocessing": prep_valid_f3},
    )
    assert psd_resp.status_code == 200, psd_resp.text

    stft_resp = client.post(
        f"/api/recordings/{rec_id}/spectrogram/configured",
        json={"mode": "spectrogram", "channels": ["O1"], "time": {"start_s": 4.0, "end_s": 10.0}, "preprocessing": prep_valid_f3},
    )
    assert stft_resp.status_code == 200, stft_resp.text

    # Unknown channel NOT in the recording must still be rejected with 422.
    prep_unknown = {
        "artifact_rejection": {
            "policy": "reject",
            "peak_to_peak_uv": None,
            "muscle_zscore_threshold": None,
            "manual_segments": [
                {"start_sample": 50, "end_sample": 200, "reason": "manual", "channels": ["GHOST_CH"]},
            ],
        },
    }
    bad_stft_resp = client.post(
        f"/api/recordings/{rec_id}/spectrogram/configured",
        json={"mode": "spectrogram", "channels": ["O1"], "time": {"start_s": 4.0, "end_s": 10.0}, "preprocessing": prep_unknown},
    )
    assert bad_stft_resp.status_code == 422
    assert "ARTIFACT_SEGMENT_CHANNEL_NOT_FOUND" in bad_stft_resp.text


def test_bug3_notch_q_propagates_to_continuous_and_bounded_execution_and_cache_identity(tmp_path: Path):
    from test_recording_import import _client

    client, recordings, _ = _client(tmp_path)
    sfreq = 500
    t = np.arange(0, 8.0, 1.0 / sfreq)
    # Include 48 Hz adjacent to 50 Hz notch so Q=5 (wide) attenuates 48 Hz much more than Q=30 (narrow).
    sig_uv = (
        15.0 * np.sin(2.0 * np.pi * 10.0 * t)
        + 40.0 * np.sin(2.0 * np.pi * 48.0 * t)
        + 60.0 * np.sin(2.0 * np.pi * 50.0 * t)
    )
    samples_uv = np.column_stack((sig_uv, sig_uv * 0.8))
    source = _write_multi_channel_edf(tmp_path / "notch_q.edf", ("F3", "O1"), samples_uv, sfreq=sfreq)
    rec_id = client.post("/api/recordings/import", files={"file": ("notch_q.edf", source)}).json()["id"]
    recording = recordings.require_recording(rec_id)
    preproc = recordings.offline_scientific_preprocessing

    spec_q5 = OfflinePreprocessingSpec(
        filter_spec=ScientificFilterSpec(low_hz=1.0, high_hz=60.0, notch_hz=50.0, notch_q=5.0,
                                         scope="whole_recording_before_window_slice"),
    )
    spec_q30 = OfflinePreprocessingSpec(
        filter_spec=ScientificFilterSpec(low_hz=1.0, high_hz=60.0, notch_hz=50.0, notch_q=30.0,
                                         scope="whole_recording_before_window_slice"),
    )
    res_q5 = preproc.prepare_continuous(recording, low_hz=1.0, high_hz=60.0, notch_hz=50.0, preprocessing_spec=spec_q5)
    res_q30 = preproc.prepare_continuous(recording, low_hz=1.0, high_hz=60.0, notch_hz=50.0, preprocessing_spec=spec_q30)

    assert res_q5 is not res_q30
    assert res_q5.contract.identity != res_q30.contract.identity
    assert not np.allclose(res_q5.data, res_q30.data, rtol=1e-3, atol=1e-8)

    # Verify bounded window path also honors notch_q and isolates cache entries.
    b_q5 = OfflinePreprocessingSpec(
        filter_spec=ScientificFilterSpec(low_hz=1.0, high_hz=60.0, notch_hz=50.0, notch_q=5.0,
                                         scope="bounded_window_with_finite_context"),
    )
    b_q30 = OfflinePreprocessingSpec(
        filter_spec=ScientificFilterSpec(low_hz=1.0, high_hz=60.0, notch_hz=50.0, notch_q=30.0,
                                         scope="bounded_window_with_finite_context"),
    )
    with recordings.open_access_session(recording) as session:
        win_q5 = preproc.prepare_bounded_window(
            session, start_sample=500, end_sample=2500, channels=["F3"],
            low_hz=1.0, high_hz=60.0, notch_hz=50.0, context_s=0.5, algorithm_version="stft-v1",
            preprocessing_spec=b_q5,
        )
        win_q30 = preproc.prepare_bounded_window(
            session, start_sample=500, end_sample=2500, channels=["F3"],
            low_hz=1.0, high_hz=60.0, notch_hz=50.0, context_s=0.5, algorithm_version="stft-v1",
            preprocessing_spec=b_q30,
        )
        assert win_q5 is not win_q30
        assert not np.allclose(win_q5.data, win_q30.data, rtol=1e-3, atol=1e-8)
        raw_block = session.read_samples(250, 2750, ("F3",)).values_v
        expected_q5 = preprocess_offline(raw_block, float(sfreq), low_hz=1.0, high_hz=60.0, notch_hz=50.0, notch_q=5.0)[250:2250]
        assert np.allclose(win_q5.data, expected_q5, rtol=1e-10, atol=1e-15)


def test_bug4_montage_mode_continuous_cache_hits_when_bad_channel_excluded(tmp_path: Path, monkeypatch):
    from test_recording_import import _client

    client, recordings, _ = _client(tmp_path)
    project_id = client.put("/api/projects/desktop/car-cache-bug4", json={"name": "CarCache"}).json()["project_id"]
    labels = ("Fp1", "Fp2", "F3", "F4", "C3", "C4")
    sfreq = 200
    t = np.arange(0, 10.0, 1.0 / sfreq)
    samples_uv = np.column_stack([20.0 * np.sin(2 * np.pi * 10.0 * t + 0.1 * idx) for idx in range(len(labels))])
    source = _write_multi_channel_edf(tmp_path / "car_cache.edf", labels, samples_uv, sfreq=sfreq)
    channel_snapshot = {
        "Id": "ch-cfg",
        "DeviceSignature": "ant-layout",
        "HardwareReferenceElectrodeLocation": "REF",
        "HardwareGroundElectrodeLocation": "GND",
        "Channels": [
            {"NativeChannelIndex": idx, "ExpectedKind": 1, "ElectrodeLabel": label,
             "IsSelectedForDisplay": True, "DisplayOrder": idx}
            for idx, label in enumerate(labels)
        ],
    }
    car_montage = {
        "Id": "car-montage",
        "DeviceSignature": "ant-layout",
        "ChannelConfigurationSnapshot": channel_snapshot,
        "AverageReferenceLabels": list(labels),
        "DerivedChannels": [
            {"Name": f"{label}-AVG", "PositiveLabel": label, "NegativeKind": 2,
             "NegativeLabels": list(labels), "DisplayOrder": idx}
            for idx, label in enumerate(labels)
        ],
    }
    identity = {
        "exported_signal_sha256": hashlib.sha256(source).hexdigest(),
        "channel_configuration_snapshot_json": channel_snapshot,
        "channels": [
            {"export_label": label, "source_label": label, "native_channel_index": idx, "kind": "1"}
            for idx, label in enumerate(labels)
        ],
        "hardware": {
            "acquisition_montage_snapshot_json": {"Id": "car-montage"},
            "montage_configuration_snapshot_json": car_montage,
            "montage_catalogue_snapshot_json": [car_montage],
        },
    }
    imported = client.post(
        f"/api/projects/{project_id}/recordings/import",
        data={"new_subject_local_code": "S-04", "export_channel_identity_json": json.dumps(identity)},
        files={"file": ("car_cache.edf", source)},
    )
    assert imported.status_code == 201, imported.text
    recording = recordings.require_recording(imported.json()["recording_id"])
    selected = recordings.select_analysis_montage(recording, "car-montage")

    open_calls = 0
    real_open = recordings.open_access_session

    def counted_open(*args, **kwargs):
        nonlocal open_calls
        open_calls += 1
        return real_open(*args, **kwargs)

    monkeypatch.setattr(recordings, "open_access_session", counted_open)

    bad_spec = BadChannelSpec(policy="exclude", channels=("C4",))
    preproc = recordings.offline_scientific_preprocessing

    first = preproc.prepare_continuous(selected, low_hz=1.0, high_hz=45.0, notch_hz=50.0, bad_channel_spec=bad_spec)
    assert "C4-AVG" not in first.channel_names
    assert open_calls == 1

    second = preproc.prepare_continuous(selected, low_hz=1.0, high_hz=45.0, notch_hz=50.0, bad_channel_spec=bad_spec)
    assert second is first
    assert open_calls == 1  # Cache hit — must NOT reopen reader or re-prepare!


def test_bug5_full_run_and_on_demand_share_identical_scientific_identity_with_preprocessing(tmp_path: Path):
    from test_recording_import import _client

    client, recordings, _projects = _client(tmp_path)
    db = tmp_path / "platform.sqlite3"
    sfreq = 250
    t = np.arange(0, 14.0, 1.0 / sfreq)
    rng = np.random.default_rng(505)
    samples_uv = rng.normal(0.0, 12.0, size=(t.size, 4)) + (15.0 * np.sin(2 * np.pi * 10.0 * t))[:, None]
    source = _write_multi_channel_edf(tmp_path / "identity_sync.edf", ("Fp1", "Fp2", "F3", "O1"), samples_uv, sfreq=sfreq)
    rec_id = client.post("/api/recordings/import", files={"file": ("identity_sync.edf", source)}).json()["id"]
    recording = recordings.require_recording(rec_id)

    runs = RunService(recordings, db, tmp_path / "artifacts")
    dynamic_service = DynamicWindowService(
        recordings, runs.algorithm_runtime_registry, database_path=db, artifacts_dir=tmp_path / "artifacts",
    )

    base_cfg = {
        "algorithm_id": "rbp",
        "mode": "dynamic",
        "channel": "F3",
        "channels": ["F3", "O1"],
        "time": {"start_s": 0.0, "end_s": 14.0},
        "dynamic_window_s": 10.0,
        "refresh_step_s": 1.0,
        "preprocessing": {
            "bad_channels": {"policy": "exclude", "channels": ["Fp2"]},
            "artifact_rejection": {"policy": "annotate", "peak_to_peak_uv": 350.0, "muscle_zscore_threshold": 4.0},
            "artifact_removal": {"method": "none"},
        },
    }

    full_prepared = runs._resolve_request(
        RunCreateRequest(recording_id=rec_id, analysis_type="official_algorithm", config=base_cfg),
        recording,
    )
    full_ctx_id = str(full_prepared["scientific_context"]["replay_context"]["context_identity"])
    full_cfg_sha = scientific_configuration_sha256(full_prepared["config"], full_prepared["scientific_context"])

    on_demand_prepared = dynamic_service._planner.prepare(
        DynamicWindowRequest(
            recording_id=rec_id,
            algorithm_id="rbp",
            config=base_cfg,
            cursor_sample=int(5.0 * sfreq),
        )
    )
    on_demand_ctx_id = str(
        on_demand_prepared.session.configuration["_scientific_context"]["replay_context"]["context_identity"]
    )
    assert on_demand_ctx_id == full_ctx_id
    assert on_demand_prepared.session.configuration_sha256 == full_cfg_sha
    dynamic_service.close()
