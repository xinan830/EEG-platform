from types import SimpleNamespace
import numpy as np
import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient
from app.api.recording_signal_preview import router
from app.scientific.primitives.spectral import preprocess_offline


class Source:
    def __init__(self):
        self.selected = None
        self.read_bounds = None
        self.filter_parameters = None

    def require_recording(self, identity):
        return SimpleNamespace(id=identity, sfreq=100, duration_s=20, channels=['Fz-AVG','Pz-AVG'])

    def select_analysis_montage(self, recording, montage):
        self.selected = montage
        return recording

    def prepare_signal_preview(self, recording, low_hz, high_hz, notch_hz, channels):
        if any(channel not in recording.channels for channel in channels):
            raise ValueError('missing channel')
        self.filter_parameters = (low_hz, high_hz, notch_hz, channels)
        time = np.arange(2000) / 100
        data = np.column_stack([np.sin(2*np.pi*10*time),np.sin(2*np.pi*25*time)]) * 1e-6
        values = preprocess_offline(data, 100, low_hz=low_hz, high_hz=high_hz, notch_hz=notch_hz)
        self.prepared = values
        return SimpleNamespace(data=values,sfreq=100,channel_names=recording.channels)


def client():
    app = FastAPI()
    app.state.recording_service = Source()
    app.include_router(router)
    return TestClient(app), app.state.recording_service


def test_signal_preview_http_preserves_units_order_time_and_montage():
    api, source = client()
    response = api.post('/api/recordings/source/signal-preview', json={
        'start_s':2,'window_s':5,'montage_id':'average-snapshot',
        'channels':['Pz-AVG','Fz-AVG'],'max_points':100})
    assert response.status_code == 200
    data = response.json()
    assert data['unit'] == 'uV' and data['filter'] == 'continuous_zero_phase'
    assert data['elapsed_s'][0] == 2
    assert list(data['channels']) == ['Pz-AVG','Fz-AVG']
    assert data['channels']['Pz-AVG'][0] == pytest.approx(source.prepared[200,1]*1e6)
    assert len(data['elapsed_s']) == 100
    assert source.selected == 'average-snapshot'
    assert source.filter_parameters == (1,30,None,['Pz-AVG','Fz-AVG'])


def test_decimated_preview_extrema_preserve_transients_missed_by_uniform_stride():
    api, source = client()
    prepare = source.prepare_signal_preview

    def prepare_with_transients(*args, **kwargs):
        prepared = prepare(*args, **kwargs)
        prepared.data[:] = 0
        prepared.data[223, 0] = 100e-6
        prepared.data[248, 0] = -80e-6
        prepared.data[236, 1] = 75e-6
        prepared.data[249, 1] = -65e-6
        return prepared

    source.prepare_signal_preview = prepare_with_transients
    response = api.post('/api/recordings/source/signal-preview', json={
        'start_s': 2, 'window_s': 5, 'channels': ['Fz-AVG', 'Pz-AVG'], 'max_points': 100,
    })

    assert response.status_code == 200
    data = response.json()
    assert data['display_stride_samples'] == 5
    assert data['channels']['Fz-AVG'][4] == 0
    assert data['channel_extrema']['Fz-AVG']['maximum_uv'][4] == 100
    assert data['channel_extrema']['Fz-AVG']['minimum_uv'][9] == -80
    assert data['channel_extrema']['Pz-AVG']['maximum_uv'][7] == 75
    assert data['channel_extrema']['Pz-AVG']['minimum_uv'][9] == -65
    assert data['extrema_elapsed_s'][4] == pytest.approx(2.22)
    assert len(data['extrema_elapsed_s']) == len(data['elapsed_s']) == 100


def test_preview_omits_extrema_envelope_without_sample_reduction():
    api, _ = client()
    response = api.post('/api/recordings/source/signal-preview', json={
        'start_s': 2, 'window_s': 1, 'channels': ['Fz-AVG'], 'max_points': 100,
    })

    assert response.status_code == 200
    data = response.json()
    assert data['display_stride_samples'] == 1
    assert data['extrema_elapsed_s'] is None
    assert data['channel_extrema'] is None


def test_filter_change_changes_waveform_and_crop_matches_continuously_filtered_signal():
    api, source = client()
    payload = {'start_s':2,'window_s':5,'channels':['Fz-AVG','Pz-AVG'],'max_points':1000}
    wide = api.post('/api/recordings/source/signal-preview',json=payload).json()
    narrow = api.post('/api/recordings/source/signal-preview',json=payload|{'high_hz':15}).json()
    np.testing.assert_allclose(narrow['channels']['Pz-AVG'], source.prepared[200:700,1]*1e6)
    assert np.std(narrow['channels']['Pz-AVG']) < np.std(wide['channels']['Pz-AVG']) / 10
    assert narrow['high_hz'] == 15


def test_preview_resolves_case_without_changing_original_channel_names():
    api, _ = client()
    response = api.post('/api/recordings/source/signal-preview',json={
        'start_s':2,'window_s':5,'channels':['pz-avg','fz-avg']})
    assert response.status_code == 200
    assert list(response.json()['channels']) == ['Pz-AVG','Fz-AVG']


def test_preview_stage_diagnostics_are_opt_in_and_request_correlated(monkeypatch, caplog):
    import logging
    from app.services.recording_signal_preview import signal_preview

    api, source = client()
    caplog.set_level(logging.INFO, logger='brain_platform.signal_preview')
    monkeypatch.setenv('BRAIN_REVIEW_PERF_DIAGNOSTICS', '1')
    signal_preview(source, 'source', start_s=2, window_s=5, montage_id=None,
                   channels=['Fz-AVG'], max_points=100, request_id='preview-request-1')

    diagnostic = next(record.getMessage() for record in caplog.records if 'signal_preview_perf' in record.getMessage())
    assert 'request_id=preview-request-1' in diagnostic
    assert 'recording_id=source' in diagnostic
    assert 'source_preparation_ms=' in diagnostic
    assert 'window_slice_validation_ms=' in diagnostic
    assert 'display_projection_ms=' in diagnostic
    assert 'display_stride_samples=5' in diagnostic


def test_preparation_resource_failure_remains_explicit_http_error():
    from app.algorithm_runtime.errors import DynamicWindowResourceExceededError
    api, source = client()
    def exceeded(*_):
        raise DynamicWindowResourceExceededError('preview allocation budget exceeded')
    source.prepare_signal_preview = exceeded
    response = api.post('/api/recordings/source/signal-preview',json={
        'start_s':2,'window_s':5,'channels':['Fz-AVG']})
    assert response.status_code == 422
    assert 'SIGNAL_PREVIEW_RESOURCE_EXCEEDED' in response.text


@pytest.mark.parametrize('changes',[{'channels':['missing']},{'window_s':31},{'start_s':50},{'channels':['Fz-AVG','Fz-AVG']}])
def test_preview_rejects_invalid_requests(changes):
    api, _ = client()
    payload = {'start_s':0,'window_s':5,'channels':['Fz-AVG']} | changes
    assert api.post('/api/recordings/source/signal-preview',json=payload).status_code == 422
