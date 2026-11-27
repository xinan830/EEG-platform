from threading import Event
from time import monotonic, sleep

import numpy as np
import pytest
from pydantic import BaseModel

from app.services.live_analysis.live_analysis_contracts import LiveAnalysisAlgorithmRegistry, LiveWindowPlan
from app.services.live_analysis.live_analysis_coordinator import LiveAnalysisCoordinator
from app.services.live_analysis.live_rbp_algorithm import LiveRbpAlgorithm
from app.services.live_analysis.live_recording_access import LiveRecordingAccessError, LiveRecordingAccessService


def source(capacity=2000):
    return LiveRecordingAccessService().create(
        "acq-live", "stream-v1", "schema-v1", 250.0, ("F3", "F4"), ("V", "V"), capacity,
    )


def wait_result(coordinator, algorithm="rbp", revision=1):
    deadline = monotonic() + 5
    while monotonic() < deadline:
        update = coordinator.latest("acq-live", algorithm)
        if update is not None and update.revision >= revision:
            return update
        sleep(0.001)
    raise AssertionError("live result did not arrive")


class Config(BaseModel):
    pass


class Result(BaseModel):
    state: str = "ready"
    scientific_identity: str = "test-v1"
    implementation_identity: str = "test-window-v1"
    values: list[float]


class Algorithm:
    def __init__(self, algorithm_id="test", *, hold=False, fail=False):
        self.algorithm_id = algorithm_id
        self.entered = Event()
        self.release = Event()
        self.finished = Event()
        if not hold:
            self.release.set()
        self.fail = fail
        self.windows = []

    def configure(self, source, config):
        return Config(), LiveWindowPlan(("F4", "F3"), 100, 50)

    def calculate(self, session_id, block, config, session_first_sample):
        self.entered.set()
        try:
            assert self.release.wait(5)
            if self.fail:
                raise RuntimeError("test failure")
            assert block.channel_names == ("F4", "F3")
            assert block.channel_units == ("V", "V")
            assert block.values_v.dtype == np.float64
            assert not block.unavailable_intervals
            self.windows.append((block.start_sample, block.end_sample))
            return Result(values=block.values_v[0].tolist())
        finally:
            self.finished.set()


@pytest.fixture
def make_coordinator():
    coordinators = []
    def make(*algorithms, max_workers=4):
        coordinator = LiveAnalysisCoordinator(LiveAnalysisAlgorithmRegistry(algorithms), max_workers)
        coordinators.append(coordinator)
        return coordinator
    yield make
    for coordinator in coordinators:
        coordinator.shutdown()


def append(coordinator, samples, start, count):
    samples.append(start, np.tile([1e-6, 2e-6], (count, 1)), count)
    return coordinator.on_samples("acq-live", samples)


def test_enable_uses_latest_ten_seconds_even_after_retention_overwrites_origin(make_coordinator):
    samples = source(capacity=3000)
    origin = 10_000
    coordinator = make_coordinator(LiveRbpAlgorithm())
    assert append(coordinator, samples, origin, 23 * 250) == []
    assert samples.range_snapshot().retained_start_sample > origin
    coordinator.start("acq-live", samples, "rbp", {"channels": ["F3"]})
    result = wait_result(coordinator)
    assert (result.start_sample, result.end_sample, result.start_s, result.end_s) == (13250, 15750, 13, 23)
    assert (result.payload["start_s"], result.payload["end_s"]) == (13, 23)
    append(coordinator, samples, 15750, 249)
    assert coordinator.latest("acq-live", "rbp") == result
    append(coordinator, samples, 15999, 1)
    updated = wait_result(coordinator, revision=2)
    assert (updated.start_s, updated.end_s) == (14, 24)
    coordinator.stop("acq-live", "rbp")
    assert append(coordinator, samples, 16000, 250) == []
    coordinator.start("acq-live", samples, "rbp", {"channels": ["F3"]})
    restarted = wait_result(coordinator)
    assert (restarted.start_s, restarted.end_s) == (15, 25)
    coordinator.close_session("acq-live")
    assert coordinator.latest("acq-live", "rbp") is None
    assert coordinator.on_samples("acq-live", samples) == []


def test_enabling_at_four_seconds_waits_only_for_the_remaining_six(make_coordinator):
    samples = source(capacity=3000)
    coordinator = make_coordinator(LiveRbpAlgorithm())
    append(coordinator, samples, 1000, 1000)
    assert coordinator.start("acq-live", samples, "rbp", {"channels": ["F3"]}).state == "warming_up"
    assert append(coordinator, samples, 2000, 1499)[0].revision == 0
    append(coordinator, samples, 3499, 1)
    result = wait_result(coordinator)
    assert (result.start_sample, result.end_sample, result.start_s, result.end_s) == (1000, 3500, 0, 10)


