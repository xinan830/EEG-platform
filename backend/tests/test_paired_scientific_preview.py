"""Contract and equivalence tests for paired scientific preview comparison (scientific-preview-comparison-v1)."""
from __future__ import annotations

from pathlib import Path
from types import SimpleNamespace

import numpy as np
import pytest

from app.scientific.contracts.preprocessing import (
    ArtifactRejectionSpec,
    ArtifactRemovalSpec,
    BadChannelSpec,
)
from app.services.preprocessing_preview import build_preprocessing_preview_window


def _make_fake_recordings(baseline_source, processed_source=None, *, formulas=None):
    calls: list[dict] = []

    def prepare_continuous(recording, **kwargs):
        calls.append(kwargs)
        spec = kwargs.get("preprocessing_spec")
        is_enhanced = False
        if spec is not None:
            is_enhanced = (
                spec.bad_channels.is_active
                or spec.artifact_rejection.is_active
                or spec.artifact_removal.is_active
            )
        else:
            is_enhanced = any(
                arg is not None and arg.normalized().is_active
                for arg in (
                    kwargs.get("bad_channel_spec"),
                    kwargs.get("artifact_rejection_spec"),
                    kwargs.get("artifact_removal_spec"),
                )
            )
        if is_enhanced and processed_source is not None:
            return processed_source
        return baseline_source

    recordings = SimpleNamespace(
        offline_scientific_preprocessing=SimpleNamespace(prepare_continuous=prepare_continuous),
        analysis_signal_provenance=lambda *_a, **_k: {"formulas": formulas or [], "snapshot_sha256": "montage-fp-1"},
        select_analysis_montage=lambda rec, _mid: rec,
    )
    return recordings, calls


def test_paired_preview_disabled_policies_reuses_single_preparation_and_matches_exactly():
    values = np.column_stack([
        np.sin(2 * np.pi * 10 * np.arange(2000) / 1000.0) * 20e-6,
        np.cos(2 * np.pi * 15 * np.arange(2000) / 1000.0) * 15e-6,
    ])
    values[500:510, 0] = np.nan
    identity = SimpleNamespace(recording_id="rec-1", source_sha256="sha-1", signal_view_sha256="view-1")
    source = SimpleNamespace(
        data=values, sfreq=1000.0, channel_names=("Fp1", "C4"),
        artifact_removal_evidence=None, source_identity=identity,
    )
    recordings, calls = _make_fake_recordings(source)

    result = build_preprocessing_preview_window(
        recordings,
        SimpleNamespace(id="rec-1", source_sha256="sha-1"),
        start_s=0.2,
        window_s=0.5,
        max_points=400,
    )

    assert len(calls) == 1, "All-disabled comparison must reuse one continuous scientific preparation"
    assert result["schema_version"] == "scientific-preview-comparison-v1"
    assert result["unit"] == "µV"
    assert result["enhanced_preprocessing_active"] is False
    assert result["start_sample"] == 200 and result["end_sample"] == 700
    assert result["common_filter"]["filter_phase"] == "zero_phase"
    assert result["common_filter"]["filter_bandpass_hz"] == [1.0, 50.0]
    assert result["common_filter"]["notch_hz"] == 50.0
    assert result["common_filter"]["notch_quality_factor"] == 30.0
    assert result["display_compression"]["method"] == "paired_finite_run_min_max_envelope_v1"
    assert result["display_compression"]["scientific_resampling"] is False
    assert result["baseline"]["channels"] == ["Fp1", "C4"]
    assert result["processed"]["channels"] == ["Fp1", "C4"]
    assert result["processed"]["excluded_output_channels"] == []
    for ch in ("Fp1", "C4"):
        assert result["baseline"]["trace_times_s"][ch] == result["processed"]["trace_times_s"][ch]
        assert result["baseline"]["traces_uv"][ch] == result["processed"]["traces_uv"][ch]


def test_paired_preview_preserves_one_sided_narrow_peak_and_gap_on_shared_times():
    base_data = np.zeros((2000, 1), dtype=np.float64)
    proc_data = np.zeros((2000, 1), dtype=np.float64)
    base_data[333, 0] = 140e-6
    proc_data[777, 0] = -95e-6
    proc_data[1200:1205, 0] = np.nan

    identity = SimpleNamespace(recording_id="rec-1", source_sha256="sha-1", signal_view_sha256="view-1")
    base_src = SimpleNamespace(data=base_data, sfreq=1000.0, channel_names=("Fp1",), artifact_removal_evidence=None, source_identity=identity)
    proc_src = SimpleNamespace(data=proc_data, sfreq=1000.0, channel_names=("Fp1",), artifact_removal_evidence=None, source_identity=identity)
    recordings, calls = _make_fake_recordings(base_src, proc_src)

    result = build_preprocessing_preview_window(
        recordings,
        SimpleNamespace(id="rec-1", source_sha256="sha-1"),
        start_s=0.0,
        window_s=2.0,
        artifact_rejection_spec=ArtifactRejectionSpec(policy="reject"),
        max_points=200,
    )

    assert len(calls) == 2
    assert result["enhanced_preprocessing_active"] is True
    b_times = result["baseline"]["trace_times_s"]["Fp1"]
    p_times = result["processed"]["trace_times_s"]["Fp1"]
    b_vals = result["baseline"]["traces_uv"]["Fp1"]
    p_vals = result["processed"]["traces_uv"]["Fp1"]
    assert b_times == p_times
    assert np.all(np.diff(b_times) > 0)
    assert 140.0 in b_vals
    assert -95.0 in p_vals
    assert None in p_vals
    assert None not in b_vals


