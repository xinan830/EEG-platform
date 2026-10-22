from types import SimpleNamespace

import numpy as np
import pytest
import time

from app.algorithm_runtime.contracts import AlgorithmInputs
from app.algorithm_runtime.on_demand import DynamicSessionIdentity, DynamicWindowIdentity
from app.algorithm_runtime.on_demand_result_validation import validate_window_result
from app.algorithms.brainbeat.config import BrainbeatConfig
from app.algorithms.brainbeat.runner import BrainbeatAlgorithm
from app.scientific.primitives import SpectralEstimate
from on_demand_fixtures import configure_api, source_hashes


SFREQ = 100.0


class FakeRecording:
    id = "brainbeat-window-test"
    channel_names = ["Fz", "Pz"]
    sfreq_hz = SFREQ
    duration_s = 20.0

    def __init__(self, gate_failed: str | None = None):
        self.gate_failed = gate_failed
        self.calls: list[dict[str, object]] = []
        self.requests = []

    def signal_source_identity(self):
        return "raw-recording:brainbeat-window-test", "source"

    def load_spectral_source(self, request, *, role_to_channel):
        self.requests.append((request, dict(role_to_channel)))
        return self.load_spectrum(
            start_s=request.start_sample / request.expected_sfreq_hz,
            window_s=(request.end_sample - request.start_sample) / request.expected_sfreq_hz,
            channels=[role_to_channel[role] for role in request.roles],
        )

    def load_spectrum(self, *, start_s: float, window_s: float, channels: list[str]):
        self.calls.append({"start_s": start_s, "window_s": window_s, "channels": channels})
        frequencies = np.arange(1.0, 30.25, 0.25)
        fz = np.ones_like(frequencies)
        pz = np.ones_like(frequencies)
        fz[(frequencies >= 4.0) & (frequencies <= 8.0)] = 3.0
        pz[(frequencies >= 8.0) & (frequencies <= 12.0)] = 2.0
        return SpectralEstimate(
            frequencies,
            np.vstack([fz, pz]) * 1e-12,
            1.0 if self.gate_failed is None else 0.0,
            4 if self.gate_failed is None else 0,
            4,
            self.gate_failed,
            ("amplitude_threshold",) if self.gate_failed else (),
            evidence={"fixture": True},
        )


def _config(**overrides) -> BrainbeatConfig:
    values = {
        "channel": "Fz", "secondary_channel": "Pz", "mode": "dynamic",
        "start_s": 0.0, "end_s": 10.0, "window_s": 5.0, "step_s": 1.0,
    }
    values.update(overrides)
    return BrainbeatConfig.model_validate(values)


def _session(algorithm: BrainbeatAlgorithm) -> DynamicSessionIdentity:
    manifest = algorithm.manifest
    return DynamicSessionIdentity(
        recording_id="brainbeat-window-test", source_sha256="source",
        algorithm_id=manifest.algorithm_id, definition_id="definition",
        definition_version=manifest.definition_persistence.version,
        scientific_version=manifest.scientific_version,
        implementation_identity=manifest.implementation_identity,
        configuration_sha256="config", window_policy_version=algorithm.window_policy_version,
    )


def _identity(session: DynamicSessionIdentity, plan) -> DynamicWindowIdentity:
    return DynamicWindowIdentity(
        **session.model_dump(), start_sample=plan.start_sample, end_sample=plan.end_sample,
    )


def _inputs(recording: FakeRecording) -> AlgorithmInputs:
    return AlgorithmInputs(
        recording_id=recording.id, channel="Fz", sfreq_hz=SFREQ,
        duration_s=recording.duration_s, payload=recording,
    )


def test_brainbeat_window_matches_existing_dynamic_point_and_keeps_pair_order():
    algorithm = BrainbeatAlgorithm()
    config = _config()
    recording = FakeRecording()
    inputs = _inputs(recording)
    full = algorithm.execute_dynamic(inputs, config)
    session = _session(algorithm)
    frame = full.windows[0]
    plan = algorithm.plan_window(inputs, config, int(round(frame["end_s"] * SFREQ)), session)
    result = algorithm.execute_window(inputs, config, plan, _identity(session, plan))

    assert result.state == "ready"
    assert result.arrays["brainbeat"][0] == pytest.approx(full.values[0], rel=1e-12, abs=1e-12)
    assert recording.calls[-1]["channels"] == ["Fz", "Pz"]
    request, role_map = recording.requests[-1]
    assert request.roles == ("Fz", "Pz")
    assert role_map == {"Fz": "Fz", "Pz": "Pz"}
    assert request.representation == "spectrum"
    assert result.evidence["extensions"]["brainbeat_evidence"]["fz_channel"] == "Fz"
    assert result.evidence["extensions"]["brainbeat_evidence"]["pz_channel"] == "Pz"
    assert result.provenance["independent_windows"] is True
    assert result.provenance["cross_window_ema"] is False
    validate_window_result(result, algorithm.manifest)


