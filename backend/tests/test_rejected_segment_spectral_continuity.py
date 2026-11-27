"""Regression tests for spectral continuity across rejected (NaN) bad segments."""

from __future__ import annotations

from pathlib import Path

import numpy as np

from app.algorithm_runtime.on_demand import DynamicWindowIdentity, DynamicWindowRequest
from app.scientific.contracts.preprocessing import ArtifactRejectionSpec, ManualArtifactSegment
from app.scientific.primitives.annotated_spectral import estimate_annotated_channel_psd
from app.services.analysis.windows.on_demand_dynamic import DynamicWindowService
from app.bootstrap import build_builtin_registry
from app.services.analysis.runs.runs import RunService
from test_offline_bad_channels import _write_multi_channel_edf


def _synthetic_eeg(duration_s: float, sfreq: float = 200.0) -> np.ndarray:
    t = np.arange(int(round(duration_s * sfreq))) / sfreq
    signal = (
        18e-6 * np.sin(2.0 * np.pi * 10.0 * t)
        + 6e-6 * np.sin(2.0 * np.pi * 6.0 * t)
        + 4e-6 * np.sin(2.0 * np.pi * 20.0 * t)
    )
    return np.column_stack([signal, 0.8 * signal])


def test_annotated_channel_psd_skips_only_nan_sub_epochs_in_10s_window():
    sfreq = 200.0
    data = _synthetic_eeg(10.0, sfreq=sfreq)
    # 0.32 s rejected bad segment at 8.00-8.32 s hits only the last 4 s Welch sub-epoch [6..10 s].
    data[int(8.00 * sfreq):int(8.32 * sfreq), 0] = np.nan

    estimate = estimate_annotated_channel_psd(data, sfreq, low_hz=1.0, high_hz=45.0)
    assert estimate.gate_failed is None
    assert np.all(np.isfinite(estimate.psd))
    qualities = estimate.evidence["channel_quality"]
    assert qualities[0]["status"] == "warning"
    assert "non_finite" in qualities[0]["reasons"]
    assert qualities[0]["clean_segments"] == 3
    assert qualities[0]["total_segments"] == 4
    assert qualities[0]["peak_uv"] is not None and qualities[0]["peak_uv"] > 0
    assert qualities[0]["gap"]["detected"] is True
    assert qualities[1]["status"] == "clean"


def test_annotated_channel_psd_marks_unavailable_when_all_4s_sub_epochs_hit_nan():
    sfreq = 200.0
    data = _synthetic_eeg(10.0, sfreq=sfreq)
    # Rejecting [2.5 s, 6.5 s) intersects all four 4 s sub-epochs: [0..4], [2..6], [4..8], [6..10].
    data[int(2.5 * sfreq):int(6.5 * sfreq), :] = np.nan

    estimate = estimate_annotated_channel_psd(data, sfreq, low_hz=1.0, high_hz=45.0)
    assert estimate.gate_failed is None
    assert np.all(np.isnan(estimate.psd))
    qualities = estimate.evidence["channel_quality"]
    assert all(item["status"] == "unavailable" for item in qualities)
    assert all("non_finite" in item["reasons"] for item in qualities)


