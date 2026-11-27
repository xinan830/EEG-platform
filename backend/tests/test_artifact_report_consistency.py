"""Inspection, masks and bounded gates must consume one recording-level report."""

from dataclasses import replace
from pathlib import Path

import numpy as np
import pytest

from app.scientific.artifact_segments import apply_artifact_rejection_mask
from app.scientific.contracts.preprocessing import ArtifactRejectionSpec
from app.scientific.quality import SpectralQualityGateError
from app.services.preprocessing.artifact_inspection import inspect_recording_artifact_segments
from test_offline_bad_channels import _write_multi_channel_edf
from test_recording_import import _client


def _recording(tmp_path: Path):
    client, recordings, _ = _client(tmp_path)
    sfreq = 500
    time = np.arange(6000) / sfreq
    values = np.random.default_rng(781).normal(0, 6, (len(time), 2))
    values += (18 * np.sin(2 * np.pi * 10 * time))[:, None]
    values[5200:5500, 0] += 240 * np.sin(2 * np.pi * 6 * time[5200:5500])
    values[:, 1] += 1400 * np.sin(2 * np.pi * 0.65 * time)
    source = _write_multi_channel_edf(tmp_path / 'artifact-report.edf', ('F3', 'O1'), values, sfreq=sfreq)
    response = client.post('/api/recordings/import', files={'file': ('artifact-report.edf', source)})
    response.raise_for_status()
    return client, recordings, recordings.require_recording(response.json()['id'])


def test_inspection_default_report_matches_exact_continuous_mask(tmp_path):
    client, recordings, recording = _recording(tmp_path)
    scan = client.get(f'/api/recordings/{recording.id}/preprocessing/artifact-segments',
                      params={'duration_s': 12}).json()
    prepared = client.app.state.offline_scientific_preprocessing_service.prepare_continuous(
        recording, low_hz=1, high_hz=50, notch_hz=50,
        artifact_rejection_spec=ArtifactRejectionSpec(policy='reject'),
    )
    assert scan['segments'] == [seg.to_dict(prepared.sfreq) for seg in prepared.artifact_intervals]
    expected = apply_artifact_rejection_mask(np.zeros_like(prepared.data), prepared.artifact_intervals, prepared.channel_names)
    np.testing.assert_array_equal(np.isnan(prepared.data), np.isnan(expected))


def test_scan_annotate_reject_reuse_one_detection_and_range_projection(tmp_path, monkeypatch):
    _, recordings, recording = _recording(tmp_path)
    import app.scientific.artifact_segments as detector
    original = detector.detect_artifact_segments
    calls = []

    def counting(*args, **kwargs):
        calls.append(1)
        return original(*args, **kwargs)

    monkeypatch.setattr(detector, 'detect_artifact_segments', counting)
    spec = ArtifactRejectionSpec(policy='annotate')
    from app.services.preprocessing.offline_scientific_preprocessing import OfflineScientificPreprocessingService
    prep = OfflineScientificPreprocessingService.for_recordings(recordings)
    annotated = prep.prepare_continuous(recording, low_hz=1, high_hz=50, notch_hz=50, artifact_rejection_spec=spec)
    rejected = prep.prepare_continuous(recording, low_hz=1, high_hz=50, notch_hz=50, artifact_rejection_spec=replace(spec, policy='reject'))
    scan = inspect_recording_artifact_segments(recordings, recording, start_s=10.5, duration_s=1, artifact_spec=spec, offline_preprocessing=prep)
    assert len(calls) == 1
    assert rejected.artifact_report is annotated.artifact_report
    assert scan['detection_scope']['end_sample'] == 6000
    assert scan['start_sample'] == 5250 and scan['end_sample'] == 5750
    for seg in scan['segments']:
        assert 5250 <= seg['start_sample'] < seg['end_sample'] <= 5750
        for channel in seg['channels'] or annotated.channel_names:
            assert np.isnan(rejected.data[seg['start_sample']:seg['end_sample'], annotated.channel_names.index(channel)]).all()
    filtered = inspect_recording_artifact_segments(recordings, recording, duration_s=12, artifact_spec=spec, high_hz=45, offline_preprocessing=prep)
    assert len(calls) == 2
    assert filtered['detection_scope']['prepared_source_identity'] != scan['detection_scope']['prepared_source_identity']


