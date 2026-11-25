"""Tests for shared scientific preprocessing contracts and live/offline preprocessing layers."""

from __future__ import annotations

from threading import Barrier, Event, Thread
from types import SimpleNamespace
import numpy as np
import pytest

from app.scientific.contracts.preprocessing import ScientificFilterSpec, live_spectral_filter_spec
from app.scientific.primitives import DEFAULT_SPECTRAL_GATEWAY
from app.services.live_analysis_contracts import LiveAnalysisAlgorithmRegistry
from app.services.live_analysis_coordinator import LiveAnalysisCoordinator
from app.services.live_rbp_algorithm import LiveRbpAlgorithm, LiveRbpConfig
from app.services.live_recording_access import LiveRecordingAccessSession, LiveSourceIdentity
from app.services.live_scientific_preprocessing import LiveScientificPreprocessingService
from app.services.live_spectral_algorithms import LiveSpectrumAlgorithm
from app.services.live_topomap_algorithm import LiveTopomapAlgorithm
from app.services.offline_scientific_preprocessing import OfflineScientificPreprocessingService
from app.services.recording_access import RecordingSourceIdentity
from app.services.sample_access_contract import SampleBlock


class _CountingGateway:
    def __init__(self, entered: Event | None = None, release: Event | None = None):
        self.calls = 0
        self._entered = entered
        self._release = release

    def preprocess(self, data, sfreq_hz, *, low_hz=None, high_hz=None, notch_hz=None, notch_q=30.0):
        self.calls += 1
        if self._entered is not None:
            self._entered.set()
        if self._release is not None:
            self._release.wait(timeout=5.0)
        return DEFAULT_SPECTRAL_GATEWAY.preprocess(
            data, sfreq_hz, low_hz=low_hz, high_hz=high_hz, notch_hz=notch_hz, notch_q=notch_q,
        )


def _synthetic_block(
    session_id: str = "live-1",
    start: int = 0,
    n: int = 2500,
    sfreq: float = 250.0,
    seed: int = 7,
    montage_revision: int = 0,
):
    rng = np.random.default_rng(seed)
    t = np.arange(n, dtype=np.float64) / sfreq
    ch0 = 15e-6 * np.sin(2 * np.pi * 10.0 * t) + 2e-6 * rng.standard_normal(n)
    ch1 = 12e-6 * np.sin(2 * np.pi * 20.0 * t) + 2e-6 * rng.standard_normal(n)
    values = np.column_stack([ch0, ch1]).astype(np.float64)
    return SampleBlock(
        source_identity=LiveSourceIdentity(session_id, "stream-v1", "schema-v1", montage_revision=montage_revision),
        start_sample=start,
        end_sample=start + n,
        sampling_rate_hz=sfreq,
        channel_names=("Fp1", "O1"),
        channel_units=("V", "V"),
        values_v=values,
        unavailable_intervals=(),
    )


def test_scientific_filter_spec_validation_and_deterministic_fingerprint():
    spec_a = live_spectral_filter_spec(50.0)
    spec_b = live_spectral_filter_spec(50.0)
    spec_60 = live_spectral_filter_spec(60.0)

    spec_a.validate(250.0)
    assert spec_a.fingerprint() == spec_b.fingerprint()
    assert spec_a.fingerprint() != spec_60.fingerprint()

    with pytest.raises(ValueError):
        spec_a.validate(80.0)
    with pytest.raises(ValueError):
        ScientificFilterSpec(low_hz=50.0, high_hz=10.0).validate(250.0)


