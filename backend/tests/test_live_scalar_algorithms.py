"""Tests for live scalar algorithms (IAPF, Theta/Beta, Classic BrainBeat), preprocessing boundary, and 6-card overload."""

from __future__ import annotations

import logging
import math
from threading import Event
import time

from fastapi.testclient import TestClient
import numpy as np
import pytest

from app.main import app
from app.scientific.contracts.preprocessing import live_spectral_filter_spec
from app.scientific.primitives import DEFAULT_SPECTRAL_GATEWAY
from app.algorithms.iapf.compute import compute_iapf
from app.algorithms.theta_beta.compute import compute_theta_beta
from app.algorithms.brainbeat.compute import compute_brainbeat
from app.services.live_analysis.live_analysis_contracts import LiveAnalysisAlgorithmRegistry
from app.services.live_analysis.live_analysis_coordinator import LiveAnalysisCoordinator
from app.services.live_analysis.live_rbp_algorithm import LiveRbpAlgorithm
from app.services.live_analysis.live_recording_access import (
    LiveRecordingAccessError,
    LiveRecordingAccessSession,
    LiveSourceIdentity,
)
from app.services.live_analysis.live_scalar_algorithms import LiveScalarAlgorithm
from app.services.live_analysis.live_scientific_preprocessing import LiveScientificPreprocessingService
from app.services.live_analysis.live_spectral_algorithms import LiveSpectrumAlgorithm
from app.services.live_analysis.live_topomap_algorithm import LiveTopomapAlgorithm
from app.services.recordings.sample_access_contract import SampleBlock, UnavailableSampleInterval


def _synthetic_eeg(
    duration_s: float = 20.0,
    sfreq_hz: float = 250.0,
    channels: tuple[str, ...] = ("Fz", "Pz"),
) -> np.ndarray:
    n_samples = int(round(duration_s * sfreq_hz))
    t = np.arange(n_samples, dtype=np.float64) / sfreq_hz
    rng = np.random.default_rng(42)
    cols = []
    for ch in channels:
        # Realistic microvolt-scale EEG in Volts (15-25 uV oscillations)
        if ch.casefold() == "fz":
            sig = 22e-6 * np.sin(2 * math.pi * 6.0 * t) + 12e-6 * np.sin(2 * math.pi * 10.0 * t) + 6e-6 * np.sin(2 * math.pi * 20.0 * t)
        else:
            sig = 8e-6 * np.sin(2 * math.pi * 6.0 * t) + 25e-6 * np.sin(2 * math.pi * 10.0 * t) + 7e-6 * np.sin(2 * math.pi * 20.0 * t)
        cols.append(sig + 1.5e-6 * rng.standard_normal(n_samples))
    return np.column_stack(cols)


def _session(sfreq_hz: float = 250.0, capacity_s: float = 40.0, channels: tuple[str, ...] = ("Fz", "Pz")) -> LiveRecordingAccessSession:
    return LiveRecordingAccessSession(
        LiveSourceIdentity("live-scalar-session", "stream-1", "schema-1"),
        sfreq_hz,
        channels,
        tuple("V" for _ in channels),
        int(round(sfreq_hz * capacity_s)),
    )


def test_live_scalar_config_validation() -> None:
    session = _session()
    iapf = LiveScalarAlgorithm("iapf")
    theta_beta = LiveScalarAlgorithm("theta_beta")
    brainbeat = LiveScalarAlgorithm("brainbeat")

    with pytest.raises(LiveRecordingAccessError, match="20"):
        iapf.configure(session, {"channels": ["Fz"], "window_s": 10.0})
    with pytest.raises(LiveRecordingAccessError, match="不支持自定义频段模式"):
        iapf.configure(session, {"channels": ["Fz"], "window_s": 20.0, "frequency_mode": "individualized"})
    with pytest.raises(LiveRecordingAccessError, match="需要 2 个通道"):
        brainbeat.configure(session, {"channels": ["Fz"], "window_s": 20.0})
    with pytest.raises(ValueError, match="非空且唯一"):
        brainbeat.configure(session, {"channels": ["Fz", "fz"], "window_s": 20.0})
    with pytest.raises(LiveRecordingAccessError, match="通道不存在"):
        theta_beta.configure(session, {"channels": ["O1"], "window_s": 20.0})

    bipolar_session = _session(channels=("Fz-Cz", "Pz-Oz"))
    with pytest.raises(LiveRecordingAccessError, match="Fz / Pz 角色"):
        brainbeat.configure(bipolar_session, {"channels": ["Fz-Cz", "Pz-Oz"], "window_s": 20.0})
    reference_session = _session(channels=("Fz-REF", "Pz-AVG"))
    ref_config, _ = brainbeat.configure(reference_session, {"channels": ["Fz-REF", "Pz-AVG"], "window_s": 20.0})
    assert ref_config.channels == ["Fz-REF", "Pz-AVG"]

    _, plan = brainbeat.configure(session, {"channels": ["fz", "pz"], "window_s": 20.0})
    assert plan.channels == ("Fz", "Pz")
    assert plan.window_samples == 5000


