from types import SimpleNamespace

import numpy as np
import pytest

from app.algorithm_runtime.on_demand import DynamicSessionIdentity, DynamicWindowIdentity
from app.algorithm_runtime.on_demand_result_validation import validate_window_result
from app.algorithms.faa.config import FaaConfig
from app.algorithms.faa.runner import FaaAlgorithm
from app.services.algorithm_recording_context import _RecordingAlgorithmContext
from tests.access_fixtures import array_access_session
from on_demand_fixtures import configure_api, source_hashes
import time


def _context(pair: np.ndarray, sfreq: float) -> _RecordingAlgorithmContext:
    class Source:
        def open_access_session(self, recording, *, analysis_montage_id=None):
            return array_access_session(recording, pair, sfreq, ["F3", "F4"])

        def analysis_signal_provenance(self, _recording):
            return {"signal_mode": "raw", "analysis_reference": "original_recording_no_software_rereference"}

    recording = SimpleNamespace(id="faa-on-demand", channels=["F3", "F4"],
                                sfreq=sfreq, duration_s=len(pair) / sfreq)
    return _RecordingAlgorithmContext(Source(), recording)


def _session(algorithm: FaaAlgorithm) -> DynamicSessionIdentity:
    return DynamicSessionIdentity(
        recording_id="faa-on-demand", source_sha256="source", algorithm_id="faa",
        definition_id="definition", definition_version=algorithm.manifest.definition_persistence.version,
        scientific_version=algorithm.manifest.scientific_version,
        implementation_identity=algorithm.manifest.implementation_identity,
        configuration_sha256="config", window_policy_version=algorithm.window_policy_version,
    )


def test_faa_window_matches_full_dynamic_point_and_keeps_continuous_scope():
    sfreq = 250.0
    time = np.arange(int(60 * sfreq)) / sfreq
    pair = np.column_stack([
        12e-6 * np.sin(2 * np.pi * 10 * time),
        18e-6 * np.sin(2 * np.pi * 10 * time),
    ])
    context = _context(pair, sfreq)
    algorithm = FaaAlgorithm()
    config = FaaConfig(channel="F3", f4_channel="F4", mode="dynamic", start_s=0,
                       end_s=60, window_s=20, step_s=1, low_hz=1, high_hz=50,
                       notch_hz=None)
    try:
        inputs = algorithm.resolve_inputs(context, config)
        full = algorithm.execute_dynamic(inputs, config)
        session = _session(algorithm)
        assert full.values
        for index in (0, 1, 20, 40):
            frame = full.windows[index]
            plan = algorithm.plan_window(inputs, config,
                                         int(round(frame["end_s"] * sfreq)), session)
            identity = DynamicWindowIdentity(**session.model_dump(),
                                             start_sample=plan.start_sample,
                                             end_sample=plan.end_sample)
            result = algorithm.execute_window(inputs, config, plan, identity)
            validate_window_result(result, algorithm.manifest)
            assert result.state == "ready"
            np.testing.assert_allclose(result.arrays["faa"], [full.values[index]],
                                       rtol=1e-12, atol=1e-12)
            assert result.evidence == full.point_evidence[index]
            assert result.provenance["preprocessing_scope"] == "whole_recording_before_window_slice"
            assert result.provenance["paired_sources"] is True
    finally:
        context.close()


def test_faa_window_checks_cancel_before_continuous_preprocessing():
    algorithm = FaaAlgorithm()
    config = FaaConfig(channel="F3", f4_channel="F4", mode="dynamic", start_s=0,
                       end_s=60, window_s=20, step_s=1, low_hz=1, high_hz=50,
                       notch_hz=None)
    context = _context(np.zeros((60 * 250, 2)), 250.0)
    try:
        inputs = algorithm.resolve_inputs(context, config)
        session = _session(algorithm)
        plan = algorithm.plan_window(inputs, config, int(20 * 250), session)
        identity = DynamicWindowIdentity(**session.model_dump(),
                                         start_sample=plan.start_sample,
                                         end_sample=plan.end_sample)
        with pytest.raises(RuntimeError, match="cancelled"):
            algorithm.execute_window(
                inputs, config, plan, identity,
                check_cancelled=lambda: (_ for _ in ()).throw(RuntimeError("cancelled")),
            )
    finally:
        context.close()


