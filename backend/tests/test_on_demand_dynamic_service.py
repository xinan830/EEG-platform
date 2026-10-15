import sqlite3
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from types import SimpleNamespace

import numpy as np

from app.algorithm_runtime.registry import AlgorithmRegistry
from app.algorithms.psd.runner import PsdAlgorithm
from app.algorithm_runtime.on_demand import DynamicWindowRequest, DynamicWindowResponse
from app.persistence.migrations import migrate_database
from app.services.on_demand_dynamic import DynamicWindowService
from app.models.recording import RecordingSummary


class _RecordingStub:
    def __init__(self, recording):
        self.recording = recording

    def require_recording(self, recording_id):
        assert recording_id == self.recording.id
        return self.recording

    def load_spectrum(self, _recording, start_s=0, window_s=0, channels=None, **_kwargs):
        frequencies = PsdAlgorithm._frequency_axis(200, 1, 50)
        return {
            "frequencies_hz": frequencies.tolist(),
            "psd": {channels[0]: np.ones(frequencies.size).tolist()},
            "sfreq_hz": 200.0,
            "channels": list(channels),
            "analysis_reference": "original",
            "signal_source": {"signal_mode": "raw"},
            "algorithm_version": "test",
            "welch_contract": {},
            "filter_contract": {},
            "filter_frequency_range_hz": {},
            "frequency_range_hz": {"low_hz": 1, "high_hz": 50},
            "units": {"psd": "uV^2/Hz"},
            "band_power": {channels[0]: {}},
            "relative_band_power": {channels[0]: {}},
            "quality": {"clean_ratio": 1.0, "clean_segments": 1, "total_segments": 1, "gate_failed": False, "rejected_reasons": []},
        }