def test_paired_preview_excluded_channel_retains_baseline_row_without_shifting():
    base_data = np.column_stack([np.ones(1000) * 10e-6, np.ones(1000) * 20e-6, np.ones(1000) * 30e-6])
    proc_data = np.column_stack([np.ones(1000) * 10e-6, np.ones(1000) * 30e-6])
    identity = SimpleNamespace(recording_id="rec-1", source_sha256="sha-1", signal_view_sha256="view-1")
    base_src = SimpleNamespace(data=base_data, sfreq=500.0, channel_names=("Fp1", "C4", "O1"), artifact_removal_evidence=None, source_identity=identity)
    proc_src = SimpleNamespace(data=proc_data, sfreq=500.0, channel_names=("Fp1", "O1"), artifact_removal_evidence=None, source_identity=identity)
    recordings, _ = _make_fake_recordings(base_src, proc_src)

    result = build_preprocessing_preview_window(
        recordings,
        SimpleNamespace(id="rec-1", source_sha256="sha-1"),
        start_s=0.0,
        window_s=2.0,
        bad_channel_spec=BadChannelSpec(policy="exclude", channels=("C4",)),
        max_points=200,
    )

    assert result["baseline"]["channels"] == ["Fp1", "C4", "O1"]
    assert result["processed"]["channels"] == ["Fp1", "O1"]
    assert result["processed"]["excluded_output_channels"] == ["C4"]
    assert "C4" in result["baseline"]["traces_uv"]
    assert "C4" not in result["processed"]["traces_uv"]
    assert all(v == 30.0 for v in result["processed"]["traces_uv"]["O1"])


def test_paired_preview_rejects_mismatched_source_identity_between_sides():
    base_src = SimpleNamespace(
        data=np.zeros((500, 1)), sfreq=500.0, channel_names=("Fp1",), artifact_removal_evidence=None,
        source_identity=SimpleNamespace(recording_id="rec-1", source_sha256="sha-old", signal_view_sha256="v1"),
    )
    proc_src = SimpleNamespace(
        data=np.zeros((500, 1)), sfreq=500.0, channel_names=("Fp1",), artifact_removal_evidence=None,
        source_identity=SimpleNamespace(recording_id="rec-1", source_sha256="sha-new", signal_view_sha256="v1"),
    )
    recordings, _ = _make_fake_recordings(base_src, proc_src)

    with pytest.raises(ValueError, match="SOURCE_IDENTITY_MISMATCH"):
        build_preprocessing_preview_window(
            recordings,
            SimpleNamespace(id="rec-1", source_sha256="sha-old"),
            start_s=0.0,
            window_s=1.0,
            artifact_rejection_spec=ArtifactRejectionSpec(policy="reject"),
        )


def test_paired_preview_api_all_disabled_matches_both_sides_on_real_edf(tmp_path: Path):
    from test_offline_bad_channels import _write_multi_channel_edf
    from test_recording_import import _client

    sfreq = 250
    t = np.arange(sfreq * 10) / sfreq
    data_uv = np.column_stack([
        25.0 * np.sin(2 * np.pi * 10.0 * t),
        18.0 * np.cos(2 * np.pi * 22.0 * t),
        12.0 * np.sin(2 * np.pi * 6.0 * t),
    ])
    client, _, _ = _client(tmp_path)
    source = _write_multi_channel_edf(tmp_path / "paired_base.edf", ["Fp1", "C3", "O1"], data_uv, sfreq=sfreq)
    uploaded = client.post("/api/recordings/import", files={"file": ("paired_base.edf", source)})
    assert uploaded.status_code == 201, uploaded.text
    rec_id = uploaded.json()["id"]

    resp = client.post(
        f"/api/recordings/{rec_id}/preprocessing/preview-window",
        json={"start_s": 2.0, "window_s": 0.4, "low_hz": 1.0, "high_hz": 50.0, "notch_hz": 50.0, "max_points": 500},
    )
    assert resp.status_code == 200, resp.text
    body = resp.json()
    assert body["schema_version"] == "scientific-preview-comparison-v1"
    assert body["enhanced_preprocessing_active"] is False
    assert body["start_sample"] == 500 and body["end_sample"] == 600
    assert body["baseline"]["traces_uv"] == body["processed"]["traces_uv"]
    assert body["baseline"]["trace_times_s"] == body["processed"]["trace_times_s"]
