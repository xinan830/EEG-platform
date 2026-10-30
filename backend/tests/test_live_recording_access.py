import numpy as np
import pytest

from app.services.live_recording_access import (
    LiveRecordingAccessError,
    LiveRecordingAccessService,
)


def _session(capacity=4):
    service = LiveRecordingAccessService(max_sessions=2)
    return service.create(
        "acq-1", "stream-v1", "schema-v1", 250.0,
        ("F3", "F4"), ("V", "V"), capacity,
    )


def test_live_access_preserves_identity_order_and_explicit_gap():
    session = _session()
    session.append(100, np.array([[1.0, 2.0], [3.0, 4.0]]), 2)
    session.append(104, np.array([[5.0, 6.0]]), 1)

    block = session.read_samples(99, 106, ("F4", "F3"))

    assert block.source_identity.acquisition_session_id == "acq-1"
    assert block.channel_names == ("F4", "F3")
    assert block.channel_units == ("V", "V")
    np.testing.assert_array_equal(block.values_v[1:3], [[2.0, 1.0], [4.0, 3.0]])
    assert any(item.reason == "source_gap" and (item.start_sample, item.end_sample) == (102, 104)
               for item in block.unavailable_intervals)
    assert any(item.reason == "overwritten" and item.start_sample == 99 for item in block.unavailable_intervals)
    assert any(item.reason == "not_yet_available" and item.start_sample == 105 for item in block.unavailable_intervals)


def test_live_access_evicts_old_samples_and_rejects_overlap():
    session = _session(capacity=3)
    session.append(0, np.arange(8, dtype=float).reshape(4, 2), 4)
    snapshot = session.range_snapshot()
    assert snapshot.retained_start_sample == 1
    assert snapshot.high_watermark_sample == 3

    with pytest.raises(LiveRecordingAccessError, match="重叠"):
        session.append(3, np.ones((1, 2)), 1)

    block = session.read_samples(0, 2)
    assert any(item.reason == "overwritten" for item in block.unavailable_intervals)


def test_live_access_session_capacity_and_close_are_explicit():
    service = LiveRecordingAccessService(max_sessions=1)
    service.create("one", "stream", "schema", 250, ("Fz",), ("V",), 4)
    with pytest.raises(LiveRecordingAccessError, match="容量"):
        service.create("two", "stream", "schema", 250, ("Fz",), ("V",), 4)
    service.close("one")
    with pytest.raises(LiveRecordingAccessError, match="不存在"):
        service.require("one")


def test_live_access_rejects_non_volt_channels():
    service = LiveRecordingAccessService()
    with pytest.raises(LiveRecordingAccessError, match="V 单位"):
        service.create("acq", "stream", "schema", 250, ("Fz",), ("uV",), 4)


def test_live_access_honors_cooperative_read_cancellation():
    session = _session()
    session.append(0, np.ones((1, 2)), 1)

    def cancel():
        raise RuntimeError("cancelled")

    with pytest.raises(RuntimeError, match="cancelled"):
        session.read_samples(0, 1, check_cancelled=cancel)