def test_service_ensures_and_publishes_one_psd_window(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    migrate_database(database)
    with sqlite3.connect(database) as connection:
        connection.execute(
            "INSERT INTO recordings(id, original_name, stored_name, extension, created_at, source_sha256, sfreq, duration_s, channels_json) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
            ("recording", "recording", "recording", ".edf", "now", "source", 200, 20, '["F3"]'),
        )
    recording = RecordingSummary(id="recording", original_name="recording", stored_name="recording", extension=".edf", created_at="now", source_sha256="source", sfreq=200, duration_s=20, channels=("F3",))
    registry = AlgorithmRegistry()
    registry.register(PsdAlgorithm())
    service = DynamicWindowService(_RecordingStub(recording), registry, database, tmp_path / "artifacts")
    try:
        response = service.ensure(DynamicWindowRequest(
            recording_id="recording", algorithm_id="psd", cursor_sample=800,
            request_id="request", config={
                "time": {"start_s": 0, "end_s": 20}, "channel": "F3", "mode": "dynamic",
                "dynamic_window_s": 5, "refresh_step_s": 1, "low_hz": 1, "high_hz": 30, "notch_hz": None,
            },
        ))
        assert response["range"]["end_s"] == 4.0
        for _ in range(30):
            current = service.get(response["window_id"])
            if current["state"] == "ready":
                break
            time.sleep(0.02)
        assert current["state"] == "ready"
        assert current["result"]["sha256"]
        assert current["result"]["arrays"]["psd"]
        validated = DynamicWindowResponse.model_validate(current)
        assert validated.state == "ready"
        artifact_id = service.repository.get_window(response["window_id"]).result_artifact_id
        assert artifact_id
        payload_path = service.artifacts_dir / f"{artifact_id}.npz"
        original = payload_path.read_bytes()
        payload_path.write_bytes(original[:-1] + bytes([original[-1] ^ 0xFF]))
        try:
            try:
                service.get(response["window_id"])
                raise AssertionError("corrupt dynamic artifact must be rejected")
            except ValueError as error:
                assert "checksum" in str(error)
        finally:
            payload_path.write_bytes(original)
        reused = service.ensure(DynamicWindowRequest(
            recording_id="recording", algorithm_id="psd", cursor_sample=800,
            request_id="request-2", config={
                "time": {"start_s": 0, "end_s": 20}, "channel": "F3", "mode": "dynamic",
                "dynamic_window_s": 5, "refresh_step_s": 1, "low_hz": 1, "high_hz": 30, "notch_hz": None,
            },
        ))
        assert reused["window_id"] == response["window_id"]
        assert reused["state"] == "ready"
    finally:
        service.close()


def test_last_subscription_cancels_queued_window(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    migrate_database(database)
    with sqlite3.connect(database) as connection:
        connection.execute(
            "INSERT INTO recordings(id, original_name, stored_name, extension, created_at, source_sha256, sfreq, duration_s, channels_json) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
            ("recording", "recording", "recording", ".edf", "now", "source", 200, 20, '["F3"]'),
        )
    recording = RecordingSummary(id="recording", original_name="recording", stored_name="recording", extension=".edf", created_at="now", source_sha256="source", sfreq=200, duration_s=20, channels=("F3",))
    registry = AlgorithmRegistry()
    registry.register(PsdAlgorithm())
    started = threading.Event()
    release = threading.Event()

    class BlockingRecordingStub(_RecordingStub):
        def load_spectrum(self, *args, **kwargs):
            started.set()
            release.wait(timeout=2)
            return super().load_spectrum(*args, **kwargs)

    service = DynamicWindowService(BlockingRecordingStub(recording), registry, database, tmp_path / "artifacts")
    try:
        response = service.ensure(DynamicWindowRequest(
            recording_id="recording", algorithm_id="psd", cursor_sample=800,
            request_id="request", config={
                "time": {"start_s": 0, "end_s": 20}, "channel": "F3", "mode": "dynamic",
                "dynamic_window_s": 5, "refresh_step_s": 1, "low_hz": 1, "high_hz": 30, "notch_hz": None,
            },
        ))
        assert started.wait(timeout=1)
        cancelled = service.cancel_subscription(response["subscription_id"])
        assert cancelled["state"] == "cancelled"
        for _ in range(50):
            current = service.get(response["window_id"])
            if current["state"] == "cancelled":
                break
            threading.Event().wait(0.01)
        assert current["state"] == "cancelled"
        release.set()
    finally:
        service.close()


def test_service_startup_removes_unreferenced_published_files(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    migrate_database(database)
    artifacts = tmp_path / "artifacts"
    dynamic = artifacts / "dynamic-windows"
    dynamic.mkdir(parents=True)
    (dynamic / "orphan.npz").write_bytes(b"partial")
    (dynamic / "orphan.json").write_text("{}", encoding="utf-8")

    service = DynamicWindowService(
        _RecordingStub(SimpleNamespace(id="missing", source_sha256="source", sfreq=200, duration_s=1, channels=("F3",))),
        AlgorithmRegistry(), database, artifacts,
    )
    try:
        assert not (dynamic / "orphan.npz").exists()
        assert not (dynamic / "orphan.json").exists()
    finally:
        service.close()


def test_concurrent_ensure_shares_one_window_and_attempt(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    migrate_database(database)
    with sqlite3.connect(database) as connection:
        connection.execute(
            "INSERT INTO recordings(id, original_name, stored_name, extension, created_at, source_sha256, sfreq, duration_s, channels_json) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
            ("recording", "recording", "recording", ".edf", "now", "source", 200, 20, '["F3"]'),
        )
    recording = RecordingSummary(id="recording", original_name="recording", stored_name="recording", extension=".edf", created_at="now", source_sha256="source", sfreq=200, duration_s=20, channels=("F3",))
    registry = AlgorithmRegistry()
    registry.register(PsdAlgorithm())
    service = DynamicWindowService(_RecordingStub(recording), registry, database, tmp_path / "artifacts")
    payload = DynamicWindowRequest(
        recording_id="recording", algorithm_id="psd", cursor_sample=800,
        config={
            "time": {"start_s": 0, "end_s": 20}, "channel": "F3", "mode": "dynamic",
            "dynamic_window_s": 5, "refresh_step_s": 1, "low_hz": 1, "high_hz": 30, "notch_hz": None,
        },
    )
    try:
        with ThreadPoolExecutor(max_workers=4) as pool:
            responses = list(pool.map(lambda index: service.ensure(payload.model_copy(update={"request_id": f"request-{index}"})), range(4)))
        assert len({item["window_id"] for item in responses}) == 1
        with sqlite3.connect(database) as connection:
            count = connection.execute(
                "SELECT COUNT(*) FROM dynamic_analysis_attempts WHERE window_id = ?",
                (responses[0]["window_id"],),
            ).fetchone()[0]
        assert count == 1
    finally:
        service.close()


def test_one_subscriber_cancellation_does_not_cancel_shared_work(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    migrate_database(database)
    with sqlite3.connect(database) as connection:
        connection.execute(
            "INSERT INTO recordings(id, original_name, stored_name, extension, created_at, source_sha256, sfreq, duration_s, channels_json) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
            ("recording", "recording", "recording", ".edf", "now", "source", 200, 20, '["F3"]'),
        )
    recording = RecordingSummary(id="recording", original_name="recording", stored_name="recording", extension=".edf", created_at="now", source_sha256="source", sfreq=200, duration_s=20, channels=("F3",))
    started = threading.Event()
    release = threading.Event()

    class BlockingRecordingStub(_RecordingStub):
        def load_spectrum(self, *args, **kwargs):
            started.set()
            release.wait(timeout=2)
            return super().load_spectrum(*args, **kwargs)

    registry = AlgorithmRegistry()
    registry.register(PsdAlgorithm())
    service = DynamicWindowService(BlockingRecordingStub(recording), registry, database, tmp_path / "artifacts")
    payload = dict(
        recording_id="recording", algorithm_id="psd", cursor_sample=800,
        config={
            "time": {"start_s": 0, "end_s": 20}, "channel": "F3", "mode": "dynamic",
            "dynamic_window_s": 5, "refresh_step_s": 1, "low_hz": 1, "high_hz": 30, "notch_hz": None,
        },
    )
    try:
        first = service.ensure(DynamicWindowRequest(**payload, request_id="request-1"))
        second = service.ensure(DynamicWindowRequest(**payload, request_id="request-2"))
        assert first["window_id"] == second["window_id"]
        assert started.wait(timeout=1)
        service.cancel_subscription(first["subscription_id"])
        assert service.get(second["window_id"])["state"] in {"preparing_source", "computing_window"}
        release.set()
        for _ in range(50):
            current = service.get(second["window_id"])
            if current["state"] == "ready":
                break
            time.sleep(0.01)
        assert current["state"] == "ready"
    finally:
        release.set()
        service.close()


def test_explicit_retry_after_cancel_creates_new_window_generation(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    migrate_database(database)
    with sqlite3.connect(database) as connection:
        connection.execute(
            "INSERT INTO recordings(id, original_name, stored_name, extension, created_at, source_sha256, sfreq, duration_s, channels_json) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
            ("recording", "recording", "recording", ".edf", "now", "source", 200, 20, '["F3"]'),
        )
    recording = RecordingSummary(id="recording", original_name="recording", stored_name="recording", extension=".edf", created_at="now", source_sha256="source", sfreq=200, duration_s=20, channels=("F3",))
    started = threading.Event()
    release = threading.Event()

    class BlockingRecordingStub(_RecordingStub):
        def load_spectrum(self, *args, **kwargs):
            started.set()
            release.wait(timeout=2)
            return super().load_spectrum(*args, **kwargs)

    registry = AlgorithmRegistry()
    registry.register(PsdAlgorithm())
    service = DynamicWindowService(BlockingRecordingStub(recording), registry, database, tmp_path / "artifacts")
    payload = dict(
        recording_id="recording", algorithm_id="psd", cursor_sample=800,
        config={
            "time": {"start_s": 0, "end_s": 20}, "channel": "F3", "mode": "dynamic",
            "dynamic_window_s": 5, "refresh_step_s": 1, "low_hz": 1, "high_hz": 30, "notch_hz": None,
        },
    )
    try:
        first = service.ensure(DynamicWindowRequest(**payload, request_id="request-1"))
        assert started.wait(timeout=1)
        service.cancel_subscription(first["subscription_id"])
        for _ in range(50):
            if service.get(first["window_id"])["state"] == "cancelled":
                break
            time.sleep(0.01)
        assert service.get(first["window_id"])["state"] == "cancelled"
        release.set()

        without_retry = service.ensure(DynamicWindowRequest(**payload, request_id="request-2"))
        assert without_retry["window_id"] == first["window_id"]
        assert without_retry["state"] == "cancelled"

        retried = service.ensure(DynamicWindowRequest(**payload, request_id="request-3", retry=True, retry_of_window_id=first["window_id"]))
        assert retried["window_id"] != first["window_id"]
        assert retried["identity"]["generation"] == 1
        for _ in range(80):
            current = service.get(retried["window_id"])
            if current["state"] == "ready":
                break
            time.sleep(0.01)
        assert current["state"] == "ready"
    finally:
        release.set()
        service.close()


def test_starting_new_service_recovers_expired_worker_lease(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    migrate_database(database)
    with sqlite3.connect(database) as connection:
        connection.execute(
            "INSERT INTO recordings(id, original_name, stored_name, extension, created_at, source_sha256, sfreq, duration_s, channels_json) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
            ("recording", "recording", "recording", ".edf", "now", "source", 200, 20, '["F3"]'),
        )
    recording = RecordingSummary(id="recording", original_name="recording", stored_name="recording", extension=".edf", created_at="now", source_sha256="source", sfreq=200, duration_s=20, channels=("F3",))
    registry = AlgorithmRegistry()
    registry.register(PsdAlgorithm())
    started = threading.Event()
    release = threading.Event()

    class BlockingRecordingStub(_RecordingStub):
        def load_spectrum(self, *args, **kwargs):
            started.set()
            release.wait(timeout=5)
            return super().load_spectrum(*args, **kwargs)

    service = DynamicWindowService(BlockingRecordingStub(recording), registry, database, tmp_path / "artifacts")
    payload = DynamicWindowRequest(
        recording_id="recording", algorithm_id="psd", cursor_sample=800,
        config={
            "time": {"start_s": 0, "end_s": 20}, "channel": "F3", "mode": "dynamic",
            "dynamic_window_s": 5, "refresh_step_s": 1, "low_hz": 1, "high_hz": 30, "notch_hz": None,
        }, request_id="request-1",
    )
    try:
        pending = service.ensure(payload)
        assert started.wait(timeout=1)
        # Simulate process death after the worker acquired its lease.  The
        # next service instance must fence that worker and queue fresh work.
        with sqlite3.connect(database) as connection:
            connection.execute(
                "UPDATE dynamic_analysis_attempts SET state = 'running', worker_id = 'dead-worker', lease_token = 'dead-token', lease_expires_at = '2000-01-01T00:00:00+00:00' WHERE window_id = ?",
                (pending["window_id"],),
            )
        service.close()
        restarted = DynamicWindowService(_RecordingStub(recording), registry, database, tmp_path / "artifacts")
        try:
            # Recovery is automatic: no consumer ensure is needed to recreate
            # the persisted job after a process restart.
            for _ in range(400):
                current = restarted.get(pending["window_id"])
                if current["state"] == "ready":
                    break
                time.sleep(0.01)
            assert current["state"] == "ready"
            with sqlite3.connect(database) as connection:
                states = {row[0] for row in connection.execute("SELECT state FROM dynamic_analysis_attempts WHERE window_id = ?", (pending["window_id"],))}
            assert "expired" in states
            assert "succeeded" in states
        finally:
            restarted.close()
    finally:
        release.set()
        service.close()