@pytest.mark.parametrize("kind,negatives,accepted", [
    (0, (), True),
    (2, ("Fz", "Pz", "Cz"), True),
    (1, ("Cz",), False),
    (3, ("Fz", "Pz"), False),
    (0, ("Cz",), False),
    (2, ("Cz",), False),
    (2, (), False),
    (None, (), False),
    (False, (), False),
    ("2", ("Fz", "Pz"), False),
])
@pytest.mark.parametrize("invalid_role", ["Fz", "Pz"])
def test_brainbeat_validates_montage_formula_despite_supported_name(kind, negatives, accepted, invalid_role) -> None:
    session = _session(channels=("Fz", "Pz", "Cz"))
    rows = [
        {"Name": f"{role}-AVG", "PositiveLabel": role,
         "NegativeKind": kind if role == invalid_role else 0,
         "NegativeLabels": list(negatives) if role == invalid_role else [],
         "DisplayOrder": index}
        for index, role in enumerate(("Fz", "Pz"))
    ]
    if kind is None:
        rows[0 if invalid_role == "Fz" else 1].pop("NegativeKind")
    session.set_montage({"DerivedChannels": rows}, 1, 0)
    algorithm = LiveScalarAlgorithm("brainbeat")
    config = {"channels": ["Fz-AVG", "Pz-AVG"]}
    if accepted:
        normalized, _ = algorithm.configure(session, config)
        assert normalized.channels == config["channels"]
    else:
        with pytest.raises(LiveRecordingAccessError) as error:
            algorithm.configure(session, config)
        assert error.value.code == "LIVE_CONFIG_INVALID"


def test_brainbeat_rejects_montage_positive_electrode_mismatch() -> None:
    session = _session(channels=("Fz", "Pz", "Cz"))
    session.set_montage({"DerivedChannels": [
        {"Name": "Fz-REF", "PositiveLabel": "Cz", "NegativeKind": 0, "NegativeLabels": [], "DisplayOrder": 0},
        {"Name": "Pz-REF", "PositiveLabel": "Pz", "NegativeKind": 0, "NegativeLabels": [], "DisplayOrder": 1},
    ]}, 1, 0)
    with pytest.raises(LiveRecordingAccessError) as error:
        LiveScalarAlgorithm("brainbeat").configure(session, {"channels": ["Fz-REF", "Pz-REF"]})
    assert error.value.code == "LIVE_CONFIG_INVALID"