def test_live_scientific_preprocessing_reuses_same_window_and_tracks_montage_revision():
    gateway = _CountingGateway()
    preprocessor = LiveScientificPreprocessingService(gateway=gateway, max_entries=8)
    block_rev0 = _synthetic_block(montage_revision=0)
    config = LiveRbpConfig(channels=["Fp1", "O1"], window_s=10.0, step_s=1.0, notch_hz=50.0)

    rbp = LiveRbpAlgorithm(preprocessor=preprocessor)
    psd = LiveSpectrumAlgorithm("psd", preprocessor=preprocessor)
    rbp_map = LiveTopomapAlgorithm("rbp", preprocessor=preprocessor)

    r1 = rbp.calculate("live-1", block_rev0, config, 0)
    r2 = psd.calculate("live-1", block_rev0, config, 0)
    assert r1.state == "ready"
    assert r2.state == "ready"
    assert gateway.calls == 1
    assert preprocessor.stats()["hits"] == 1
    assert rbp_map.spectrum._preprocessor is preprocessor

    # Even if channel names and sample values match, a new montage_revision MUST miss cache!
    block_rev1_same_values = SampleBlock(
        source_identity=LiveSourceIdentity("live-1", "stream-v1", "schema-v1", montage_revision=1),
        start_sample=block_rev0.start_sample,
        end_sample=block_rev0.end_sample,
        sampling_rate_hz=block_rev0.sampling_rate_hz,
        channel_names=block_rev0.channel_names,
        channel_units=block_rev0.channel_units,
        values_v=block_rev0.values_v.copy(),
        unavailable_intervals=(),
    )
    prepared_rev1 = preprocessor.prepare_window("live-1", block_rev1_same_values, live_spectral_filter_spec(50.0))
    assert gateway.calls == 2
    assert prepared_rev1.evidence["montage_revision"] == 1
    assert not prepared_rev1.values_v.flags.writeable
    with pytest.raises(ValueError):
        prepared_rev1.values_v[0, 0] = 0.0

    # End-to-end check: LiveRecordingAccessSession.read_samples embeds updated montage_revision
    session = LiveRecordingAccessSession(
        LiveSourceIdentity("live-m", "s1", "v1"),
        sampling_rate_hz=250.0,
        channel_names=("Fp1", "O1"),
        channel_units=("V", "V"),
        capacity_samples=5000,
    )
    session.append(0, block_rev0.values_v, 2500)
    b0 = session.read_samples(0, 2500, ("Fp1", "O1"))
    assert b0.source_identity.montage_revision == 0
    session.set_montage(
        {
            "derivedChannels": [
                {"displayOrder": 0, "name": "Fp1", "positiveLabel": "Fp1", "negativeLabels": ["O1"]},
                {"displayOrder": 1, "name": "O1", "positiveLabel": "O1", "negativeLabels": []},
            ]
        },
        revision=1,
        effective_from_sample_counter=2500,
    )
    b1 = session.read_samples(0, 2500, ("Fp1", "O1"))
    assert b1.source_identity.montage_revision == 1
    preprocessor.prepare_window("live-m", b0, live_spectral_filter_spec(50.0))
    preprocessor.prepare_window("live-m", b1, live_spectral_filter_spec(50.0))
    assert gateway.calls == 4


def test_live_invalidation_while_inflight_prevents_stale_cache_writeback():
    entered = Event()
    release = Event()
    gateway = _CountingGateway(entered=entered, release=release)
    preprocessor = LiveScientificPreprocessingService(gateway=gateway)
    coordinator = LiveAnalysisCoordinator(
        LiveAnalysisAlgorithmRegistry((LiveRbpAlgorithm(preprocessor=preprocessor),)),
        preprocessor=preprocessor,
    )
    block = _synthetic_block("sess-race")
    spec = live_spectral_filter_spec(50.0)
    holder = []

    def inflight_worker():
        holder.append(preprocessor.prepare_window("sess-race", block, spec))

    try:
        thread = Thread(target=inflight_worker)
        thread.start()
        assert entered.wait(timeout=3.0)

        # Invalidate session while preprocessing is mid-flight!
        coordinator.close_session("sess-race")
        release.set()
        thread.join(timeout=5.0)

        assert len(holder) == 1
        # Stale in-flight result MUST NOT repopulate cache after close_session!
        assert preprocessor.stats()["entries"] == 0

        # A reused session_id must recompute cleanly rather than hitting stale cache
        preprocessor.prepare_window("sess-race", block, spec)
        assert gateway.calls == 2
        assert preprocessor.stats()["entries"] == 1
    finally:
        release.set()
        coordinator.shutdown()