def test_rbp_and_psd_dynamic_and_static_continue_past_rejected_bad_segment(tmp_path: Path):
    from test_recording_import import _client

    client, recordings, _projects = _client(tmp_path)
    db = tmp_path / "platform.sqlite3"
    sfreq = 200.0
    samples_uv = _synthetic_eeg(20.0, sfreq=sfreq) * 1e6
    source = _write_multi_channel_edf(
        tmp_path / "rejected_continuity.edf", ("Fp1", "Fp2"), samples_uv, sfreq=int(sfreq),
    )
    recording_id = client.post("/api/recordings/import", files={"file": ("rejected_continuity.edf", source)}).json()["id"]
    recording = recordings.require_recording(recording_id)

    spec = ArtifactRejectionSpec(
        policy="reject",
        peak_to_peak_uv=None,
        muscle_zscore_threshold=None,
        manual_segments=(
            ManualArtifactSegment(start_sample=int(14.00 * sfreq), end_sample=int(14.32 * sfreq), reason="emg"),
        ),
    )
    from app.services.preprocessing.offline_scientific_preprocessing import OfflineScientificPreprocessingService
    from app.services.analysis.spectral_analysis import SpectralAnalysisService
    spectral = SpectralAnalysisService(recordings, OfflineScientificPreprocessingService.for_recordings(recordings))
    static_spectrum = spectral.load_spectrum(
        recording,
        start_s=0.0,
        window_s=20.0,
        channels=["Fp1", "Fp2"],
        quality_policy="per_channel_annotation",
        artifact_rejection_spec=spec,
    )
    assert np.all(np.isfinite(static_spectrum["psd"]["Fp1"]))
    assert static_spectrum["quality"]["by_channel"]["Fp1"]["status"] == "warning"
    assert "non_finite" in static_spectrum["quality"]["by_channel"]["Fp1"]["reasons"]

    runs = RunService(recordings, db, tmp_path / "artifacts", algorithm_registry=build_builtin_registry())
    dynamic = DynamicWindowService(
        recordings, runs.algorithm_runtime_registry, database_path=db, artifacts_dir=tmp_path / "artifacts",
    )
    preprocessing = {
        "bad_channels": {"policy": "none", "channels": []},
        "artifact_rejection": {
            "policy": "reject",
            "peak_to_peak_uv": None,
            "muscle_zscore_threshold": None,
            "manual_segments": [
                {"start_sample": int(14.00 * sfreq), "end_sample": int(14.32 * sfreq), "reason": "emg"},
            ],
        },
        "artifact_removal": {"method": "none"},
    }
    try:
        for algorithm_id in ("rbp", "psd"):
            cfg = {
                "algorithm_id": algorithm_id,
                "mode": "dynamic",
                "channels": ["Fp1", "Fp2"],
                "time": {"start_s": 0.0, "end_s": 20.0},
                "dynamic_window_s": 10.0,
                "refresh_step_s": 1.0,
                "preprocessing": preprocessing,
            }
            if algorithm_id == "rbp":
                cfg["channel"] = "Fp1"
            for cursor_s in (15.0, 16.0, 18.0):
                prepared = dynamic._planner.prepare(
                    DynamicWindowRequest(
                        recording_id=recording_id,
                        algorithm_id=algorithm_id,
                        config=cfg,
                        cursor_sample=int(cursor_s * sfreq),
                    )
                )
                identity = DynamicWindowIdentity(
                    **prepared.plan.session_identity.model_dump(),
                    generation=1,
                    start_sample=prepared.plan.start_sample,
                    end_sample=prepared.plan.end_sample,
                )
                try:
                    frame_result = prepared.capability.execute_window(
                        prepared.inputs, prepared.config, prepared.plan, identity,
                    )
                finally:
                    if hasattr(prepared.inputs.payload, "close"):
                        prepared.inputs.payload.close()
                assert frame_result.state == "ready", (
                    f"{algorithm_id} aborted at {cursor_s}s: {frame_result.failure}"
                )
                assert frame_result.quality == "partial"
                if algorithm_id == "rbp":
                    shares = np.column_stack([
                        np.asarray(frame_result.arrays[band], dtype=float)
                        for band in ("delta", "theta", "alpha", "beta", "gamma")
                    ])
                    assert np.all(np.isfinite(shares))
                    assert np.allclose(shares.sum(axis=1), 1.0, atol=1e-6)
                    warnings = frame_result.evidence["extensions"]["channel_warnings"]["by_channel"]
                    assert "non_finite" in warnings["Fp1"]["reasons"]
                else:
                    psd_values = np.asarray(frame_result.arrays["psd"], dtype=float)
                    assert np.all(np.isfinite(psd_values))
    finally:
        dynamic.close()
