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