def test_offline_bounded_window_cache_tracks_real_recording_identity_and_coalesces_concurrent_reads():
    rng = np.random.default_rng(42)
    sfreq = 250.0
    total_samples = 5000
    raw = rng.standard_normal((total_samples, 2)).astype(np.float64) * 15e-6
    read_calls = 0
    read_entered = Event()
    read_release = Event()

    class _FakeSession:
        def __init__(self, identity, *, block_first_read: bool = False):
            self.identity = identity
            self.channel_names = ("Fp1", "O1")
            self.sampling_rate_hz = sfreq
            self.sample_count = total_samples
            self._block_first_read = block_first_read

        def read_samples(self, start, end, channels, check_cancelled=None):
            nonlocal read_calls
            read_calls += 1
            if self._block_first_read:
                read_entered.set()
                read_release.wait(timeout=5.0)
            return SampleBlock(
                source_identity=self.identity,
                start_sample=start,
                end_sample=end,
                sampling_rate_hz=sfreq,
                channel_names=tuple(channels),
                channel_units=("V", "V"),
                values_v=raw[start:end].copy(),
                unavailable_intervals=(),
            )

    service = OfflineScientificPreprocessingService(continuous=SimpleNamespace(), max_window_entries=8)
    id_v1 = RecordingSourceIdentity("rec-1", source_sha256="sha-aaa", import_version="v1", signal_view_sha256="view-1")

    # 1. Concurrent identical requests coalesce into ONE disk read + ONE filter call
    sess_concurrent = _FakeSession(id_v1, block_first_read=True)
    out = []
    start_barrier = Barrier(2)

    def run_window():
        start_barrier.wait(timeout=3.0)
        out.append(
            service.prepare_bounded_window(
                sess_concurrent, start_sample=1000, end_sample=3500, channels=["Fp1", "O1"],
                low_hz=1.0, high_hz=50.0, notch_hz=50.0, context_s=2.0, algorithm_version="stft-v1",
            )
        )

    t1 = Thread(target=run_window)
    t2 = Thread(target=run_window)
    t1.start()
    t2.start()
    assert read_entered.wait(timeout=3.0)
    read_release.set()
    t1.join(timeout=5.0)
    t2.join(timeout=5.0)
    assert len(out) == 2
    assert out[0] is out[1]
    assert read_calls == 1
    assert service.window_cache_stats()["coalesced"] == 1

    # 2. Real signal identity changes MUST miss cache (source_sha256 / import_version / signal_view_sha256)
    for mutated in (
        RecordingSourceIdentity("rec-1", source_sha256="sha-bbb", import_version="v1", signal_view_sha256="view-1"),
        RecordingSourceIdentity("rec-1", source_sha256="sha-aaa", import_version="v2", signal_view_sha256="view-1"),
        RecordingSourceIdentity("rec-1", source_sha256="sha-aaa", import_version="v1", signal_view_sha256="view-2"),
    ):
        before = read_calls
        service.prepare_bounded_window(
            _FakeSession(mutated), start_sample=1000, end_sample=3500, channels=["Fp1", "O1"],
            low_hz=1.0, high_hz=50.0, notch_hz=50.0, context_s=2.0, algorithm_version="stft-v1",
        )
        assert read_calls == before + 1

    # 3. In-flight invalidation prevents stale offline cache writeback
    read_entered.clear()
    read_release.clear()
    id_race = RecordingSourceIdentity("rec-race", source_sha256="sha-r", import_version="v1", signal_view_sha256="view-r")
    race_session = _FakeSession(id_race, block_first_read=True)
    race_thread = Thread(
        target=lambda: service.prepare_bounded_window(
            race_session, start_sample=1000, end_sample=3500, channels=["Fp1", "O1"],
            low_hz=1.0, high_hz=50.0, notch_hz=50.0, context_s=2.0, algorithm_version="stft-v1",
        )
    )
    race_thread.start()
    assert read_entered.wait(timeout=3.0)
    service.invalidate_recording("rec-race")
    read_release.set()
    race_thread.join(timeout=5.0)
    assert all(key[0] != "rec-race" for key in service._window_entries)