def test_enabling_with_a_recent_gap_reports_the_latest_window_then_recovers(make_coordinator):
    samples = source()
    coordinator = make_coordinator(Algorithm())
    append(coordinator, samples, 1000, 50)
    append(coordinator, samples, 1060, 50)
    coordinator.start("acq-live", samples, "test", {})
    result = wait_result(coordinator, "test")
    assert (result.start_sample, result.end_sample) == (1010, 1110)
    assert result.state == "unavailable"
    assert "source_gap" in result.failure["reasons"]
    append(coordinator, samples, 1110, 50)
    assert wait_result(coordinator, "test", 2).state == "ready"


def test_variable_transport_batches_share_sample_grid(make_coordinator):
    windows = []
    for batches in ((100, 50, 50), (10, 30, 60, 20, 30, 50)):
        samples = source()
        algorithm = Algorithm()
        coordinator = make_coordinator(algorithm)
        coordinator.start("acq-live", samples, "test", {})
        start = 0
        for count in batches:
            append(coordinator, samples, start, count)
            start += count
            if start >= 100 and (start - 100) % 50 == 0:
                wait_result(coordinator, "test", (start - 100) // 50 + 1)
        windows.append(algorithm.windows)
    assert windows == [[(0, 100), (50, 150), (100, 200)]] * 2


def test_common_gap_and_overwrite_gate_prevents_adapter_execution_and_recovers(make_coordinator):
    samples = source(capacity=120)
    algorithm = Algorithm()
    coordinator = make_coordinator(algorithm)
    coordinator.start("acq-live", samples, "test", {})
    append(coordinator, samples, 0, 50)
    append(coordinator, samples, 60, 50)
    gap = wait_result(coordinator, "test")
    assert gap.failure["code"] == "LIVE_ANALYSIS_WINDOW_UNAVAILABLE"
    assert "source_gap" in gap.failure["reasons"]
    assert algorithm.windows == []
    append(coordinator, samples, 110, 90)
    recovered = wait_result(coordinator, "test", 2)
    assert recovered.state == "ready" and recovered.start_sample == 100
    # Retention can advance between scheduling and a worker's read.
    read_entered, allow_read = Event(), Event()
    original_read = samples.read_samples
    def delayed_read(start, end, channels):
        read_entered.set()
        assert allow_read.wait(2)
        return original_read(start, end, channels)
    samples.read_samples = delayed_read
    try:
        append(coordinator, samples, 200, 50)
        assert read_entered.wait(2)
        samples.append(250, np.ones((200, 2)), 200)
    finally:
        allow_read.set()
    overwritten = wait_result(coordinator, "test", 3)
    assert "overwritten" in overwritten.failure["reasons"]


def test_burst_skips_are_explicit_and_windows_stay_aligned(make_coordinator):
    samples = source()
    coordinator = make_coordinator(Algorithm())
    coordinator.start("acq-live", samples, "test", {})
    append(coordinator, samples, 0, 225)
    result = wait_result(coordinator, "test")
    assert (result.start_sample, result.end_sample, result.skipped_windows) == (100, 200, 2)


def test_slow_algorithm_is_bounded_and_cannot_overwrite_newer_overload(make_coordinator):
    samples = source()
    slow = Algorithm(hold=True)
    coordinator = make_coordinator(slow, max_workers=1)
    coordinator.start("acq-live", samples, "test", {})
    append(coordinator, samples, 0, 100)
    assert slow.entered.wait(2)
    append(coordinator, samples, 100, 50)
    overloaded = wait_result(coordinator, "test", 2)
    assert overloaded.state == "overloaded"
    assert overloaded.failure["code"] == "LIVE_ANALYSIS_OVERLOADED"
    assert samples.range_snapshot().high_watermark_sample == 149
    slow.release.set()
    assert slow.finished.wait(2)
    # Wait for completion callback, not just the calculation body.
    deadline = monotonic() + 2
    while coordinator._sessions[("acq-live", "test")].future is not None and monotonic() < deadline:
        sleep(0.001)
    assert coordinator.latest("acq-live", "test") == overloaded
    append(coordinator, samples, 150, 50)
    assert wait_result(coordinator, "test", 3).state == "ready"


def test_algorithm_failure_and_slow_work_do_not_block_other_algorithms(make_coordinator):
    samples = source()
    slow, broken, good = Algorithm("slow", hold=True), Algorithm("broken", fail=True), Algorithm("good")
    coordinator = make_coordinator(slow, broken, good)
    for algorithm in (slow, broken, good):
        coordinator.start("acq-live", samples, algorithm.algorithm_id, {})
    try:
        append(coordinator, samples, 0, 100)
        assert wait_result(coordinator, "good").state == "ready"
        assert wait_result(coordinator, "broken").failure["code"] == "LIVE_ANALYSIS_ALGORITHM_FAILED"
        assert coordinator.latest("acq-live", "slow").state == "warming_up"
    finally:
        slow.release.set()


def test_stop_restart_rejects_old_completion_and_stale_source(make_coordinator):
    samples = source()
    algorithm = Algorithm(hold=True)
    coordinator = make_coordinator(algorithm)
    coordinator.start("acq-live", samples, "test", {})
    append(coordinator, samples, 0, 100)
    assert algorithm.entered.wait(2)
    coordinator.stop("acq-live", "test")
    assert coordinator.latest("acq-live", "test") is None
    replacement = source()
    coordinator.start("acq-live", replacement, "test", {})
    algorithm.release.set()
    append(coordinator, replacement, 1000, 100)
    result = wait_result(coordinator, "test")
    assert (result.start_sample, result.end_sample) == (1000, 1100)
    with pytest.raises(LiveRecordingAccessError) as error:
        coordinator.on_samples("acq-live", samples)
    assert error.value.code == "LIVE_SESSION_SOURCE_MISMATCH"
    coordinator.close_all()
    assert coordinator.latest("acq-live", "test") is None


def test_global_worker_budget_reports_overload_instead_of_queuing(make_coordinator):
    samples = source()
    slow, other = Algorithm("slow", hold=True), Algorithm("other")
    coordinator = make_coordinator(slow, other, max_workers=1)
    for algorithm in (slow, other):
        coordinator.start("acq-live", samples, algorithm.algorithm_id, {})
    try:
        append(coordinator, samples, 0, 100)
        assert wait_result(coordinator, "other").state == "overloaded"
        assert not other.entered.is_set()
    finally:
        slow.release.set()


def test_restarted_algorithm_publishes_new_window_before_old_completion(make_coordinator):
    old_entered, release_old = Event(), Event()
    class DelayedFirst(Algorithm):
        def calculate(self, session_id, block, config, session_first_sample):
            if block.start_sample == 0:
                old_entered.set()
                assert release_old.wait(5)
            return super().calculate(session_id, block, config, session_first_sample)
    samples = source()
    algorithm = DelayedFirst()
    coordinator = make_coordinator(algorithm)
    coordinator.start("acq-live", samples, "test", {})
    append(coordinator, samples, 0, 100)
    assert old_entered.wait(2)
    old_future = coordinator._sessions[("acq-live", "test")].future
    try:
        samples.append(100, np.ones((100, 2)), 100)
        coordinator.start("acq-live", samples, "test", {})
        newest = wait_result(coordinator, "test")
        assert newest.start_sample == 100
    finally:
        release_old.set()
    old_future.result(timeout=2)
    # Taking the coordinator lock also serializes with its completion callback.
    assert coordinator.latest("acq-live", "test") == newest


def test_close_invalidates_running_work_and_invalid_restart_keeps_current_config(make_coordinator):
    samples = source()
    algorithm = Algorithm(hold=True)
    coordinator = make_coordinator(algorithm, LiveRbpAlgorithm())
    coordinator.start("acq-live", samples, "test", {})
    append(coordinator, samples, 0, 100)
    assert algorithm.entered.wait(2)
    old_future = coordinator._sessions[("acq-live", "test")].future
    coordinator.start("acq-live", samples, "rbp", {"channels": ["F3"], "window_s": 4})
    warming = coordinator.latest("acq-live", "rbp")
    with pytest.raises(LiveRecordingAccessError) as error:
        coordinator.start("acq-live", samples, "rbp", {"channels": ["F3", "f3"]})
    assert error.value.code == "LIVE_CONFIG_INVALID"
    assert coordinator.latest("acq-live", "rbp") == warming
    coordinator.close_session("acq-live")
    samples.close()
    algorithm.release.set()
    old_future.result(timeout=2)
    assert coordinator.latest("acq-live", "test") is None
    assert coordinator.latest("acq-live", "rbp") is None