def test_live_scalar_algorithms_compute_and_skip_iapf_segment_diagnostics(caplog: pytest.LogCaptureFixture) -> None:
    caplog.set_level(logging.INFO, logger="brain_platform.iapf")
    preprocessor = LiveScientificPreprocessingService()
    session = _session()
    values = _synthetic_eeg(20.0, 250.0, ("Fz", "Pz"))
    session.append(0, values, values.shape[0])

    iapf = LiveScalarAlgorithm("iapf", preprocessor=preprocessor)
    tb = LiveScalarAlgorithm("theta_beta", preprocessor=preprocessor)
    bb = LiveScalarAlgorithm("brainbeat", preprocessor=preprocessor)

    iapf_cfg, _ = iapf.configure(session, {"channels": ["Pz"], "window_s": 20.0})
    tb_std_cfg, _ = tb.configure(session, {"channels": ["Fz"], "window_s": 20.0, "frequency_mode": "standard"})
    tb_ind_cfg, _ = tb.configure(session, {"channels": ["Pz"], "window_s": 20.0, "frequency_mode": "individualized"})
    bb_cfg, _ = bb.configure(session, {"channels": ["Fz", "Pz"], "window_s": 20.0})

    pz_block = session.read_samples(0, 5000, ("Pz",))
    fz_block = session.read_samples(0, 5000, ("Fz",))
    bb_block = session.read_samples(0, 5000, ("Fz", "Pz"))

    iapf_res = iapf.calculate("live-scalar-session", pz_block, iapf_cfg, 0)
    assert iapf_res.state == "ready"
    assert iapf_res.unit == "Hz"
    assert 9.5 <= (iapf_res.value or 0.0) <= 10.5
    assert iapf_res.provenance["preprocessing_scope"] == "window_local_zero_phase"
    assert iapf_res.provenance["equivalent_to_offline_continuous_preprocessing"] is False
    assert "iapf_welch_segment_diagnostics" not in caplog.text

    tb_std_res = tb.calculate("live-scalar-session", fz_block, tb_std_cfg, 0)
    assert tb_std_res.state == "ready"
    assert tb_std_res.unit == "dimensionless"
    assert (tb_std_res.value or 0.0) > 1.0

    tb_ind_res = tb.calculate("live-scalar-session", pz_block, tb_ind_cfg, 0)
    assert tb_ind_res.state == "ready"
    assert tb_ind_res.evidence["extensions"]["theta_beta_evidence"]["frequency_mode"] == "individualized"
    assert tb_ind_res.evidence["extensions"]["theta_beta_evidence"]["iapf_hz"] is not None

    bb_res = bb.calculate("live-scalar-session", bb_block, bb_cfg, 0)
    assert bb_res.state == "ready"
    assert bb_res.unit == "ratio"
    assert (bb_res.value or 0.0) > 0.0
    assert bb_res.provenance["paired_role_order"] == ["Fz", "Pz"]


def test_live_scalar_quality_gates_and_gap_handling() -> None:
    preprocessor = LiveScientificPreprocessingService()
    session = _session()
    tb = LiveScalarAlgorithm("theta_beta", preprocessor=preprocessor)
    iapf = LiveScalarAlgorithm("iapf", preprocessor=preprocessor)
    bb = LiveScalarAlgorithm("brainbeat", preprocessor=preprocessor)

    tb_ind_cfg, _ = tb.configure(session, {"channels": ["Fz"], "window_s": 20.0, "frequency_mode": "individualized"})
    iapf_cfg, _ = iapf.configure(session, {"channels": ["Fz"], "window_s": 20.0})
    bb_cfg, _ = bb.configure(session, {"channels": ["Fz", "Pz"], "window_s": 20.0})

    # 1. Flatline / artifact-free-check failure -> PSD_QUALITY_GATE_FAILED
    flat_block = SampleBlock(
        source_identity=session.identity,
        start_sample=0, end_sample=5000, sampling_rate_hz=250.0,
        channel_names=("Fz",), channel_units=("V",),
        values_v=np.zeros((5000, 1), dtype=np.float64),
        unavailable_intervals=(),
    )
    assert iapf.calculate("live-scalar-session", flat_block, iapf_cfg, 0).failure["code"] == "PSD_QUALITY_GATE_FAILED"
    assert tb.calculate("live-scalar-session", flat_block, tb_ind_cfg, 0).failure["code"] == "PSD_QUALITY_GATE_FAILED"

    # 2. Clean 5 Hz + 20 Hz signal (no alpha peak) -> passes PSD quality gate, individualized Theta/Beta returns IAPF_UNAVAILABLE
    t = np.arange(5000, dtype=np.float64) / 250.0
    no_alpha = (20e-6 * np.sin(2 * math.pi * 5.0 * t) + 8e-6 * np.sin(2 * math.pi * 20.0 * t)).reshape(-1, 1)
    no_alpha_block = SampleBlock(
        source_identity=session.identity,
        start_sample=5000, end_sample=10000, sampling_rate_hz=250.0,
        channel_names=("Fz",), channel_units=("V",),
        values_v=no_alpha,
        unavailable_intervals=(),
    )
    no_alpha_tb = tb.calculate("live-scalar-session", no_alpha_block, tb_ind_cfg, 0)
    assert no_alpha_tb.state == "unavailable"
    assert no_alpha_tb.failure is not None
    assert no_alpha_tb.failure["code"] == "IAPF_UNAVAILABLE"

    # 3. Sample gap -> LIVE_SCALAR_WINDOW_UNAVAILABLE
    gap_block = SampleBlock(
        source_identity=session.identity,
        start_sample=0, end_sample=5000, sampling_rate_hz=250.0,
        channel_names=("Fz", "Pz"), channel_units=("V", "V"),
        values_v=np.ones((5000, 2), dtype=np.float64) * 1e-5,
        unavailable_intervals=(UnavailableSampleInterval(100, 110, "source_gap", ("Fz",)),),
    )
    gap_bb = bb.calculate("live-scalar-session", gap_block, bb_cfg, 0)
    assert gap_bb.state == "unavailable"
    assert gap_bb.failure["code"] == "LIVE_SCALAR_WINDOW_UNAVAILABLE"


