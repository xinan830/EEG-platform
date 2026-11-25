"""Scientific gaps stay intact; oversized display responses fail explicitly."""
from types import SimpleNamespace

import numpy as np
import pytest

from app.scientific.contracts.preprocessing import ArtifactRejectionSpec, BadChannelSpec
from app.services.preprocessing_preview import build_preprocessing_preview_window
from app.services.preview_envelope import (
    MAX_PAIRED_PREVIEW_POINTS_PER_TRACE,
    MAX_PAIRED_PREVIEW_RESPONSE_SCALARS,
    PreviewEnvelopeLimitError,
    paired_preview_envelope_indices,
)
from test_paired_scientific_preview import _make_fake_recordings


def _source(data, names):
    return SimpleNamespace(data=data, sfreq=1000.0, channel_names=names,
                           artifact_removal_evidence=None)


def test_dense_gaps_rejected_before_collecting_unbounded_indices():
    baseline = np.ones(120000)
    processed = baseline.copy()
    processed[1::2] = np.nan
    with pytest.raises(PreviewEnvelopeLimitError, match="缩短"):
        paired_preview_envelope_indices(baseline, processed, target_points=4000)


def test_mandatory_gap_boundaries_can_exceed_soft_target_within_hard_limit():
    baseline = np.ones(100)
    processed = baseline.copy()
    processed[1::2] = np.nan
    indices = paired_preview_envelope_indices(baseline, processed, target_points=16)
    assert indices == list(range(100))
    assert len(indices) <= MAX_PAIRED_PREVIEW_POINTS_PER_TRACE


def test_bucket_extrema_are_checked_against_remaining_budget():
    baseline = np.arange(100, dtype=np.float64)
    with pytest.raises(PreviewEnvelopeLimitError):
        paired_preview_envelope_indices(baseline, baseline, target_points=100, max_output_points=10)


def test_excluded_output_baseline_also_obeys_hard_trace_limit():
    data = np.ones((20000, 1))
    data[1::2] = np.nan
    recordings, _ = _make_fake_recordings(_source(data, ("C4",)), _source(data[:, :0], ()))
    with pytest.raises(PreviewEnvelopeLimitError):
        build_preprocessing_preview_window(
            recordings, SimpleNamespace(id="rec"), start_s=0, window_s=20,
            bad_channel_spec=BadChannelSpec(policy="exclude", channels=("C4",)))


@pytest.mark.parametrize("excluded", [False, True])
def test_whole_response_budget_counts_all_channels_and_both_sides(excluded):
    names = tuple(f"EEG{i}" for i in range(20))
    baseline = np.ones((14000, len(names)))
    baseline[1::2] = np.nan
    proc_names = names[:-1] if excluded else names
    recordings, _ = _make_fake_recordings(_source(baseline, names),
                                        _source(baseline[:, :len(proc_names)], proc_names))
    assert baseline.shape[0] < MAX_PAIRED_PREVIEW_POINTS_PER_TRACE
    assert baseline.size * 4 > MAX_PAIRED_PREVIEW_RESPONSE_SCALARS
    with pytest.raises(PreviewEnvelopeLimitError):
        build_preprocessing_preview_window(
            recordings, SimpleNamespace(id="rec"), start_s=0, window_s=14,
            bad_channel_spec=BadChannelSpec(policy="exclude", channels=(names[-1],)) if excluded else None,
            artifact_rejection_spec=ArtifactRejectionSpec(policy="reject"), max_points=4000)


def test_preview_api_reports_stable_limit_error_and_request_id(tmp_path, monkeypatch):
    from test_recording_import import _client

    client, recordings, _ = _client(tmp_path)
    recording = SimpleNamespace(id="rec")
    monkeypatch.setattr(recordings, "require_recording", lambda _: recording)
    values = np.ones((20000, 1))
    values[1::2] = np.nan
    monkeypatch.setattr(recordings.offline_scientific_preprocessing, "prepare_continuous",
                        lambda *_a, **_k: _source(values, ("F3",)))
    response = client.post("/api/recordings/rec/preprocessing/preview-window",
                           headers={"X-Request-ID": "preview-limit-test"},
                           json={"start_s": 0, "window_s": 20, "max_points": 4000})
    assert response.status_code == 422
    assert response.json()["code"] == "PREPROCESSING_PREVIEW_LIMIT_EXCEEDED"
    assert response.json()["request_id"] == "preview-limit-test"
    assert "缩短" in response.json()["message"]