def test_bounded_gate_projects_authoritative_report_without_redetection(tmp_path, monkeypatch):
    _, recordings, recording = _recording(tmp_path)
    from app.services.preprocessing.offline_scientific_preprocessing import OfflineScientificPreprocessingService
    prep = OfflineScientificPreprocessingService.for_recordings(recordings)
    art = ArtifactRejectionSpec(policy='annotate')
    annotated = prep.prepare_continuous(recording, low_hz=1, high_hz=50, notch_hz=50, artifact_rejection_spec=art)
    def unexpected(*args, **kwargs):
        pytest.fail('a bounded window must not independently redetect artifacts')
    monkeypatch.setattr('app.scientific.artifact_segments.detect_artifact_segments', unexpected)
    with recordings.open_access_session(recording) as session:
        kwargs = dict(start_sample=5000, end_sample=5900, channels=['F3'], low_hz=1, high_hz=50,
                      notch_hz=50, context_s=2, algorithm_version='test', recording=recording)
        window = prep.prepare_bounded_window(session, **kwargs, artifact_rejection_spec=art)
        from app.scientific.artifact_reports import project_artifact_report
        expected = project_artifact_report(annotated.artifact_report, 5000, 5900, ['F3']).to_dict()
        assert window.evidence['artifact_rejection']['segments'] == expected['segments']
        assert window.evidence['artifact_rejection']['detection_scope']['end_sample'] == 6000
        with pytest.raises(SpectralQualityGateError) as exc:
            prep.prepare_bounded_window(session, **kwargs, artifact_rejection_spec=replace(art, policy='reject'))
        assert exc.value.quality['gate_failed'] == 'artifact_segment_rejected'


def test_report_projection_clips_channel_intervals_without_cross_channel_union():
    from app.scientific.artifact_reports import project_artifact_report
    from app.scientific.artifact_segments import ArtifactSegment, ArtifactSegmentReport
    report = ArtifactSegmentReport(1000, 1000, 0, (
        ArtifactSegment(100, 200, 'manual', ('F3',)),
        ArtifactSegment(150, 250, 'manual', ('O1',)),
    ), 150, 0.85, None, 'disabled')
    projected = project_artifact_report(report, 125, 225)
    assert [(s.start_sample, s.end_sample, s.channels) for s in projected.segments] == [
        (125, 200, ('F3',)), (150, 225, ('O1',)),
    ]
    single = project_artifact_report(report, 200, 225, ['F3'])
    assert single.segments == () and single.clean_ratio == 1
    assert projected.rejected_sample_count == 100
    assert report.segments[0].start_sample == 100


def test_bounded_qc_rejects_missing_and_wrong_recording_context(tmp_path):
    _, recordings, recording = _recording(tmp_path)
    with recordings.open_access_session(recording) as session:
        kwargs = dict(start_sample=1000, end_sample=3000, channels=['F3'], low_hz=1, high_hz=50,
                      notch_hz=50, context_s=2, algorithm_version='test',
                      artifact_rejection_spec=ArtifactRejectionSpec(policy='annotate'))
        from app.services.preprocessing.offline_scientific_preprocessing import OfflineScientificPreprocessingService
        prep = OfflineScientificPreprocessingService.for_recordings(recordings)
        with pytest.raises(ValueError, match='ARTIFACT_QC_RECORDING_REQUIRED'):
            prep.prepare_bounded_window(session, **kwargs)
        with pytest.raises(ValueError, match='ARTIFACT_REPORT_SOURCE_MISMATCH'):
            prep.prepare_bounded_window(session, **kwargs, recording=replace(recording, id='other'))


def test_api_exposes_effective_filters_and_validates_nyquist(tmp_path):
    client, _, recording = _recording(tmp_path)
    url = f'/api/recordings/{recording.id}/preprocessing/artifact-segments'
    response = client.get(url, params={'start_s': 0.0019, 'duration_s': 1, 'low_hz': .5, 'high_hz': 45,
                                      'notch_hz': 0, 'notch_q': 5})
    response.raise_for_status()
    payload = response.json()
    assert payload['start_sample'] == 0
    assert payload['inspection_filter'] == {'low_hz': .5, 'high_hz': 45, 'notch_hz': None, 'notch_q': 5}
    assert payload['detection_scope']['end_sample'] == 6000
    invalid = client.get(url, params={'high_hz': 250})
    assert invalid.status_code == 422