def test_live_window_local_vs_offline_continuous_preprocessing_agreement() -> None:
    sfreq = 250.0
    continuous = _synthetic_eeg(60.0, sfreq, ("Fz", "Pz"))
    spec = live_spectral_filter_spec(50.0)
    offline_filtered = DEFAULT_SPECTRAL_GATEWAY.preprocess(
        continuous, sfreq, low_hz=spec.low_hz, high_hz=spec.high_hz, notch_hz=spec.notch_hz, notch_q=spec.notch_q,
    )
    start_idx, end_idx = int(20.0 * sfreq), int(40.0 * sfreq)
    offline_slice = offline_filtered[start_idx:end_idx]

    off_spec_30 = DEFAULT_SPECTRAL_GATEWAY.welch(offline_slice[:, 1:2], sfreq, low_hz=1.0, high_hz=30.0)
    off_spec_50 = DEFAULT_SPECTRAL_GATEWAY.welch(offline_slice[:, 0:1], sfreq, low_hz=1.0, high_hz=50.0)
    off_spec_bb = DEFAULT_SPECTRAL_GATEWAY.welch(offline_slice, sfreq, low_hz=1.0, high_hz=30.0)
    off_iapf = compute_iapf(off_spec_30, channel="Pz", requested_range={"start_s": 20.0, "end_s": 40.0}).value
    off_tb = compute_theta_beta(off_spec_50, channel="Fz", requested_range={"start_s": 20.0, "end_s": 40.0}).value
    off_bb = compute_brainbeat(off_spec_bb, fz_channel="Fz", pz_channel="Pz", requested_range={"start_s": 20.0, "end_s": 40.0}).value

    session = _session()
    preprocessor = LiveScientificPreprocessingService()
    pz_block = SampleBlock(session.identity, start_idx, end_idx, sfreq, ("Pz",), ("V",), continuous[start_idx:end_idx, 1:2], ())
    fz_block = SampleBlock(session.identity, start_idx, end_idx, sfreq, ("Fz",), ("V",), continuous[start_idx:end_idx, 0:1], ())
    bb_block = SampleBlock(session.identity, start_idx, end_idx, sfreq, ("Fz", "Pz"), ("V", "V"), continuous[start_idx:end_idx], ())

    iapf_alg = LiveScalarAlgorithm("iapf", preprocessor=preprocessor)
    tb_alg = LiveScalarAlgorithm("theta_beta", preprocessor=preprocessor)
    bb_alg = LiveScalarAlgorithm("brainbeat", preprocessor=preprocessor)

    live_iapf = iapf_alg.calculate("live-scalar-session", pz_block, iapf_alg.configure(session, {"channels": ["Pz"]})[0], 0).value
    live_tb = tb_alg.calculate("live-scalar-session", fz_block, tb_alg.configure(session, {"channels": ["Fz"]})[0], 0).value
    live_bb = bb_alg.calculate("live-scalar-session", bb_block, bb_alg.configure(session, {"channels": ["Fz", "Pz"]})[0], 0).value

    assert live_iapf == pytest.approx(off_iapf, abs=0.05)
    assert live_tb == pytest.approx(off_tb, rel=0.02)
    assert live_bb == pytest.approx(off_bb, rel=0.02)