def test_brainbeat_window_quality_failure_is_rejected_and_null():
    algorithm = BrainbeatAlgorithm()
    config = _config()
    recording = FakeRecording(gate_failed="amplitude_threshold")
    inputs = _inputs(recording)
    session = _session(algorithm)
    plan = algorithm.plan_window(inputs, config, 500, session)
    result = algorithm.execute_window(inputs, config, plan, _identity(session, plan))

    assert result.state == "rejected"
    assert result.quality == "gate_failed"
    assert np.isnan(result.arrays["brainbeat"][0])
    assert result.failure["code"] == "PSD_QUALITY_GATE_FAILED"
    validate_window_result(result, algorithm.manifest)


def test_brainbeat_window_checks_cancellation_before_loading():
    algorithm = BrainbeatAlgorithm()
    config = _config()
    inputs = _inputs(FakeRecording())
    session = _session(algorithm)
    plan = algorithm.plan_window(inputs, config, 500, session)
    with pytest.raises(RuntimeError, match="cancelled"):
        algorithm.execute_window(
            inputs, config, plan, _identity(session, plan),
            check_cancelled=lambda: (_ for _ in ()).throw(RuntimeError("cancelled")),
        )


def test_brainbeat_real_recording_api_matches_each_full_dynamic_window(tmp_path):
    client, recording, service, runs, root = configure_api(
        tmp_path, labels=["Fz", "Pz"], duration_s=12.35,
    )
    before = source_hashes(root)
    config = {
        "algorithm_id": "brainbeat", "channel": "Fz-REF", "secondary_channel": "Pz-REF",
        "mode": "dynamic", "time": {"start_s": 0, "end_s": recording.duration_s},
        "dynamic_window_s": 5, "refresh_step_s": 1,
    }
    try:
        submitted = client.post("/api/runs", json={
            "recording_id": recording.id, "analysis_type": "official_algorithm", "config": config,
        })
        assert submitted.status_code == 202, submitted.text
        run = runs.process_next()
        assert run.status.value == "completed", run.error
        series = run.result_summary["metric"]["series"]
        assert series
        for index, frame in enumerate(series):
            ensured = client.post("/api/analysis-windows/ensure", json={
                "recording_id": recording.id, "algorithm_id": "brainbeat", "config": config,
                "cursor_sample": round(frame["window_end_s"] * recording.sfreq),
                "request_id": f"brainbeat-window-{index}",
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
                    pytest.fail("Brainbeat window did not finish")
                time.sleep(0.01)
            assert window["range"]["start_s"] == frame["window_start_s"]
            assert window["range"]["end_s"] == frame["window_end_s"]
            expected_state = "rejected" if frame["failure"] is not None else "ready"
            assert window["state"] == expected_state
            result = window["result"]
            if frame["output"]["value"] is None:
                assert result is None or result["arrays"]["brainbeat"][0] is None
            else:
                np.testing.assert_allclose(
                    result["arrays"]["brainbeat"], [frame["output"]["value"]], rtol=1e-12, atol=1e-12,
                )
            if result is not None:
                assert result["array_units"]["brainbeat"] == "ratio"
                assert result["axes"]["time_s"] == [frame["window_end_s"]]
                assert result["provenance"]["paired_sources"] is True
                assert result["provenance"]["independent_windows"] is True
                assert result["provenance"]["cross_window_ema"] is False
                assert result["provenance"]["iapf_dependency"] is True
                assert result["evidence"]["extensions"]["brainbeat_evidence"] == frame["official"]["brainbeat_evidence"]
            client.delete(f"/api/analysis-windows/subscriptions/{ensured.json()['subscription_id']}")
        assert source_hashes(root) == before
    finally:
        service.close(wait=True)
        client.close()