@pytest.mark.parametrize("kind", ["artifact", "gap"])
def test_faa_window_quality_rejection_preserves_null_value(kind):
    sfreq = 250.0
    time_axis = np.arange(int(30 * sfreq)) / sfreq
    pair = np.column_stack([
        12e-6 * np.sin(2 * np.pi * 10 * time_axis),
        18e-6 * np.sin(2 * np.pi * 10 * time_axis),
    ])
    if kind == "artifact":
        pair[:, 0] = 2e-3 * np.sin(2 * np.pi * 10 * time_axis)
    else:
        pair[100:110, 0] = np.nan
    context = _context(pair, sfreq)
    algorithm = FaaAlgorithm()
    config = FaaConfig(channel="F3", f4_channel="F4", mode="dynamic", start_s=0,
                       end_s=30, window_s=20, step_s=1, low_hz=1, high_hz=50,
                       notch_hz=None)
    try:
        inputs = algorithm.resolve_inputs(context, config)
        session = _session(algorithm)
        plan = algorithm.plan_window(inputs, config, int(20 * sfreq), session)
        identity = DynamicWindowIdentity(**session.model_dump(),
                                         start_sample=plan.start_sample,
                                         end_sample=plan.end_sample)
        result = algorithm.execute_window(inputs, config, plan, identity)
        assert result.state == "rejected"
        assert result.quality == "gate_failed"
        assert np.isnan(result.arrays["faa"][0])
        assert result.failure["code"] == "FAA_QUALITY_GATE_FAILED"
        assert result.evidence["source_quality"]["clean_segments"] < 10
    finally:
        context.close()


def test_faa_real_recording_api_matches_every_full_dynamic_window(tmp_path):
    client, recording, service, runs, root = configure_api(
        tmp_path, labels=["F3", "F4"], duration_s=42.35,
    )
    before = source_hashes(root)
    config = {
        "algorithm_id": "faa", "channel": "F3-REF", "f4_channel": "F4-REF",
        "mode": "dynamic", "time": {"start_s": 0, "end_s": recording.duration_s},
        "dynamic_window_s": 20, "refresh_step_s": 1,
        "low_hz": 1, "high_hz": 50, "notch_hz": None,
    }
    try:
        submitted = client.post("/api/runs", json={
            "recording_id": recording.id, "analysis_type": "official_algorithm", "config": config,
        })
        assert submitted.status_code == 202, submitted.text
        run = runs.process_next()
        assert run.status.value == "completed", run.error
        series = run.result_summary["metric"]["series"]
        assert len(series) == 23
        for index, frame in enumerate(series):
            ensured = client.post("/api/analysis-windows/ensure", json={
                "recording_id": recording.id, "algorithm_id": "faa", "config": config,
                "cursor_sample": round(frame["window_end_s"] * recording.sfreq),
                "request_id": f"faa-window-{index}",
            })
            assert ensured.status_code in {200, 202}, ensured.text
            window_id = ensured.json()["window_id"]
            deadline = time.monotonic() + 10
            while True:
                window_response = client.get(f"/api/analysis-windows/{window_id}")
                assert window_response.status_code == 200, window_response.text
                window = window_response.json()
                if window["state"] not in {"queued", "preparing_source", "computing_window"}:
                    break
                if time.monotonic() >= deadline:
                    pytest.fail("FAA window did not finish")
                time.sleep(0.01)
            assert window["range"]["start_s"] == frame["window_start_s"]
            assert window["range"]["end_s"] == frame["window_end_s"]
            assert window["quality"] == frame["quality"]["status"]
            assert window["state"] == "ready"
            result = window["result"]
            np.testing.assert_allclose(result["arrays"]["faa"], [frame["output"]["value"]], rtol=1e-12, atol=1e-12)
            assert result["array_units"]["faa"] == "dimensionless"
            assert result["axes"]["time_s"] == [frame["window_end_s"]]
            assert result["evidence"]["source_quality"] == frame["official"]["source_quality"]
            assert result["evidence"]["calculation_trace"] == frame["official"]["calculation_trace"]
            assert result["evidence"]["extensions"]["faa_evidence"] == frame["official"]["faa_evidence"]
            assert result["provenance"]["preprocessing_scope"] == "whole_recording_before_window_slice"
            assert result["provenance"]["paired_sources"] is True
            client.delete(f"/api/analysis-windows/subscriptions/{ensured.json()['subscription_id']}")
        assert source_hashes(root) == before
    finally:
        service.close(wait=True)
        client.close()