def test_algorithm_identity_versions_active_artifact_detection_only():
    from app.core.provenance import with_offline_preprocessing
    from app.scientific.contracts.preprocessing import ARTIFACT_DETECTION_VERSION
    filters = {'low_hz': 1, 'high_hz': 50}
    assert with_offline_preprocessing(filters, {}) is filters
    disabled = with_offline_preprocessing(filters, {'preprocessing': {}})
    assert 'artifact_detection_version' not in disabled
    active = with_offline_preprocessing(filters, {'preprocessing': {'artifact_rejection': {'policy': 'reject'}}})
    assert active['artifact_detection_version'] == ARTIFACT_DETECTION_VERSION


def test_reject_preparation_notifies_once_and_preserves_full_detection_population(tmp_path):
    from types import SimpleNamespace
    _, recordings, recording = _recording(tmp_path)
    notified = []
    control = SimpleNamespace(check_cancelled=lambda: None, select_requested_channels=True,
                              source_preparation_completed=lambda: notified.append(1))
    from app.services.preprocessing.offline_scientific_preprocessing import OfflineScientificPreprocessingService
    prepared = OfflineScientificPreprocessingService.for_recordings(recordings).prepare_continuous(
        recording, low_hz=1, high_hz=50, notch_hz=50, channels=['F3'],
        requested_channels_only=True, execution_context=control,
        artifact_rejection_spec=ArtifactRejectionSpec(policy='reject'),
    )
    assert notified == [1]
    assert prepared.channel_names == ('F3', 'O1')


def test_scientific_preview_exposes_null_gaps_for_listed_channel_segments(tmp_path):
    client, recordings, recording = _recording(tmp_path)
    scan = inspect_recording_artifact_segments(recordings, recording, duration_s=12)
    response = client.post(f'/api/recordings/{recording.id}/preprocessing/preview-window', json={
        'start_s': 0, 'window_s': 12, 'low_hz': 1, 'high_hz': 50, 'notch_hz': 50,
        'preprocessing': {'artifact_rejection': {'policy': 'reject'}},
    })
    response.raise_for_status()
    payload = response.json()
    proc = payload['processed']
    assert scan['segments']
    for seg in scan['segments']:
        for channel in seg['channels']:
            assert any(value is None and seg['start_s'] <= time < seg['end_s']
                       for time, value in zip(proc['trace_times_s'][channel], proc['traces_uv'][channel]))


def test_concurrent_annotate_and_reject_share_single_flight_report(tmp_path, monkeypatch):
    from concurrent.futures import ThreadPoolExecutor
    from threading import Event
    import app.scientific.artifact_segments as detector
    _, recordings, recording = _recording(tmp_path)
    entered, release = Event(), Event()
    original, calls = detector.detect_artifact_segments, []

    def detect(*args, **kwargs):
        calls.append(1)
        entered.set()
        assert release.wait(5)
        return original(*args, **kwargs)

    monkeypatch.setattr(detector, 'detect_artifact_segments', detect)
    from app.services.preprocessing.offline_scientific_preprocessing import OfflineScientificPreprocessingService
    prep = OfflineScientificPreprocessingService.for_recordings(recordings)
    def prepare(policy):
        return prep.prepare_continuous(
            recording, low_hz=1, high_hz=50, notch_hz=50,
            artifact_rejection_spec=ArtifactRejectionSpec(policy=policy),
        )
    with ThreadPoolExecutor(max_workers=2) as pool:
        annotated = pool.submit(prepare, 'annotate')
        try:
            assert entered.wait(5)
            rejected = pool.submit(prepare, 'reject')
        finally:
            release.set()
        assert annotated.result(timeout=10).artifact_report is rejected.result(timeout=10).artifact_report
    assert calls == [1]


def test_global_qc_respects_resource_budget_instead_of_shrinking_detection_channels(tmp_path):
    from types import SimpleNamespace
    from app.algorithm_runtime.errors import DynamicWindowResourceExceededError
    from app.services.analysis.windows.dynamic_window_resources import DynamicWindowResourcePolicy
    from app.services.preprocessing.offline_scientific_preprocessing import OfflineScientificPreprocessingService
    _, recordings, recording = _recording(tmp_path)
    control = SimpleNamespace(check_cancelled=lambda: None, select_requested_channels=True,
        preparation_resource_policy=DynamicWindowResourcePolicy(max_preparation_bytes=1))
    with pytest.raises(DynamicWindowResourceExceededError):
        OfflineScientificPreprocessingService.for_recordings(recordings).prepare_continuous(
            recording, low_hz=1, high_hz=50, notch_hz=50, channels=['F3'],
            requested_channels_only=True, execution_context=control,
            artifact_rejection_spec=ArtifactRejectionSpec(policy='reject'),
        )
