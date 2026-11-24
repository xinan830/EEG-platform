import numpy as np
import pytest

from app.services import recording_access as recording_access_module
from app.services.recording_access import (
    RecordingAccessError,
    RecordingAccessSession,
    RecordingSourceIdentity,
)


class _Reader:
    def __init__(self, values):
        self.values = values
        self.closed = False

    def read(self, start, end, channels=None, check_cancelled=None):
        if check_cancelled is not None:
            check_cancelled()
        return self.values[start:end, channels]

    def source_gaps(self, start, stop):
        return ()

    def close(self):
        self.closed = True


def _session(values, validate_source=lambda: None):
    reader = _Reader(values)
    session = RecordingAccessSession(
        RecordingSourceIdentity("recording-1", "sha256", "import-v1"),
        reader,
        250.0,
        len(values),
        ("F3", "F4"),
        (),
        validate_source,
        serialize_reads=False,
    )
    return session, reader


def test_access_session_preserves_half_open_range_channel_order_and_gaps():
    values = np.array([[1.0, 2.0], [np.nan, 3.0], [4.0, 5.0]])
    session, _ = _session(values)

    block = session.read_samples(1, 3, ("F4", "F3"))

    assert (block.start_sample, block.end_sample) == (1, 3)
    assert block.channel_names == ("F4", "F3")
    assert block.sampling_rate_hz == 250.0
    np.testing.assert_array_equal(block.values_v[:, 0], [3.0, 5.0])
    assert np.isnan(block.values_v[0, 1])
    assert [(gap.start_sample, gap.end_sample, gap.reason, gap.channels) for gap in block.unavailable_intervals] == [
        (1, 2, "missing_value", ("F3",)),
    ]
    assert block.channel_units == ("V", "V")


def test_access_session_throttles_reader_cancellation_checks(monkeypatch):
    monkeypatch.setattr(recording_access_module.time, "monotonic", lambda: 0.0)
    values = np.ones((1000, 2))
    calls = 0

    class _ChattyReader(_Reader):
        def read(self, start, end, channels=None, check_cancelled=None):
            if check_cancelled is not None:
                for _ in range(1000):
                    check_cancelled()
            return super().read(start, end, channels, None)

    reader = _ChattyReader(values)
    session = RecordingAccessSession(
        RecordingSourceIdentity("recording-1", "sha256", "import-v1"),
        reader,
        250.0,
        len(values),
        ("F3", "F4"),
        (),
        lambda: None,
        serialize_reads=False,
    )

    def check_cancelled():
        nonlocal calls
        calls += 1

    block = session.read_samples(0, 1000, check_cancelled=check_cancelled)

    assert block.values_v.shape == (1000, 2)
    assert calls == 2


def test_access_session_rejects_changed_source_and_closes_reader():
    changed = False

    def validate_source():
        if changed:
            raise RecordingAccessError("RECORDING_SOURCE_CHANGED", "changed")

    session, reader = _session(np.ones((2, 2)), validate_source)
    changed = True
    with pytest.raises(RecordingAccessError, match="changed"):
        session.read_samples(0, 1)

    session.close()
    assert reader.closed
    with pytest.raises(RecordingAccessError, match="已关闭"):
        session.read_samples(0, 1)


def test_access_session_rejects_out_of_range_and_unknown_channels():
    session, _ = _session(np.ones((2, 2)))

    with pytest.raises(RecordingAccessError, match="半开样本区间"):
        session.read_samples(0, 3)
    with pytest.raises(RecordingAccessError, match="通道不存在"):
        session.read_samples(0, 1, ("Cz",))