def test_six_cards_seven_tasks_under_four_workers_overload_and_recover() -> None:
    gate = Event()

    class _SlowAlgorithm:
        def __init__(self, inner: LiveScalarAlgorithm, block_until_released: bool) -> None:
            self._inner = inner
            self._block = block_until_released
            self.algorithm_id = inner.algorithm_id
            self.scientific_identity = inner.scientific_identity
            self.implementation_identity = inner.implementation_identity

        def configure(self, source, config):
            return self._inner.configure(source, config)

        def calculate(self, session_id, block, config, session_first_sample):
            if self._block:
                gate.wait(timeout=2.0)
            return self._inner.calculate(session_id, block, config, session_first_sample)

    preprocessor = LiveScientificPreprocessingService()
    registry = LiveAnalysisAlgorithmRegistry((
        LiveRbpAlgorithm(preprocessor=preprocessor),
        LiveTopomapAlgorithm("rbp", preprocessor=preprocessor),
        LiveSpectrumAlgorithm("psd", preprocessor=preprocessor),
        LiveSpectrumAlgorithm("stft", preprocessor=preprocessor),
        _SlowAlgorithm(LiveScalarAlgorithm("iapf", preprocessor=preprocessor), True),
        _SlowAlgorithm(LiveScalarAlgorithm("theta_beta", preprocessor=preprocessor), True),
        _SlowAlgorithm(LiveScalarAlgorithm("brainbeat", preprocessor=preprocessor), True),
    ))
    coordinator = LiveAnalysisCoordinator(registry, max_workers=4, preprocessor=preprocessor)
    session = _session(sfreq_hz=250.0, capacity_s=45.0)
    try:
        positions = {"Fz": {"x": 0.0, "y": 0.4, "enabled": True}, "Pz": {"x": 0.0, "y": -0.4, "enabled": True}}
        coordinator.start("live-scalar-session", session, "rbp", {"channels": ["Fz", "Pz"], "window_s": 10.0, "step_s": 1.0})
        coordinator.start("live-scalar-session", session, "rbp-topomap", {
            "spectrum": {"channels": ["Fz", "Pz"], "window_s": 10.0, "step_s": 1.0},
            "field_ids": ["rbp.relative.alpha"],
            "positions": positions,
        })
        coordinator.start("live-scalar-session", session, "psd", {"channels": ["Fz"], "window_s": 10.0, "step_s": 1.0})
        coordinator.start("live-scalar-session", session, "stft", {"channels": ["Pz"], "window_s": 10.0, "step_s": 1.0})
        coordinator.start("live-scalar-session", session, "iapf", {"channels": ["Pz"], "window_s": 20.0, "step_s": 1.0})
        coordinator.start("live-scalar-session", session, "theta_beta", {"channels": ["Fz"], "window_s": 20.0, "step_s": 1.0})
        coordinator.start("live-scalar-session", session, "brainbeat", {"channels": ["Fz", "Pz"], "window_s": 20.0, "step_s": 1.0})

        chunk = _synthetic_eeg(20.0, 250.0, ("Fz", "Pz"))
        session.append(0, chunk, chunk.shape[0])
        coordinator.on_samples("live-scalar-session", session)

        # Immediately push 2 more seconds while slow tasks still occupy workers -> triggers overloaded + skipped_windows
        extra = _synthetic_eeg(2.0, 250.0, ("Fz", "Pz"))
        session.append(chunk.shape[0], extra, extra.shape[0])
        updates = coordinator.on_samples("live-scalar-session", session)
        assert any(item.state == "overloaded" and item.skipped_windows >= 1 for item in updates)

        gate.set()
        # Allow initial saturated workers to finish before pushing normal-pace recovery chunks
        time.sleep(0.5)
        deadline = time.monotonic() + 8.0
        while time.monotonic() < deadline:
            next_chunk = _synthetic_eeg(1.0, 250.0, ("Fz", "Pz"))
            high = session.range_snapshot().high_watermark_sample + 1
            session.append(high, next_chunk, next_chunk.shape[0])
            coordinator.on_samples("live-scalar-session", session)
            time.sleep(0.4)
            states = {
                alg: coordinator.latest("live-scalar-session", alg).state
                for alg in ("iapf", "theta_beta", "brainbeat")
            }
            if all(state == "ready" for state in states.values()):
                break
        assert {
            alg: coordinator.latest("live-scalar-session", alg).state
            for alg in ("iapf", "theta_beta", "brainbeat")
        } == {"iapf": "ready", "theta_beta": "ready", "brainbeat": "ready"}
    finally:
        gate.set()
        coordinator.shutdown()


