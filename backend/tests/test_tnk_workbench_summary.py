from types import SimpleNamespace
import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient
from app.algorithms.tnk.workbench_summary import score_state_summary
from app.api.tnk_workbench import router


def summary(values):
    points = [{"extensions": {"tnk_evidence": {"children": {
        "tnk_composite_meditation": [{"value": value, "actual_range": {"end_s": time}}]
    }}}} for time, value in values]
    return {"structured": {"official": {"tnk_evidence": {"points": points}}}}


def test_state_durations_follow_recording_time_thresholds_and_ignore_unavailable_gaps():
    result = score_state_summary(summary([(4, 54), (5, 55), (6, 65), (7, None), (8, 75), (9, 76)]), 9)
    assert [state['duration_s'] for state in result['states']] == [0, 1, 1, 1]
    assert result['valid_duration_s'] == 3
    assert result['valid_points'] == 5
    assert sum(state['percent'] for state in result['states']) == pytest.approx(100)


def test_cursor_does_not_include_future_or_extrapolate_first_value():
    payload = summary([(4, 10), (5, 20), (6, 80)])
    assert score_state_summary(payload, 3)['valid_points'] == 0
    assert score_state_summary(payload, 4)['valid_duration_s'] == 0
    assert score_state_summary(payload, 5)['valid_duration_s'] == 1
    timeline = score_state_summary(payload, 6)['timeline']
    assert [frame['cursor_s'] for frame in timeline] == [4,5,6]
    assert [frame['valid_duration_s'] for frame in timeline] == [0,1,2]


def test_summary_http_binds_run_identity_and_rejects_non_completed_run():
    app = FastAPI()
    run = SimpleNamespace(config={'algorithm_id':'tnk_biofeedback'},status=SimpleNamespace(value='completed'),
                          recording_id='recording',result_summary=summary([(4,60),(5,70)]))
    app.state.run_service = SimpleNamespace(get=lambda _: run)
    app.include_router(router)
    with TestClient(app) as api:
        response = api.get('/api/runs/run/tnk-workbench?cursor_s=5')
        assert response.status_code == 200
        assert response.json()['run_id'] == 'run'
        assert response.json()['recording_id'] == 'recording'
        run.status.value = 'running'
        assert api.get('/api/runs/run/tnk-workbench?cursor_s=5').status_code == 409
