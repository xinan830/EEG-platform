"""Exact amplitude windows must match an independent brute-force reference."""

import numpy as np
import pytest

from app.scientific.artifact_segments import (
    _detect_amplitude_ptp_segments, apply_artifact_rejection_mask, detect_artifact_segments,
)
from app.scientific.contracts.preprocessing import ArtifactRejectionSpec


def _reference(data, *, win, threshold_v, min_duration=1, pad=0, offset=0):
    if len(data) < 2:
        return []
    win = max(2, min(len(data), win))
    results = []
    for column in range(data.shape[1]):
        merged = []
        for start in range(len(data) - win + 1):
            values = data[start:start + win, column]
            finite = values[np.isfinite(values)]
            if len(finite) < 2 or np.ptp(finite) <= threshold_v:
                continue
            peak = float(np.ptp(finite)) * 1e6
            end = start + win
            if merged and start <= merged[-1][1]:
                first, previous_end, previous_peak = merged[-1]
                merged[-1] = (first, max(previous_end, end), max(previous_peak, peak))
            else:
                merged.append((start, end, peak))
        for start, end, peak in merged:
            if end - start >= min_duration:
                results.append((offset + max(0, start - pad),
                                offset + min(len(data), end + pad), column, peak))
    return results


def _assert_reference(data, *, win, threshold_v, min_duration=1, pad=0, offset=0):
    names = tuple(f'CH{i}' for i in range(data.shape[1]))
    actual = _detect_amplitude_ptp_segments(
        data, 1000, names, threshold_v=threshold_v, window_s=win / 1000,
        min_duration_samples=min_duration, pad_samples=pad, offset=offset,
    )
    expected = _reference(data, win=win, threshold_v=threshold_v,
                          min_duration=min_duration, pad=pad, offset=offset)
    assert len(actual) == len(expected)
    for segment, (start, end, column, peak) in zip(actual, expected, strict=True):
        assert (segment.start_sample, segment.end_sample, segment.channels) == (start, end, (names[column],))
        assert segment.peak_metric == pytest.approx(peak, rel=1e-14)
    return actual


def test_off_grid_extrema_detected_at_every_former_125_sample_phase():
    for phase in range(125):
        data = np.zeros((2400, 2), dtype=np.float64)
        data[1118 + phase, 0] = 107e-6
        data[1549 + phase, 0] = -198e-6
        actual = _assert_reference(data, win=500, threshold_v=300e-6, offset=2000)
        assert len(actual) == 1
        assert actual[0].start_sample == 3050 + phase
        assert actual[0].end_sample == 3618 + phase


@pytest.mark.parametrize('count,win', [(0, 8), (1, 8), (2, 2), (3, 8), (9, 3),
                                     (9, 4), (48, 7), (48, 8), (48, 49), (96, 20)])
@pytest.mark.parametrize('pad,min_duration', [(0, 1), (3, 12)])
def test_exact_windows_match_brute_force_with_nonfinite_samples(count, win, pad, min_duration):
    data = np.random.default_rng(923 + count).normal(0, 100e-6, (count, 3))
    data[::11, 0] = np.nan
    data[::13, 1] = np.inf
    data[::17, 2] = -np.inf
    _assert_reference(data, win=win, threshold_v=220e-6, pad=pad,
                      min_duration=min_duration, offset=173)


def test_threshold_is_strict_and_missing_samples_are_never_zero_filled():
    data = np.array([[200e-6, 0, np.nan, np.nan],
                     [np.nan, 300e-6, np.nan, 200e-6],
                     [np.nan, 0, np.nan, 200e-6]])
    assert _assert_reference(data, win=3, threshold_v=300e-6) == []


def test_public_detection_preserves_channel_masks_offset_and_edge_padding():
    data = np.zeros((1500, 2))
    data[118, 0] = 107e-6
    data[549, 0] = -198e-6
    data[-1, 1] = 400e-6
    spec = ArtifactRejectionSpec(policy='reject', peak_to_peak_uv=300,
                                 muscle_zscore_threshold=None, pad_s=0.1)
    report = detect_artifact_segments(data, 1000, ('T5', 'P3'), spec, start_sample_offset=2000)
    assert [(s.start_sample, s.end_sample, s.channels) for s in report.segments] == [
        (2000, 2718, ('T5',)), (2900, 3500, ('P3',)),
    ]
    masked = apply_artifact_rejection_mask(data, report.segments, ('T5', 'P3'), start_sample_offset=2000)
    assert np.isnan(masked[:718, 0]).all() and np.isfinite(masked[718:, 0]).all()
    assert np.isfinite(masked[:900, 1]).all() and np.isnan(masked[900:, 1]).all()
    assert np.isfinite(data).all()


def test_detector_version_changes_active_fingerprint_but_not_disabled_policy(monkeypatch):
    import app.scientific.contracts.preprocessing as contracts

    active = ArtifactRejectionSpec(policy='reject')
    disabled = ArtifactRejectionSpec(policy='none')
    current, disabled_current = active.fingerprint(), disabled.fingerprint()
    monkeypatch.setattr(contracts, 'ARTIFACT_DETECTION_VERSION', 'continuous-artifact-report-v2')
    assert active.fingerprint() != current
    assert disabled.fingerprint() == disabled_current