def test_live_scalar_api_end_to_end() -> None:
    with TestClient(app) as client:
        sid = "live-scalar-api-test"
        create = client.post("/api/live-recordings/sessions", json={
            "session_id": sid,
            "stream_revision": "dev:250",
            "schema_revision": "acquisition-eeg-v1",
            "sampling_rate_hz": 250.0,
            "channel_names": ["Fz", "Pz"],
            "channel_units": ["V", "V"],
            "capacity_samples": 40 * 250,
        })
        assert create.status_code == 201

        for alg, cfg in (
            ("iapf", {"channels": ["Pz"], "window_s": 20.0, "step_s": 1.0}),
            ("theta_beta", {"channels": ["Fz"], "window_s": 20.0, "step_s": 1.0, "frequency_mode": "individualized"}),
            ("brainbeat", {"channels": ["Fz", "Pz"], "window_s": 20.0, "step_s": 1.0}),
        ):
            started = client.post(
                f"/api/live-recordings/sessions/{sid}/algorithms/{alg}",
                json={"schema_version": "live-analysis-config-v1", "config": cfg},
            )
            assert started.status_code == 201
            assert started.json()["state"] == "warming_up"

        chunk = _synthetic_eeg(20.0, 250.0, ("Fz", "Pz"))
        ack = client.post(
            f"/api/live-recordings/sessions/{sid}/batches/binary?first_sample_counter=0&sample_count={chunk.shape[0]}",
            content=np.asarray(chunk, dtype="<f8").tobytes(),
            headers={"content-type": "application/vnd.brain-platform.float64"},
        )
        assert ack.status_code == 200

        deadline = time.monotonic() + 5.0
        ready = {}
        while time.monotonic() < deadline and len(ready) < 3:
            for alg in ("iapf", "theta_beta", "brainbeat"):
                res = client.get(f"/api/live-recordings/sessions/{sid}/algorithms/{alg}")
                assert res.status_code == 200
                body = res.json()
                if body["state"] == "ready":
                    ready[alg] = body
            if len(ready) < 3:
                time.sleep(0.05)

        assert set(ready) == {"iapf", "theta_beta", "brainbeat"}
        assert ready["iapf"]["payload"]["unit"] == "Hz"
        assert ready["theta_beta"]["payload"]["unit"] == "dimensionless"
        assert ready["brainbeat"]["payload"]["unit"] == "ratio"
        montage = client.put(f"/api/live-recordings/sessions/{sid}/montage", json={
            "schema_version": "live-montage-v1", "revision": 1,
            "effective_from_sample_counter": len(chunk), "fingerprint": "misleading-brainbeat-names",
            "profile": {"DerivedChannels": [
                {"Name": "Fz-AVG", "PositiveLabel": "Fz", "NegativeKind": 1, "NegativeLabels": ["Pz"], "DisplayOrder": 0},
                {"Name": "Pz-REF", "PositiveLabel": "Pz", "NegativeKind": 0, "NegativeLabels": [], "DisplayOrder": 1},
            ]},
        })
        assert montage.status_code == 200
        rejected = client.post(f"/api/live-recordings/sessions/{sid}/algorithms/brainbeat", json={
            "schema_version": "live-analysis-config-v1", "config": {"channels": ["Fz-AVG", "Pz-REF"]},
        })
        assert rejected.status_code == 422
        assert rejected.json()["code"] == "LIVE_CONFIG_INVALID"
        client.delete(f"/api/live-recordings/sessions/{sid}")
