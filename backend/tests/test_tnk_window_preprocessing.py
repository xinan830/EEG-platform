from types import SimpleNamespace

import numpy as np

from app.services.tnk_window_preprocessing import prepare_tnk_window


class _Session:
    def __init__(self, values, sfreq=100.0):
        self.values = np.asarray(values, dtype=np.float64)
        self.sampling_rate_hz = sfreq
        self.sample_count = len(self.values)
        self.channel_names = ("Fz",)
        self.reads = []

    def read_samples(self, start, end, channels, *, check_cancelled=None):
        self.reads.append((start, end, tuple(channels)))
        if check_cancelled:
            check_cancelled()
        return SimpleNamespace(
            values_v=self.values[start:end],
            unavailable_intervals=(),
        )

    def close(self):
        pass


class _Recordings:
    def __init__(self, session):
        self.session = session

    def open_access_session(self, recording, **kwargs):
        assert kwargs == {"verify_content_hash": False}
        return self.session


def test_tnk_preparation_reads_only_bounded_context():
    session = _Session(np.ones((10_000, 1)))
    prepared = prepare_tnk_window(
        _Recordings(session), SimpleNamespace(), start_sample=4_000,
        end_sample=4_400, channels=["Fz"], low_hz=1.0, high_hz=30.0,
        notch_hz=None,
    )

    assert session.reads == [(3_800, 4_600, ("Fz",))]
    assert prepared.data.shape == (400, 1)
    assert prepared.evidence["algorithm_version"] == "tnk-window-spectral-v1"


def test_tnk_preparation_clamps_context_at_recording_edge():
    session = _Session(np.ones((1_000, 1)))
    prepared = prepare_tnk_window(
        _Recordings(session), SimpleNamespace(), start_sample=0, end_sample=400,
        channels=["Fz"], low_hz=1.0, high_hz=30.0, notch_hz=None,
    )

    assert session.reads == [(0, 600, ("Fz",))]
    assert prepared.data.shape == (400, 1)
