"""Contract and integration checks for offline preprocessing ownership."""

import numpy as np
import pytest
from pathlib import Path

from app.bootstrap import build_builtin_registry
from app.models.analysis_config import OfflinePreprocessingRequest
from app.scientific.primitives import detrend_and_filter_for_inspection


def test_only_offline_facade_constructs_or_calls_the_continuous_executor():
    services = Path(__file__).resolve().parents[1] / "app" / "services"
    for path in services.rglob("*.py"):
        if path.name in {"continuous_preprocessing.py", "offline_scientific_preprocessing.py"}:
            continue
        source = path.read_text(encoding="utf-8")
        assert "ContinuousPreprocessingService" not in source, path
        assert "from app.services.preprocessing.artifact_inspection import detrend_and_filter_for_inspection" not in source, path
        assert "from app.services.preprocessing.artifact_inspection import detrend_and_filter_for_inspection" not in source, path
    for name in ("tnk_window_preprocessing.py", "spectrogram_source.py"):
        matches = list(services.rglob(name))
        assert len(matches) == 1, name
        source = matches[0].read_text(encoding="utf-8")
        assert "return prepare_window(" not in source
        assert "= prepare_window(" not in source


def test_shared_inspection_helper_preserves_gaps_input_and_frequency_response():
    sfreq = 250
    times = np.arange(3000) / sfreq
    samples = (12e-6 * np.sin(2*np.pi*10*times) + 7e-6 * np.sin(2*np.pi*50*times))[:, None]
    samples[1000:1050] = np.nan
    original = samples.copy()
    band, broad = detrend_and_filter_for_inspection(samples, sfreq)
    np.testing.assert_array_equal(samples, original)
    assert np.array_equal(np.isnan(samples), np.isnan(band))
    assert np.array_equal(np.isnan(samples), np.isnan(broad))
    segment = slice(200, 800)
    alpha = 2*np.abs(np.mean(band[segment, 0]*np.exp(-2j*np.pi*10*times[segment])))
    mains = 2*np.abs(np.mean(broad[segment, 0]*np.exp(-2j*np.pi*50*times[segment])))
    assert alpha > 10e-6
    assert mains < 0.2e-6
    short = samples[:20]
    first, second = detrend_and_filter_for_inspection(short, sfreq)
    np.testing.assert_array_equal(first, short)
    np.testing.assert_array_equal(second, short)
    assert not np.shares_memory(first, short)


@pytest.mark.parametrize("algorithm_id,supported", [("psd", True), ("rbp", True),
    ("faa", True), ("stft", False), ("tnk_biofeedback", False)])
def test_registry_owns_artifact_removal_support_and_validation(algorithm_id, supported):
    requirements = build_builtin_registry().get_current(algorithm_id).manifest.signal_requirements
    assert requirements.supports_artifact_removal is supported
    request = OfflinePreprocessingRequest.model_validate({"artifact_removal": {"method": "ssp_eog"}})
    if supported:
        request.validate_capabilities(requirements)
    else:
        with pytest.raises(ValueError, match="ARTIFACT_REMOVAL_REQUIRES_CONTINUOUS_SCOPE"):
            request.validate_capabilities(requirements)
    OfflinePreprocessingRequest().validate_capabilities(requirements)


def test_catalogue_exposes_the_registry_capability_contract(tmp_path):
    from test_recording_import import _client
    client, _, _ = _client(tmp_path)
    response = client.get("/api/algorithms")
    assert response.status_code == 200
    catalogue = {item["id"]: item for item in response.json()["algorithms"]}
    assert catalogue["psd"]["signal_requirements"]["supports_artifact_removal"] is True
    assert catalogue["stft"]["signal_requirements"]["supports_artifact_removal"] is False


def test_official_entry_points_reject_unsupported_removal_before_computing(tmp_path):
    from app.models.run import RunCreateRequest
    from app.algorithm_runtime.on_demand import DynamicWindowRequest
    from app.services.analysis.windows.on_demand_dynamic import DynamicWindowService
    from app.services.analysis.runs.runs import RunService
    from test_recording_import import _client
    from test_offline_bad_channels import _write_multi_channel_edf

    from app.services.preprocessing.offline_scientific_preprocessing import OfflineScientificPreprocessingService
    _, recordings, _ = _client(tmp_path)
    values = np.random.default_rng(9).normal(0, 10, (3000, 2))
    payload = _write_multi_channel_edf(tmp_path / "capability.edf", ("F3", "O1"), values, sfreq=200)
    recording = recordings.create_imported_recording("capability.edf", ".edf", payload)
    offline_prep = OfflineScientificPreprocessingService.for_recordings(recordings)
    prepared = offline_prep.prepare_continuous(
        recording, low_hz=1, high_hz=50, notch_hz=50)
    repeated = offline_prep.prepare_continuous(
        recording, low_hz=1, high_hz=50, notch_hz=50)
    assert repeated is prepared
    assert not prepared.data.flags.writeable
    with pytest.raises(ValueError):
        prepared.data[0, 0] = 1.0
    runs = RunService(recordings, recordings.database_path, tmp_path / "artifacts", algorithm_registry=build_builtin_registry())
    dynamic = DynamicWindowService(recordings, runs.algorithm_runtime_registry,
        database_path=recordings.database_path, artifacts_dir=tmp_path / "artifacts")
    config = {"algorithm_id": "stft", "mode": "dynamic", "channel": "F3",
        "time": {"start_s": 0, "end_s": 15}, "dynamic_window_s": 5, "refresh_step_s": 1,
        "preprocessing": {"artifact_removal": {"method": "ssp_eog"}}}
    try:
        with pytest.raises(ValueError, match="ARTIFACT_REMOVAL_REQUIRES_CONTINUOUS_SCOPE"):
            runs._resolve_request(RunCreateRequest(recording_id=recording.id,
                analysis_type="official_algorithm", config=config), recording)
        with pytest.raises(ValueError, match="ARTIFACT_REMOVAL_REQUIRES_CONTINUOUS_SCOPE"):
            dynamic._planner.prepare(DynamicWindowRequest(recording_id=recording.id,
                algorithm_id="stft", config=config, cursor_sample=2000))
    finally:
        dynamic.close()
